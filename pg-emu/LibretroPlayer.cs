using Godot;
using PGEmu.app;
using System;
using System.IO;
using System.Linq;
using System.Threading;

public partial class LibretroPlayer : Control, ILibretroFrontend
{
    private TextureRect _videoRect = null!;
    private Label _statusLabel = null!;
    private Button _stopButton = null!;
    private LibretroHost? _host;
    private FrameData? _pendingFrame;
    private ImageTexture? _videoTexture;
    private string? _configPath;
    private PlatformConfig? _platform;
    private string? _gamePath;
    private int _inputBits;
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
        public RetroPixelFormat Format { get; }

        public FrameData(byte[] buffer, int width, int height, int pitch, RetroPixelFormat format)
        {
            Buffer = buffer;
            Width = width;
            Height = height;
            Pitch = pitch;
            Format = format;
        }
    }

    public override void _Ready()
    {
        _videoRect = GetNode<TextureRect>("VideoRect");
        _statusLabel = GetNode<Label>("StatusLabel");
        _stopButton = GetNode<Button>("StopButton");
        _stopButton.Pressed += OnStopPressed;
        SetProcess(true);
        InitializeHost();
    }

    public override void _Process(double delta)
    {
        UpdateInputSnapshot();
        ApplyPendingError();
        ProcessPendingFrame();
    }

    public override void _ExitTree()
    {
        StopHost();
    }

    private void InitializeHost()
    {
        try
        {
            var tree = GetTree();
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
            var hostConfig = _platform.Libretro.WithResolvedCorePath(resolvedCore);

            _host = new LibretroHost(hostConfig);
            _host.Start(_gamePath, this);
            SetStatus($"Running {_platform.Name}");
        }
        catch (Exception ex)
        {
            SetStatus($"Libretro failed: {ex.Message}", true);
        }
    }

    private void StopHost()
    {
        if (_host == null)
            return;

        _stopButton.Disabled = true;
        try
        {
            _host.Stop();
        }
        catch (Exception ex)
        {
            GD.PrintErr("Error stopping Libretro host: ", ex);
        }
        finally
        {
            _host.Dispose();
            _host = null;
        }
    }

    private void ReturnToLibrary()
    {
        var tree = GetTree();
        if (_platform != null)
            tree.SetMeta("pgemu_selected_platform_id", _platform.Id);

        if (!string.IsNullOrWhiteSpace(_configPath))
            tree.SetMeta("pgemu_config_path", _configPath);

        tree.ChangeSceneToFile("res://GameSelect.tscn");
    }

    private void OnStopPressed()
    {
        StopHost();
        ReturnToLibrary();
    }

    private void UpdateInputSnapshot()
    {
        var bits = 0;
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

    public void SubmitFrame(byte[] buffer, int width, int height, int pitch, RetroPixelFormat format)
    {
        var frame = new FrameData(buffer, width, height, pitch, format);
        Interlocked.Exchange(ref _pendingFrame, frame);
    }

    public short GetInputState(int port, int device, int index, int id)
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

    public void OnHostError(string message)
    {
        Interlocked.Exchange(ref _pendingError, message);
    }

    private void ProcessPendingFrame()
    {
        var frame = Interlocked.Exchange(ref _pendingFrame, null);
        if (frame == null)
            return;

        try
        {
            var rgba = ConvertToRgba(frame);
            var image = Image.CreateFromData(frame.Width, frame.Height, false, Image.Format.Rgba8, rgba);
            _videoTexture = ImageTexture.CreateFromImage(image);
            _videoRect.Texture = _videoTexture;
        }
        catch (Exception ex)
        {
            SetStatus($"Frame render failed: {ex.Message}", true);
        }
    }

    private static byte[] ConvertToRgba(FrameData frame)
    {
        var output = new byte[frame.Width * frame.Height * 4];
        if (frame.Format == RetroPixelFormat.Xrgb8888)
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
        else if (frame.Format == RetroPixelFormat.Rgb565)
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
