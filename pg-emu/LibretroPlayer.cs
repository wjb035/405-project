using Godot;
using PGEmu.app;
using PGEmu.Emu.Libretro;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

// Godot scene that runs a Libretro core *in-process* and displays its video output.
//
// Flow (mirrors CatUI's approach at a smaller scope):
// - `GameSelect` decides whether to launch externally or via Libretro based on `PlatformConfig.Libretro`.
// - When launching via Libretro, `GameSelect` stores a small launch context in `SceneTree` metadata.
// - This node reads that metadata, resolves the core path relative to `config.json`, starts `LibretroRunner`,
//   and renders frames into a `TextureRect`.
//
// Threading:
// - `LibretroRunner` runs the core on a background thread.
// - The core's callbacks may arrive on that background thread, so we only store the most recent frame/error
//   using `Interlocked.Exchange` and apply them during `_Process()` on Godot's main thread.
public partial class LibretroPlayer : Control
{
    private readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    private TextureRect _videoRect = null!;
    private Label _statusLabel = null!;
    private Button _stopButton = null!;
    private LibretroRunner? _runner;
    private RunnerFrontendBridge? _frontendBridge;
    private FrameData? _pendingFrame;
    private ImageTexture? _videoTexture;
    private string? _configPath;
    private PlatformConfig? _platform;
    private string? _gamePath;
    private int _inputBits;
    private int _mainThreadId;
    private int _mainThreadDrainScheduled;
    private string? _pendingError;

    private const int InputBitB = 1 << 0;
    private const int InputBitY = 1 << 1;
    private const int InputBitSelect = 1 << 2;
    private const int InputBitStart = 1 << 3;
    private const int InputBitUp = 1 << 4;
    private const int InputBitDown = 1 << 5;
    private const int InputBitLeft = 1 << 6;
    private const int InputBitRight = 1 << 7;
    private const int InputBitA = 1 << 8;
    private const int InputBitX = 1 << 9;

    private sealed class FrameData
    {
        public byte[] Buffer { get; }
        public int Width { get; }
        public int Height { get; }
        public int Pitch { get; }
        public LibretroPixelFormat Format { get; }

        public FrameData(byte[] buffer, int width, int height, int pitch, LibretroPixelFormat format)
        {
            Buffer = buffer;
            Width = width;
            Height = height;
            Pitch = pitch;
            Format = format;
        }
    }

    // Plain managed bridge used by `LibretroRunner` from its background thread.
    // This avoids invoking methods on a Godot `Node` instance from non-main threads.
    private sealed class RunnerFrontendBridge : ILibretroRunnerFrontend
    {
        private readonly Func<int, int, int, int, short> _inputReader;
        private readonly Action<FrameData> _frameSink;
        private readonly Action<string> _errorSink;

        public RunnerFrontendBridge(
            Func<int, int, int, int, short> inputReader,
            Action<FrameData> frameSink,
            Action<string> errorSink)
        {
            _inputReader = inputReader;
            _frameSink = frameSink;
            _errorSink = errorSink;
        }

        public void SubmitFrame(byte[] buffer, int width, int height, int pitch, LibretroPixelFormat format)
        {
            _frameSink(new FrameData(buffer, width, height, pitch, format));
        }

        public short GetInputState(int port, int device, int index, int id)
        {
            return _inputReader(port, device, index, id);
        }

        public void OnRunnerError(string message)
        {
            _errorSink(message);
        }
    }

    public override void _Ready()
    {
        _mainThreadId = System.Environment.CurrentManagedThreadId;
        LogThread("Ready");
        _videoRect = GetNode<TextureRect>("VideoRect");
        _statusLabel = GetNode<Label>("StatusLabel");
        _stopButton = GetNode<Button>("StopButton");
        _stopButton.Pressed += OnStopPressed;
        SetProcess(true);
        InitializeRunner();
    }

    public override void _Process(double delta)
    {
        if (!EnsureMainThread(nameof(_Process)))
            return;

        DrainMainThreadQueue();
        UpdateInputSnapshot();
        ApplyPendingError();
        ProcessPendingFrame();
    }

