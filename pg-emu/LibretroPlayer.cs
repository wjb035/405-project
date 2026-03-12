using Godot;
using PGEmu.app;
using System;
using System.IO;
using System.Linq;
using System.Threading;

public partial class LibretroPlayer : Control
{
    private TextureRect _videoRect = null!;
    private Label _statusLabel = null!;
    private Button _stopButton = null!;
    private LibretroHost? _host;
    private readonly FrontendBridge _frontend = new();
    private ImageTexture? _videoTexture;
    private string? _configPath;
    private PlatformConfig? _platform;
    private string? _gamePath;

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
    private const int InputBitL = 1 << 10;
    private const int InputBitR = 1 << 11;
    private const int InputBitL2 = 1 << 12;
    private const int InputBitR2 = 1 << 13;
    private const int InputBitL3 = 1 << 14;
    private const int InputBitR3 = 1 << 15;

    private const int RetroDeviceJoypad = 1;
    private const int RetroDeviceAnalog = 5;
    private const int RetroDeviceMask = 0xFF;
    private const int RetroAnalogIndexLeft = 0;
    private const int RetroAnalogIndexRight = 1;
    private const int RetroAnalogIdX = 0;
    private const int RetroAnalogIdY = 1;

    private const float StickDeadzone = 0.55f;
    private const float TriggerDeadzone = 0.45f;

    private const string ActionUp = "pgemu_up";
    private const string ActionDown = "pgemu_down";
    private const string ActionLeft = "pgemu_left";
    private const string ActionRight = "pgemu_right";
    private const string ActionA = "pgemu_a";
    private const string ActionB = "pgemu_b";
    private const string ActionX = "pgemu_x";
    private const string ActionY = "pgemu_y";
    private const string ActionStart = "pgemu_start";
    private const string ActionSelect = "pgemu_select";
    private const string ActionL = "pgemu_l";
    private const string ActionR = "pgemu_r";
    private const string ActionL2 = "pgemu_l2";
    private const string ActionR2 = "pgemu_r2";
    private const string ActionL3 = "pgemu_l3";
    private const string ActionR3 = "pgemu_r3";

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

    private sealed class FrontendBridge : ILibretroFrontend
    {
        private FrameData? _pendingFrame;
        private int _inputBits;
        private int _analogLeftX;
        private int _analogLeftY;
        private int _analogRightX;
        private int _analogRightY;
        private string? _pendingError;

        public void Reset()
        {
            Interlocked.Exchange(ref _pendingFrame, null);
            Volatile.Write(ref _inputBits, 0);
            Volatile.Write(ref _analogLeftX, 0);
            Volatile.Write(ref _analogLeftY, 0);
            Volatile.Write(ref _analogRightX, 0);
            Volatile.Write(ref _analogRightY, 0);
            Interlocked.Exchange(ref _pendingError, null);
        }

        public void UpdateInputState(int bits, int leftX, int leftY, int rightX, int rightY)
        {
            Volatile.Write(ref _inputBits, bits);
            Volatile.Write(ref _analogLeftX, leftX);
            Volatile.Write(ref _analogLeftY, leftY);
            Volatile.Write(ref _analogRightX, rightX);
            Volatile.Write(ref _analogRightY, rightY);
        }

        public FrameData? TakePendingFrame()
        {
            return Interlocked.Exchange(ref _pendingFrame, null);
        }

        public string? TakePendingError()
        {
            return Interlocked.Exchange(ref _pendingError, null);
        }

        public void SubmitFrame(byte[] buffer, int width, int height, int pitch, RetroPixelFormat format)
        {
            var frame = new FrameData(buffer, width, height, pitch, format);
            Interlocked.Exchange(ref _pendingFrame, frame);
        }

        public short GetInputState(int port, int device, int index, int id)
        {
            if (port != 0)
                return 0;

            var deviceType = device & RetroDeviceMask;
            if (deviceType == RetroDeviceAnalog)
                return GetAnalogState(index, id);

            if (deviceType != RetroDeviceJoypad && deviceType != 0)
                return 0;

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
                10 => IsPressed(bits, InputBitL),
                11 => IsPressed(bits, InputBitR),
                12 => IsPressed(bits, InputBitL2),
                13 => IsPressed(bits, InputBitR2),
                14 => IsPressed(bits, InputBitL3),
                15 => IsPressed(bits, InputBitR3),
                _ => 0
            };
        }