    public override void _ExitTree()
    {
        if (EnsureMainThread(nameof(_ExitTree)))
            LogThread("ExitTree");

        StopRunnerBackend();
    }

    private void InitializeRunner()
    {
        if (!EnsureMainThread(nameof(InitializeRunner)))
            return;

        try
        {
            var tree = GetTree();
            // Launch context set by `GameSelect.LaunchViaLibretro(...)`.
            _configPath = tree.HasMeta("pgemu_config_path") ? tree.GetMeta("pgemu_config_path").AsString() : null;
            _gamePath = tree.HasMeta("pgemu_libretro_game_path") ? tree.GetMeta("pgemu_libretro_game_path").AsString() : null;
            var platformId = tree.HasMeta("pgemu_libretro_platform_id") ? tree.GetMeta("pgemu_libretro_platform_id").AsString() : null;

            if (string.IsNullOrWhiteSpace(_configPath) || string.IsNullOrWhiteSpace(_gamePath) || string.IsNullOrWhiteSpace(platformId))
            {
                SetStatus("Libretro launch context missing.", true);
                return;
            }

            var config = AppConfig.Load(_configPath);
            _platform = config.Platforms.FirstOrDefault(p => string.Equals(p.Id, platformId, StringComparison.OrdinalIgnoreCase));
            if (_platform?.Libretro == null)
            {
                SetStatus("Selected platform has no Libretro configuration.", true);
                return;
            }

            var baseDir = Path.GetDirectoryName(_configPath);
            var resolvedCore = _platform.Libretro.ResolveCorePath(baseDir);
            var runnerConfig = _platform.Libretro.WithResolvedCorePath(resolvedCore);

            _frontendBridge = new RunnerFrontendBridge(
                ReadInputState,
                frame => Interlocked.Exchange(ref _pendingFrame, frame),
                message => Interlocked.Exchange(ref _pendingError, message));

            _runner = new LibretroRunner(runnerConfig);
            _runner.Start(_gamePath, _frontendBridge);
            SetStatus($"Running {_platform.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"Libretro failed: {ex.Message}", true);
        }
    }

    private void StopRunnerBackend()
    {
        var runner = _runner;
        if (runner == null)
            return;

        _runner = null;
        _frontendBridge = null;
        try
        {
            runner.Stop();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LibretroPlayer][thread {System.Environment.CurrentManagedThreadId}] Error stopping runner: {ex}");
        }
        finally
        {
            runner.Dispose();
        }
    }

    private void ReturnToLibraryMainThread()
    {
        if (!EnsureMainThread(nameof(ReturnToLibraryMainThread)))
            return;

        LogThread("ChangeSceneToFile -> res://GameSelect.tscn");
        var tree = GetTree();
        if (_platform != null)
            tree.SetMeta("pgemu_selected_platform_id", _platform.Id);

        if (!string.IsNullOrWhiteSpace(_configPath))
            tree.SetMeta("pgemu_config_path", _configPath);

        tree.ChangeSceneToFile("res://GameSelect.tscn");
    }

    private void OnStopPressed()
    {
        if (!EnsureMainThread(nameof(OnStopPressed)))
            return;

        _stopButton.Disabled = true;
        StopRunnerBackend();
        EnqueueMainThread(ReturnToLibraryMainThread);
    }

    private void UpdateInputSnapshot()
    {
        var bits = 0;
        // Minimal mapping for libretro joypad IDs (RETRO_DEVICE_JOYPAD):
        // 0=B, 1=Y, 2=Select, 3=Start, 4=Up, 5=Down, 6=Left, 7=Right, 8=A, 9=X.
        //
        // CatUI uses a more complete per-core input mapper; for PGEmu we keep this lightweight.
        if (Input.IsActionPressed("ui_up")) bits |= InputBitUp;
        if (Input.IsActionPressed("ui_down")) bits |= InputBitDown;
        if (Input.IsActionPressed("ui_left")) bits |= InputBitLeft;
        if (Input.IsActionPressed("ui_right")) bits |= InputBitRight;
        if (Input.IsActionPressed("ui_accept")) bits |= InputBitA;
        if (Input.IsActionPressed("ui_cancel")) bits |= InputBitB;
        if (Input.IsActionPressed("ui_select")) bits |= InputBitX;
        if (Input.IsActionPressed("ui_page_up")) bits |= InputBitY;
        if (Input.IsActionPressed("ui_focus_next")) bits |= InputBitStart;
        if (Input.IsActionPressed("ui_focus_prev")) bits |= InputBitSelect;

        Volatile.Write(ref _inputBits, bits);
    }

    private void SetStatus(string message, bool fatal = false)
    {
        if (string.IsNullOrEmpty(message))
            return;

        if (!EnsureMainThread(nameof(SetStatus)))
        {
            EnqueueMainThread(() => SetStatus(message, fatal));
            return;
        }

        LogThread($"Status update: {message}");
        if (_statusLabel != null)
            _statusLabel.Text = message;

        if (fatal && _stopButton != null)
            _stopButton.Disabled = true;
    }

    private void ApplyPendingError()
    {
        var pendingError = Interlocked.Exchange(ref _pendingError, null);
        if (string.IsNullOrWhiteSpace(pendingError))
            return;

        SetStatus(pendingError, true);
    }

    private short ReadInputState(int port, int device, int index, int id)
    {
        var bits = Volatile.Read(ref _inputBits);
        return id switch
        {
            0 => IsPressed(bits, InputBitB),
            1 => IsPressed(bits, InputBitY),
            2 => IsPressed(bits, InputBitSelect),
            3 => IsPressed(bits, InputBitStart),
            4 => IsPressed(bits, InputBitUp),
            5 => IsPressed(bits, InputBitDown),
            6 => IsPressed(bits, InputBitLeft),
            7 => IsPressed(bits, InputBitRight),
            8 => IsPressed(bits, InputBitA),
            9 => IsPressed(bits, InputBitX),
            _ => 0
        };
    }