        private short GetAnalogState(int index, int id)
        {
            if (id != RetroAnalogIdX && id != RetroAnalogIdY)
                return 0;

            var value = index switch
            {
                RetroAnalogIndexLeft when id == RetroAnalogIdX => Volatile.Read(ref _analogLeftX),
                RetroAnalogIndexLeft when id == RetroAnalogIdY => Volatile.Read(ref _analogLeftY),
                RetroAnalogIndexRight when id == RetroAnalogIdX => Volatile.Read(ref _analogRightX),
                RetroAnalogIndexRight when id == RetroAnalogIdY => Volatile.Read(ref _analogRightY),
                _ => 0
            };

            return (short)value;
        }

        public void OnHostError(string message)
        {
            Interlocked.Exchange(ref _pendingError, message);
        }
    }

    public override void _Ready()
    {
        _videoRect = GetNode<TextureRect>("VideoRect");
        _statusLabel = GetNode<Label>("StatusLabel");
        _stopButton = GetNode<Button>("StopButton");
        _stopButton.Pressed += OnStopPressed;
        EnsureInputActions();
        FocusMode = FocusModeEnum.All;
        GrabFocus();
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

            _frontend.Reset();
            _host = new LibretroHost(hostConfig);
            _host.Start(_gamePath, _frontend);
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
        var upPressed = Input.IsActionPressed(ActionUp)
                        || IsAnyPhysicalKeyPressed(Key.Up, Key.W)
                        || IsAnyJoyButtonPressed(JoyButton.DpadUp)
                        || IsAnyJoyAxisBelow(JoyAxis.LeftY, -StickDeadzone);

        var downPressed = Input.IsActionPressed(ActionDown)
                          || IsAnyPhysicalKeyPressed(Key.Down, Key.S)
                          || IsAnyJoyButtonPressed(JoyButton.DpadDown)
                          || IsAnyJoyAxisAbove(JoyAxis.LeftY, StickDeadzone);

        var leftPressed = Input.IsActionPressed(ActionLeft)
                          || IsAnyPhysicalKeyPressed(Key.Left, Key.A)
                          || IsAnyJoyButtonPressed(JoyButton.DpadLeft)
                          || IsAnyJoyAxisBelow(JoyAxis.LeftX, -StickDeadzone);

        var rightPressed = Input.IsActionPressed(ActionRight)
                           || IsAnyPhysicalKeyPressed(Key.Right, Key.D)
                           || IsAnyJoyButtonPressed(JoyButton.DpadRight)
                           || IsAnyJoyAxisAbove(JoyAxis.LeftX, StickDeadzone);

        var aPressed = Input.IsActionPressed(ActionA)
                       || IsAnyPhysicalKeyPressed(Key.Enter, Key.Space, Key.Z)
                       || IsAnyJoyButtonPressed(JoyButton.A);

        var bPressed = Input.IsActionPressed(ActionB)
                       || IsAnyPhysicalKeyPressed(Key.Escape, Key.Backspace, Key.X)
                       || IsAnyJoyButtonPressed(JoyButton.B);

        var xPressed = Input.IsActionPressed(ActionX)
                       || IsAnyPhysicalKeyPressed(Key.C)
                       || IsAnyJoyButtonPressed(JoyButton.X);

        var yPressed = Input.IsActionPressed(ActionY)
                       || IsAnyPhysicalKeyPressed(Key.V)
                       || IsAnyJoyButtonPressed(JoyButton.Y);

        var startPressed = Input.IsActionPressed(ActionStart)
                           || IsAnyPhysicalKeyPressed(Key.P)
                           || IsAnyJoyButtonPressed(JoyButton.Start);

        var selectPressed = Input.IsActionPressed(ActionSelect)
                            || IsAnyPhysicalKeyPressed(Key.O, Key.Tab)
                            || IsAnyJoyButtonPressed(JoyButton.Back);

        var lPressed = Input.IsActionPressed(ActionL)
                       || IsAnyPhysicalKeyPressed(Key.Q)
                       || IsAnyJoyButtonPressed(JoyButton.LeftShoulder);

        var rPressed = Input.IsActionPressed(ActionR)
                       || IsAnyPhysicalKeyPressed(Key.E)
                       || IsAnyJoyButtonPressed(JoyButton.RightShoulder);

        var l2Pressed = Input.IsActionPressed(ActionL2)
                        || IsAnyPhysicalKeyPressed(Key.R)
                        || IsAnyJoyAxisAbove(JoyAxis.TriggerLeft, TriggerDeadzone);

        var r2Pressed = Input.IsActionPressed(ActionR2)
                        || IsAnyPhysicalKeyPressed(Key.T)
                        || IsAnyJoyAxisAbove(JoyAxis.TriggerRight, TriggerDeadzone);

        var l3Pressed = Input.IsActionPressed(ActionL3)
                        || IsAnyPhysicalKeyPressed(Key.F)
                        || IsAnyJoyButtonPressed(JoyButton.LeftStick);

        var r3Pressed = Input.IsActionPressed(ActionR3)
                        || IsAnyPhysicalKeyPressed(Key.G)
                        || IsAnyJoyButtonPressed(JoyButton.RightStick);

        var bits = 0;
        var leftX = 0f;
        var leftY = 0f;
        var rightX = 0f;
        var rightY = 0f;

        foreach (var deviceId in Input.GetConnectedJoypads())
        {
            leftX = Input.GetJoyAxis(deviceId, JoyAxis.LeftX);
            leftY = Input.GetJoyAxis(deviceId, JoyAxis.LeftY);
            rightX = Input.GetJoyAxis(deviceId, JoyAxis.RightX);
            rightY = Input.GetJoyAxis(deviceId, JoyAxis.RightY);
            break;
        }

        var digitalX = 0f;
        if (leftPressed && !rightPressed) digitalX = -1f;
        else if (rightPressed && !leftPressed) digitalX = 1f;

        var digitalY = 0f;
        if (upPressed && !downPressed) digitalY = -1f;
        else if (downPressed && !upPressed) digitalY = 1f;

        if (Mathf.Abs(leftX) < StickDeadzone && digitalX != 0f)
            leftX = digitalX;

        if (Mathf.Abs(leftY) < StickDeadzone && digitalY != 0f)
            leftY = digitalY;

        if (upPressed) bits |= InputBitUp;
        if (downPressed) bits |= InputBitDown;
        if (leftPressed) bits |= InputBitLeft;
        if (rightPressed) bits |= InputBitRight;
        if (aPressed) bits |= InputBitA;
        if (bPressed) bits |= InputBitB;
        if (xPressed) bits |= InputBitX;
        if (yPressed) bits |= InputBitY;
        if (startPressed) bits |= InputBitStart;
        if (selectPressed) bits |= InputBitSelect;
        if (lPressed) bits |= InputBitL;
        if (rPressed) bits |= InputBitR;
        if (l2Pressed) bits |= InputBitL2;
        if (r2Pressed) bits |= InputBitR2;
        if (l3Pressed) bits |= InputBitL3;
        if (r3Pressed) bits |= InputBitR3;

        _frontend.UpdateInputState(
            bits,
            ToRetroAnalog(leftX),
            ToRetroAnalog(leftY),
            ToRetroAnalog(rightX),
            ToRetroAnalog(rightY));
    }

    private static int ToRetroAnalog(float axis)
    {
        if (Mathf.Abs(axis) < 0.08f)
            return 0;

        var clamped = Mathf.Clamp(axis, -1f, 1f);
        return Mathf.RoundToInt(clamped * short.MaxValue);
    }

    private static bool IsAnyJoyAxisBelow(JoyAxis axis, float threshold)
    {
        foreach (var deviceId in Input.GetConnectedJoypads())
        {
            if (Input.GetJoyAxis(deviceId, axis) <= threshold)
                return true;
        }

        return false;
    }

    private static bool IsAnyJoyAxisAbove(JoyAxis axis, float threshold)
    {
        foreach (var deviceId in Input.GetConnectedJoypads())
        {
            if (Input.GetJoyAxis(deviceId, axis) >= threshold)
                return true;
        }

        return false;
    }

    private static bool IsAnyJoyButtonPressed(JoyButton button)
    {
        foreach (var deviceId in Input.GetConnectedJoypads())
        {
            if (Input.IsJoyButtonPressed(deviceId, button))
                return true;
        }

        return false;
    }

    private static bool IsAnyPhysicalKeyPressed(params Key[] keys)
    {
        foreach (var key in keys)
        {
            if (Input.IsPhysicalKeyPressed(key))
                return true;
        }

        return false;
    }

    private static void EnsureInputActions()
    {
        EnsureAction(ActionUp,
            Key.Up, Key.W,
            JoyButton.DpadUp);

        EnsureAction(ActionDown,
            Key.Down, Key.S,
            JoyButton.DpadDown);

        EnsureAction(ActionLeft,
            Key.Left, Key.A,
            JoyButton.DpadLeft);

        EnsureAction(ActionRight,
            Key.Right, Key.D,
            JoyButton.DpadRight);

        EnsureAction(ActionA,
            Key.Enter, Key.Space, Key.Z,
            JoyButton.A);

        EnsureAction(ActionB,
            Key.Escape, Key.Backspace, Key.X,
            JoyButton.B);

        EnsureAction(ActionX,
            Key.C,
            JoyButton.X);

        EnsureAction(ActionY,
            Key.V,
            JoyButton.Y);

        EnsureAction(ActionStart,
            Key.P,
            JoyButton.Start);

        EnsureAction(ActionSelect,
            Key.O, Key.Tab,
            JoyButton.Back);

        EnsureAction(ActionL,
            Key.Q,
            JoyButton.LeftShoulder);

        EnsureAction(ActionR,
            Key.E,
            JoyButton.RightShoulder);

        EnsureAction(ActionL2,
            Key.R);

        EnsureAction(ActionR2,
            Key.T);

        EnsureAction(ActionL3,
            Key.F,
            JoyButton.LeftStick);

        EnsureAction(ActionR3,
            Key.G,
            JoyButton.RightStick);
    }

    private static void EnsureAction(string actionName, Key primaryKey, Key secondaryKey, JoyButton joyButton)
    {
        if (InputMap.HasAction(actionName))
            return;

        InputMap.AddAction(actionName);
        InputMap.ActionAddEvent(actionName, CreateKeyEvent(primaryKey));
        InputMap.ActionAddEvent(actionName, CreateKeyEvent(secondaryKey));
        InputMap.ActionAddEvent(actionName, CreateJoyButtonEvent(joyButton));
    }

    private static void EnsureAction(string actionName, Key primaryKey, Key secondaryKey, Key tertiaryKey, JoyButton joyButton)
    {
        if (InputMap.HasAction(actionName))
            return;

        InputMap.AddAction(actionName);
        InputMap.ActionAddEvent(actionName, CreateKeyEvent(primaryKey));
        InputMap.ActionAddEvent(actionName, CreateKeyEvent(secondaryKey));
        InputMap.ActionAddEvent(actionName, CreateKeyEvent(tertiaryKey));
        InputMap.ActionAddEvent(actionName, CreateJoyButtonEvent(joyButton));
    }

    private static void EnsureAction(string actionName, Key key, JoyButton joyButton)
    {
        if (InputMap.HasAction(actionName))
            return;

        InputMap.AddAction(actionName);
        InputMap.ActionAddEvent(actionName, CreateKeyEvent(key));
        InputMap.ActionAddEvent(actionName, CreateJoyButtonEvent(joyButton));
    }

    private static void EnsureAction(string actionName, Key key)
    {
        if (InputMap.HasAction(actionName))
            return;

        InputMap.AddAction(actionName);
        InputMap.ActionAddEvent(actionName, CreateKeyEvent(key));
    }

    private static InputEventKey CreateKeyEvent(Key key)
    {
        return new InputEventKey
        {
            PhysicalKeycode = key
        };
    }

    private static InputEventJoypadButton CreateJoyButtonEvent(JoyButton button)
    {
        return new InputEventJoypadButton
        {
            ButtonIndex = button,
            Pressed = true
        };
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
        var pendingError = _frontend.TakePendingError();
        if (string.IsNullOrWhiteSpace(pendingError))
            return;

        SetStatus(pendingError, true);
    }

    private void ProcessPendingFrame()
    {
        var frame = _frontend.TakePendingFrame();
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