    private void ProcessPendingFrame()
    {
        if (!EnsureMainThread(nameof(ProcessPendingFrame)))
            return;

        var frame = Interlocked.Exchange(ref _pendingFrame, null);
        if (frame == null)
            return;

        try
        {
            var rgba = ConvertToRgba(frame);
            var image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, rgba);
            _videoTexture = ImageTexture.CreateFromImage(image);
            LogThread($"Texture assignment: {frame.Width}x{frame.Height}");
            _videoRect.Texture = _videoTexture;
        }
        catch (Exception ex)
        {
            SetStatus($"Frame render failed: {ex.Message}", true);
        }
    }

    private void EnqueueMainThread(Action action)
    {
        _mainThreadQueue.Enqueue(action);
        if (Interlocked.Exchange(ref _mainThreadDrainScheduled, 1) == 0)
            CallDeferred(nameof(DrainMainThreadQueueDeferred));
    }

    private void DrainMainThreadQueueDeferred()
    {
        if (!EnsureMainThread(nameof(DrainMainThreadQueueDeferred)))
            return;

        DrainMainThreadQueue();
    }

    private void DrainMainThreadQueue()
    {
        if (!EnsureMainThread(nameof(DrainMainThreadQueue)))
            return;

        while (_mainThreadQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                GD.PrintErr("Deferred main-thread action failed: ", ex);
            }
        }

        Interlocked.Exchange(ref _mainThreadDrainScheduled, 0);
        if (!_mainThreadQueue.IsEmpty && Interlocked.Exchange(ref _mainThreadDrainScheduled, 1) == 0)
            CallDeferred(nameof(DrainMainThreadQueueDeferred));
    }

    private bool EnsureMainThread(string operation)
    {
        var currentThreadId = System.Environment.CurrentManagedThreadId;
        var isMainThread = _mainThreadId != 0 && currentThreadId == _mainThreadId;
        Debug.Assert(isMainThread, $"Operation '{operation}' executed off main thread.");

        if (isMainThread)
            return true;

        GD.PushError($"[LibretroPlayer] '{operation}' executed off main thread (current={currentThreadId}, main={_mainThreadId}).");
        return false;
    }

    private static void LogThread(string operation)
    {
        GD.Print($"[LibretroPlayer][thread {System.Environment.CurrentManagedThreadId}] {operation}");
    }

    private static byte[] ConvertToRgba(FrameData frame)
    {
        var output = new byte[frame.Width * frame.Height * 4];
        if (frame.Format == LibretroPixelFormat.Xrgb8888)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                var srcRow = row * frame.Pitch;
                var dstRow = row * frame.Width * 4;
                for (var col = 0; col < frame.Width; col++)
                {
                    var srcIndex = srcRow + col * 4;
                    var dstIndex = dstRow + col * 4;
                    output[dstIndex + 0] = frame.Buffer[srcIndex + 2];
                    output[dstIndex + 1] = frame.Buffer[srcIndex + 1];
                    output[dstIndex + 2] = frame.Buffer[srcIndex + 0];
                    output[dstIndex + 3] = 255;
                }
            }
        }
        else if (frame.Format == LibretroPixelFormat.ZeroRgb1555)
        {
            // libretro: 16-bit 0RGB1555 (little-endian). 1 unused bit + 5 bits per channel.
            for (var row = 0; row < frame.Height; row++)
            {
                var srcRow = row * frame.Pitch;
                var dstRow = row * frame.Width * 4;
                for (var col = 0; col < frame.Width; col++)
                {
                    var srcIndex = srcRow + col * 2;
                    var value = (ushort)(frame.Buffer[srcIndex] | (frame.Buffer[srcIndex + 1] << 8));
                    var dstIndex = dstRow + col * 4;
                    output[dstIndex + 0] = (byte)(((value >> 10) & 0x1F) << 3);
                    output[dstIndex + 1] = (byte)(((value >> 5) & 0x1F) << 3);
                    output[dstIndex + 2] = (byte)(((value >> 0) & 0x1F) << 3);
                    output[dstIndex + 3] = 255;
                }
            }
        }
        else if (frame.Format == LibretroPixelFormat.Rgb565)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                var srcRow = row * frame.Pitch;
                var dstRow = row * frame.Width * 4;
                for (var col = 0; col < frame.Width; col++)
                {
                    var srcIndex = srcRow + col * 2;
                    var value = (ushort)(frame.Buffer[srcIndex] | (frame.Buffer[srcIndex + 1] << 8));
                    var dstIndex = dstRow + col * 4;
                    output[dstIndex + 0] = (byte)(((value >> 11) & 0x1F) << 3);
                    output[dstIndex + 1] = (byte)(((value >> 5) & 0x3F) << 2);
                    output[dstIndex + 2] = (byte)(((value >> 0) & 0x1F) << 3);
                    output[dstIndex + 3] = 255;
                }
            }
        }
        else
        {
            // Hardware-rendered frames arrive as RGBA8 from `LibretroRunner` (glReadPixels).
            // For unknown formats we treat the buffer as RGBA-like and force alpha to 255.
            var srcPitch = frame.Pitch > 0 ? frame.Pitch : frame.Width * 4;
            var dstPitch = frame.Width * 4;
            for (var row = 0; row < frame.Height; row++)
            {
                var srcRow = row * srcPitch;
                if (srcRow >= frame.Buffer.Length)
                    break;

                var dstRow = row * dstPitch;
                var copyLength = Math.Min(dstPitch, frame.Buffer.Length - srcRow);
                Buffer.BlockCopy(frame.Buffer, srcRow, output, dstRow, copyLength);
            }

            for (var alpha = 3; alpha < output.Length; alpha += 4)
                output[alpha] = 255;
        }

        return output;
    }

    private static short IsPressed(int bits, int mask)
    {
        return (short)((bits & mask) != 0 ? 1 : 0);
    }
}
