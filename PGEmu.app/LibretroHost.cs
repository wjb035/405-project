using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PGEmu.app;

public enum RetroPixelFormat : uint
{
    Xrgb8888 = 0,
    Rgb565 = 1,
    Xrgb1555 = 2,
    Unknown = uint.MaxValue
}

public interface ILibretroFrontend
{
    void SubmitFrame(byte[] buffer, int width, int height, int pitch, RetroPixelFormat format);
    short GetInputState(int port, int device, int index, int id);
    void OnHostError(string message);
}

public sealed class LibretroHost : IDisposable
{
    private const uint RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY = 9;
    private const uint RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 10;
    private const uint RETRO_ENVIRONMENT_SET_HW_RENDER = 14;
    private const uint RETRO_ENVIRONMENT_GET_VARIABLE = 15;
    private const uint RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE = 17;
    private const uint RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY = 30;
    private const uint RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY = 31;

    private const string OpenGlFrameworkPath = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    private const int kCGLPFAOpenGLProfile = 99;
    private const int kCGLPFAAccelerated = 73;
    private const int kCGLOGLPVersion_3_2_Core = 0x3200;
    private const int kCGLOGLPVersion_GL4_Core = 0x4100;

    private const uint GL_FRAMEBUFFER = 0x8D40;
    private const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    private const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    private const uint GL_TEXTURE_WRAP_S = 0x2802;
    private const uint GL_TEXTURE_WRAP_T = 0x2803;
    private const uint GL_CLAMP_TO_EDGE = 0x812F;
    private const uint GL_LINEAR = 0x2601;
    private const uint GL_RGBA8 = 0x8058;
    private const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    private const uint GL_RGBA = 0x1908;
    private const uint GL_UNSIGNED_BYTE = 0x1401;

    private static readonly IntPtr RETRO_HW_FRAME_BUFFER_VALID = new(-1);

    private delegate void retro_init_t();
    private delegate void retro_deinit_t();
    private delegate bool retro_load_game_t(ref retro_game_info game);
    private delegate void retro_unload_game_t();
    private delegate void retro_run_t();
    private delegate void retro_set_environment_t(retro_environment_t cb);
    private delegate void retro_set_video_refresh_t(retro_video_refresh_t cb);
    private delegate void retro_set_audio_sample_t(retro_audio_sample_t cb);
    private delegate void retro_set_audio_sample_batch_t(retro_audio_sample_batch_t cb);
    private delegate void retro_set_input_poll_t(retro_input_poll_t cb);
    private delegate void retro_set_input_state_t(retro_input_state_t cb);

    private delegate bool retro_environment_t(uint cmd, IntPtr data);
    private delegate void retro_video_refresh_t(IntPtr data, uint width, uint height, uint pitch);
    private delegate void retro_audio_sample_t(short left, short right);
    private delegate nuint retro_audio_sample_batch_t(IntPtr data, nuint frames);
    private delegate void retro_input_poll_t();
    private delegate short retro_input_state_t(int port, int device, int index, int id);

    private delegate void retro_hw_context_reset_t();
    private delegate void retro_hw_context_destroy_t();
    private delegate nuint retro_hw_get_current_framebuffer_t();
    private delegate IntPtr retro_hw_get_proc_address_t(IntPtr symbol);

    private delegate void glGenFramebuffers_t(int n, out uint framebuffers);
    private delegate void glDeleteFramebuffers_t(int n, ref uint framebuffers);
    private delegate void glBindFramebuffer_t(uint target, uint framebuffer);
    private delegate void glGenTextures_t(int n, out uint textures);
    private delegate void glDeleteTextures_t(int n, ref uint textures);
    private delegate void glBindTexture_t(uint target, uint texture);
    private delegate void glTexParameteri_t(uint target, uint pname, int param);
    private delegate void glTexImage2D_t(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, IntPtr data);
    private delegate void glFramebufferTexture2D_t(uint target, uint attachment, uint textarget, uint texture, int level);
    private delegate uint glCheckFramebufferStatus_t(uint target);
    private delegate void glReadPixels_t(int x, int y, int width, int height, uint format, uint type, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct retro_game_info
    {
        public IntPtr path;
        public IntPtr data;
        public uint size;
        public IntPtr meta;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct retro_variable
    {
        public IntPtr key;
        public IntPtr value;
    }

    private enum RetroHwContextType : int
    {
        None = 0,
        OpenGl = 1,
        OpenGlEs2 = 2,
        OpenGlCore = 3,
        OpenGlEs3 = 4,
        OpenGlEsVersion = 5,
        Vulkan = 6,
        D3D11 = 7,
        D3D10 = 8,
        D3D12 = 9,
        D3D9 = 10,
        Metal = 11,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct retro_hw_render_callback
    {
        public RetroHwContextType context_type;
        public IntPtr context_reset;
        public IntPtr get_current_framebuffer;
        public IntPtr get_proc_address;
        [MarshalAs(UnmanagedType.I1)] public bool depth;
        [MarshalAs(UnmanagedType.I1)] public bool stencil;
        [MarshalAs(UnmanagedType.I1)] public bool bottom_left_origin;
        public uint version_major;
        public uint version_minor;
        [MarshalAs(UnmanagedType.I1)] public bool cache_context;
        public IntPtr context_destroy;
        [MarshalAs(UnmanagedType.I1)] public bool debug_context;
    }

    private readonly LibretroConfig _config;
    private readonly retro_environment_t _environmentCallback;
    private readonly retro_video_refresh_t _videoRefreshCallback;
    private readonly retro_audio_sample_t _audioSampleCallback;
    private readonly retro_audio_sample_batch_t _audioSampleBatchCallback;
    private readonly retro_input_poll_t _inputPollCallback;
    private readonly retro_input_state_t _inputStateCallback;
    private readonly retro_hw_get_current_framebuffer_t _hwGetCurrentFramebufferCallback;
    private readonly retro_hw_get_proc_address_t _hwGetProcAddressCallback;
    private readonly List<IntPtr> _allocatedStrings = new();
    private readonly Dictionary<string, IntPtr> _optionValuePointers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IntPtr> _persistentStringPointers = new(StringComparer.Ordinal);
    private readonly ManualResetEventSlim _startupSignal = new(false);

    private ILibretroFrontend? _frontend;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    private IntPtr _coreHandle = IntPtr.Zero;
    private IntPtr _gamePathPtr = IntPtr.Zero;
    private RetroPixelFormat _pixelFormat = RetroPixelFormat.Xrgb8888;
    private Exception? _startupException;

    private retro_init_t? _retroInit;
    private retro_deinit_t? _retroDeinit;
    private retro_load_game_t? _retroLoadGame;
    private retro_unload_game_t? _retroUnloadGame;
    private retro_run_t? _retroRun;
    private retro_set_environment_t? _retroSetEnvironment;
    private retro_set_video_refresh_t? _retroSetVideoRefresh;
    private retro_set_audio_sample_t? _retroSetAudioSample;
    private retro_set_audio_sample_batch_t? _retroSetAudioSampleBatch;
    private retro_set_input_poll_t? _retroSetInputPoll;
    private retro_set_input_state_t? _retroSetInputState;

    private bool _hwRenderRequested;
    private string? _hwRenderFailureReason;
    private bool _hwBottomLeftOrigin;
    private int _hwVersionMajor;
    private int _hwVersionMinor;
    private RetroHwContextType _hwContextType = RetroHwContextType.None;
    private retro_hw_context_reset_t? _hwContextReset;
    private retro_hw_context_destroy_t? _hwContextDestroy;

    private IntPtr _cglContext = IntPtr.Zero;
    private IntPtr _openGlLibrary = IntPtr.Zero;
    private glGenFramebuffers_t? _glGenFramebuffers;
    private glDeleteFramebuffers_t? _glDeleteFramebuffers;
    private glBindFramebuffer_t? _glBindFramebuffer;
    private glGenTextures_t? _glGenTextures;
    private glDeleteTextures_t? _glDeleteTextures;
    private glBindTexture_t? _glBindTexture;
    private glTexParameteri_t? _glTexParameteri;
    private glTexImage2D_t? _glTexImage2D;
    private glFramebufferTexture2D_t? _glFramebufferTexture2D;
    private glCheckFramebufferStatus_t? _glCheckFramebufferStatus;
    private glReadPixels_t? _glReadPixels;
    private uint _hwFramebufferId;
    private uint _hwColorTextureId;
    private int _hwFramebufferWidth;
    private int _hwFramebufferHeight;

    public LibretroHost(LibretroConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _environmentCallback = EnvironmentCallback;
        _videoRefreshCallback = VideoRefresh;
        _audioSampleCallback = AudioSample;
        _audioSampleBatchCallback = AudioSampleBatch;
        _inputPollCallback = InputPoll;
        _inputStateCallback = InputState;
        _hwGetCurrentFramebufferCallback = GetCurrentFramebuffer;
        _hwGetProcAddressCallback = GetProcAddress;
    }

    public void Start(string romPath, ILibretroFrontend frontend)
    {
        if (string.IsNullOrWhiteSpace(romPath))
            throw new ArgumentException("ROM path is required", nameof(romPath));

        if (_frontend != null || _loopTask != null)
            throw new InvalidOperationException("Libretro host already running.");

        _frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        _startupException = null;
        _hwRenderRequested = false;
        _hwRenderFailureReason = null;
        _startupSignal.Reset();

        _loopCts = new CancellationTokenSource();
        _loopTask = Task.Factory.StartNew(
            () => CoreThreadMain(romPath, _loopCts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        if (!_startupSignal.Wait(TimeSpan.FromSeconds(10)))
        {
            Stop();
            throw new TimeoutException("Timed out waiting for Libretro core startup.");
        }

        if (_startupException != null)
        {
            Stop();
            throw new InvalidOperationException(_startupException.Message, _startupException);
        }
    }

    public void Stop()
    {
        if (_loopCts != null)
        {
            _loopCts.Cancel();
            try
            {
                _loopTask?.Wait(2000);
            }
            catch (AggregateException)
            {
                // Ignore cancellation-time exceptions.
            }
            finally
            {
                _loopTask = null;
                _loopCts.Dispose();
                _loopCts = null;
            }
        }

        _frontend = null;
        _startupException = null;
        _startupSignal.Reset();
    }

    public void Dispose()
    {
        Stop();
        _startupSignal.Dispose();
        GC.SuppressFinalize(this);
    }

    private void CoreThreadMain(string romPath, CancellationToken token)
    {
        try
        {
            EnsureCoreLoaded();
            SetupCallbacks();
            _retroInit?.Invoke();

            var info = BuildGameInfo(romPath);
            if (_retroLoadGame == null || !_retroLoadGame(ref info))
                throw new InvalidOperationException("Libretro core refused to load game.");

            if (!string.IsNullOrWhiteSpace(_hwRenderFailureReason))
                throw new InvalidOperationException(_hwRenderFailureReason);

            if (_hwRenderRequested)
            {
                EnsureHardwareContext();
                _hwContextReset?.Invoke();
            }

            _startupSignal.Set();
            RunLoop(token);
        }
        catch (Exception ex)
        {
            _startupException ??= ex;
            _frontend?.OnHostError($"Libretro runtime error: {ex.Message}");
            _startupSignal.Set();
        }
        finally
        {
            try { _hwContextDestroy?.Invoke(); }
            catch (Exception ex) { _frontend?.OnHostError($"Libretro context destroy failed: {ex.Message}"); }

            DestroyHardwareContext();

            try { _retroUnloadGame?.Invoke(); }
            catch (Exception ex) { _frontend?.OnHostError($"Libretro unload failed: {ex.Message}"); }

            try { _retroDeinit?.Invoke(); }
            catch (Exception ex) { _frontend?.OnHostError($"Libretro deinit failed: {ex.Message}"); }

            FreeGamePath();
            FreeAllocatedStrings();
            FreeCoreHandle();
        }
    }

    private void RunLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            _retroRun?.Invoke();
            Thread.Sleep(1);
        }
    }

    private void EnsureCoreLoaded()
    {
        if (_coreHandle != IntPtr.Zero)
            return;

        if (string.IsNullOrWhiteSpace(_config.CorePath))
            throw new InvalidOperationException("Libretro core path was not configured.");

        if (!File.Exists(_config.CorePath))
            throw new FileNotFoundException($"Libretro core not found at '{_config.CorePath}'.");

        _coreHandle = NativeLibrary.Load(_config.CorePath);
        _retroInit = GetCoreFunction<retro_init_t>("retro_init");
        _retroDeinit = GetCoreFunction<retro_deinit_t>("retro_deinit");
        _retroLoadGame = GetCoreFunction<retro_load_game_t>("retro_load_game");
        _retroUnloadGame = GetCoreFunction<retro_unload_game_t>("retro_unload_game");
        _retroRun = GetCoreFunction<retro_run_t>("retro_run");
        _retroSetEnvironment = GetCoreFunction<retro_set_environment_t>("retro_set_environment");
        _retroSetVideoRefresh = GetCoreFunction<retro_set_video_refresh_t>("retro_set_video_refresh");
        _retroSetAudioSample = GetCoreFunction<retro_set_audio_sample_t>("retro_set_audio_sample");
        _retroSetAudioSampleBatch = GetCoreFunction<retro_set_audio_sample_batch_t>("retro_set_audio_sample_batch");
        _retroSetInputPoll = GetCoreFunction<retro_set_input_poll_t>("retro_set_input_poll");
        _retroSetInputState = GetCoreFunction<retro_set_input_state_t>("retro_set_input_state");
    }

    private void SetupCallbacks()
    {
        _retroSetEnvironment?.Invoke(_environmentCallback);
        _retroSetVideoRefresh?.Invoke(_videoRefreshCallback);
        _retroSetAudioSample?.Invoke(_audioSampleCallback);
        _retroSetAudioSampleBatch?.Invoke(_audioSampleBatchCallback);
        _retroSetInputPoll?.Invoke(_inputPollCallback);
        _retroSetInputState?.Invoke(_inputStateCallback);
    }

    private retro_game_info BuildGameInfo(string romPath)
    {
        FreeGamePath();
        _gamePathPtr = Marshal.StringToHGlobalAnsi(romPath);

        return new retro_game_info
        {
            path = _gamePathPtr,
            data = IntPtr.Zero,
            size = 0,
            meta = IntPtr.Zero
        };
    }

    private bool EnvironmentCallback(uint cmd, IntPtr data)
    {
        switch (cmd)
        {
            case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
                if (data == IntPtr.Zero)
                    return false;
                _pixelFormat = (RetroPixelFormat)Marshal.ReadInt32(data);
                return true;

            case RETRO_ENVIRONMENT_SET_HW_RENDER:
                return HandleSetHwRender(data);

            case RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
                return WriteStringPointer(data, Path.GetDirectoryName(_config.CorePath));

            case RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY:
                return WriteStringPointer(data, Path.GetDirectoryName(_config.CorePath));

            case RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
                return WriteStringPointer(data, Path.GetDirectoryName(_config.CorePath));

            case RETRO_ENVIRONMENT_GET_VARIABLE:
            {
                if (data == IntPtr.Zero)
                    return false;

                var variable = Marshal.PtrToStructure<retro_variable>(data);
                var key = variable.key == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(variable.key);
                variable.value = IntPtr.Zero;

                if (!string.IsNullOrEmpty(key) && _config.Options != null && _config.Options.TryGetValue(key, out var value) && value != null)
                    variable.value = GetOptionPointer(value);

                Marshal.StructureToPtr(variable, data, false);
                return true;
            }

            case RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE:
                if (data != IntPtr.Zero)
                    Marshal.WriteByte(data, 0);
                return true;
        }

        return false;
    }

    private bool HandleSetHwRender(IntPtr data)
    {
        _hwRenderRequested = true;
        _hwRenderFailureReason = null;
        _hwFramebufferId = 0;
        _hwColorTextureId = 0;
        _hwFramebufferWidth = 0;
        _hwFramebufferHeight = 0;

        if (data == IntPtr.Zero)
        {
            _hwRenderFailureReason = "Core provided empty hardware render callback.";
            return false;
        }

        var callback = Marshal.PtrToStructure<retro_hw_render_callback>(data);
        _hwContextType = callback.context_type;
        _hwBottomLeftOrigin = callback.bottom_left_origin;
        _hwVersionMajor = (int)callback.version_major;
        _hwVersionMinor = (int)callback.version_minor;

        if (!SupportsHardwareContext(_hwContextType))
        {
            _hwRenderFailureReason = $"Unsupported hardware context type '{_hwContextType}'.";
            return false;
        }

        if (callback.context_reset == IntPtr.Zero)
        {
            _hwRenderFailureReason = "Core did not provide context_reset callback.";
            return false;
        }

        _hwContextReset = Marshal.GetDelegateForFunctionPointer<retro_hw_context_reset_t>(callback.context_reset);
        _hwContextDestroy = callback.context_destroy == IntPtr.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<retro_hw_context_destroy_t>(callback.context_destroy);

        try
        {
            EnsureOpenGlLibraryLoaded();
        }
        catch (Exception ex)
        {
            _hwRenderFailureReason = ex.Message;
            return false;
        }

        callback.get_current_framebuffer = Marshal.GetFunctionPointerForDelegate(_hwGetCurrentFramebufferCallback);
        callback.get_proc_address = Marshal.GetFunctionPointerForDelegate(_hwGetProcAddressCallback);
        Marshal.StructureToPtr(callback, data, false);
        return true;
    }

    private static bool SupportsHardwareContext(RetroHwContextType contextType)
    {
        return contextType == RetroHwContextType.OpenGl || contextType == RetroHwContextType.OpenGlCore;
    }

    private void VideoRefresh(IntPtr data, uint width, uint height, uint pitch)
    {
        if (width == 0 || height == 0)
            return;

        if (data == RETRO_HW_FRAME_BUFFER_VALID)
        {
            CaptureHardwareFrame((int)width, (int)height);
            return;
        }

        if (data == IntPtr.Zero)
            return;

        try
        {
            var size = checked((int)(height * pitch));
            var buffer = new byte[size];
            Marshal.Copy(data, buffer, 0, size);
            _frontend?.SubmitFrame(buffer, (int)width, (int)height, (int)pitch, _pixelFormat);
        }
        catch (Exception ex)
        {
            _frontend?.OnHostError($"Frame conversion failed: {ex.Message}");
        }
    }

    private void CaptureHardwareFrame(int width, int height)
    {
        try
        {
            if (_cglContext == IntPtr.Zero || _glReadPixels == null)
                throw new InvalidOperationException("Hardware frame arrived before GL context was ready.");

            if (CGLSetCurrentContext(_cglContext) != 0)
                throw new InvalidOperationException("Failed to activate OpenGL context for hardware frame readback.");

            EnsureHardwareRenderTarget(width, height);
            _glBindFramebuffer?.Invoke(GL_FRAMEBUFFER, _hwFramebufferId);

            var size = checked(width * height * 4);
            var buffer = new byte[size];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                _glReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, handle.AddrOfPinnedObject());
            }
            finally
            {
                handle.Free();
            }

            if (_hwBottomLeftOrigin)
                FlipRowsInPlaceRgba(buffer, width, height);

            _frontend?.SubmitFrame(buffer, width, height, width * 4, RetroPixelFormat.Unknown);
        }
        catch (Exception ex)
        {
            _frontend?.OnHostError($"Hardware frame readback failed: {ex.Message}");
        }
    }

    private short InputState(int port, int device, int index, int id)
    {
        return _frontend?.GetInputState(port, device, index, id) ?? (short)0;
    }

    private void InputPoll()
    {
        // Frontend keeps the latest input snapshot.
    }

    private void AudioSample(short left, short right)
    {
        // Audio output is intentionally stubbed for now.
    }

    private nuint AudioSampleBatch(IntPtr data, nuint frames)
    {
        // Audio output is intentionally stubbed for now.
        return frames;
    }

    private nuint GetCurrentFramebuffer()
    {
        if (!_hwRenderRequested || _cglContext == IntPtr.Zero)
            return 0;

        try
        {
            if (CGLSetCurrentContext(_cglContext) != 0)
                return 0;

            var targetWidth = _config.TargetWidth > 0 ? _config.TargetWidth : 640;
            var targetHeight = _config.TargetHeight > 0 ? _config.TargetHeight : 480;
            EnsureHardwareRenderTarget(targetWidth, targetHeight);
            return _hwFramebufferId == 0 ? 0u : _hwFramebufferId;
        }
        catch
        {
            return 0;
        }
    }

    private IntPtr GetProcAddress(IntPtr symbolPtr)
    {
        if (symbolPtr == IntPtr.Zero)
            return IntPtr.Zero;

        var symbolName = Marshal.PtrToStringAnsi(symbolPtr);
        if (string.IsNullOrWhiteSpace(symbolName))
            return IntPtr.Zero;

        EnsureOpenGlLibraryLoaded();
        return NativeLibrary.TryGetExport(_openGlLibrary, symbolName, out var proc) ? proc : IntPtr.Zero;
    }

    private void EnsureHardwareRenderTarget(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Framebuffer dimensions must be positive.");

        if (_glGenFramebuffers == null || _glBindFramebuffer == null || _glDeleteFramebuffers == null ||
            _glGenTextures == null || _glDeleteTextures == null || _glBindTexture == null ||
            _glTexParameteri == null || _glTexImage2D == null || _glFramebufferTexture2D == null ||
            _glCheckFramebufferStatus == null)
        {
            throw new InvalidOperationException("OpenGL framebuffer entry points are not initialized.");
        }

        if (_hwFramebufferId == 0)
            _glGenFramebuffers(1, out _hwFramebufferId);

        if (_hwColorTextureId == 0)
        {
            _glGenTextures(1, out _hwColorTextureId);
            _glBindTexture(GL_TEXTURE_2D, _hwColorTextureId);
            _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR);
            _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
            _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
            _glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        }

        var sizeChanged = _hwFramebufferWidth != width || _hwFramebufferHeight != height;
        if (sizeChanged)
        {
            _glBindTexture(GL_TEXTURE_2D, _hwColorTextureId);
            _glTexImage2D(GL_TEXTURE_2D, 0, (int)GL_RGBA8, width, height, 0, GL_RGBA, GL_UNSIGNED_BYTE, IntPtr.Zero);
            _hwFramebufferWidth = width;
            _hwFramebufferHeight = height;
        }

        _glBindFramebuffer(GL_FRAMEBUFFER, _hwFramebufferId);
        _glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, _hwColorTextureId, 0);

        var status = _glCheckFramebufferStatus(GL_FRAMEBUFFER);
        if (status != GL_FRAMEBUFFER_COMPLETE)
            throw new InvalidOperationException($"OpenGL framebuffer is incomplete (status 0x{status:X}).");
    }

    private void EnsureHardwareContext()
    {
        if (!_hwRenderRequested)
            return;

        if (!string.IsNullOrWhiteSpace(_hwRenderFailureReason))
            throw new InvalidOperationException(_hwRenderFailureReason);

        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Hardware Libretro context is currently implemented for macOS only.");

        if (_cglContext != IntPtr.Zero)
            return;

        EnsureOpenGlLibraryLoaded();
        CreateCglContext();
        LoadOpenGlEntrypoints();
    }

    private void EnsureOpenGlLibraryLoaded()
    {
        if (_openGlLibrary != IntPtr.Zero)
            return;

        _openGlLibrary = NativeLibrary.Load(OpenGlFrameworkPath);
    }

    private void CreateCglContext()
    {
        var profile = _hwVersionMajor >= 4 ? kCGLOGLPVersion_GL4_Core : kCGLOGLPVersion_3_2_Core;
        var attributes = new[] { kCGLPFAOpenGLProfile, profile, kCGLPFAAccelerated, 0 };

        var chooseResult = CGLChoosePixelFormat(attributes, out var pixelFormat, out var pixelCount);
        if (chooseResult != 0 || pixelFormat == IntPtr.Zero || pixelCount <= 0)
            throw new InvalidOperationException($"Failed to choose CGL pixel format (error {chooseResult}).");

        try
        {
            var createResult = CGLCreateContext(pixelFormat, IntPtr.Zero, out _cglContext);
            if (createResult != 0 || _cglContext == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to create CGL context (error {createResult}).");
        }
        finally
        {
            CGLDestroyPixelFormat(pixelFormat);
        }

        var setResult = CGLSetCurrentContext(_cglContext);
        if (setResult != 0)
            throw new InvalidOperationException($"Failed to activate CGL context (error {setResult}).");
    }

    private void LoadOpenGlEntrypoints()
    {
        _glGenFramebuffers = GetGlFunction<glGenFramebuffers_t>("glGenFramebuffers");
        _glDeleteFramebuffers = GetGlFunction<glDeleteFramebuffers_t>("glDeleteFramebuffers");
        _glBindFramebuffer = GetGlFunction<glBindFramebuffer_t>("glBindFramebuffer");
        _glGenTextures = GetGlFunction<glGenTextures_t>("glGenTextures");
        _glDeleteTextures = GetGlFunction<glDeleteTextures_t>("glDeleteTextures");
        _glBindTexture = GetGlFunction<glBindTexture_t>("glBindTexture");
        _glTexParameteri = GetGlFunction<glTexParameteri_t>("glTexParameteri");
        _glTexImage2D = GetGlFunction<glTexImage2D_t>("glTexImage2D");
        _glFramebufferTexture2D = GetGlFunction<glFramebufferTexture2D_t>("glFramebufferTexture2D");
        _glCheckFramebufferStatus = GetGlFunction<glCheckFramebufferStatus_t>("glCheckFramebufferStatus");
        _glReadPixels = GetGlFunction<glReadPixels_t>("glReadPixels");
    }

    private T GetGlFunction<T>(string symbolName) where T : Delegate
    {
        EnsureOpenGlLibraryLoaded();
        if (!NativeLibrary.TryGetExport(_openGlLibrary, symbolName, out var symbol))
            throw new MissingMethodException($"OpenGL symbol '{symbolName}' was not found.");

        return Marshal.GetDelegateForFunctionPointer<T>(symbol);
    }

    private void DestroyHardwareContext()
    {
        if (_cglContext != IntPtr.Zero)
        {
            CGLSetCurrentContext(_cglContext);

            if (_hwColorTextureId != 0 && _glDeleteTextures != null)
            {
                _glDeleteTextures(1, ref _hwColorTextureId);
                _hwColorTextureId = 0;
            }

            if (_hwFramebufferId != 0 && _glDeleteFramebuffers != null)
            {
                _glDeleteFramebuffers(1, ref _hwFramebufferId);
                _hwFramebufferId = 0;
            }

            CGLSetCurrentContext(IntPtr.Zero);
            CGLDestroyContext(_cglContext);
            _cglContext = IntPtr.Zero;
        }

        _glGenFramebuffers = null;
        _glDeleteFramebuffers = null;
        _glBindFramebuffer = null;
        _glGenTextures = null;
        _glDeleteTextures = null;
        _glBindTexture = null;
        _glTexParameteri = null;
        _glTexImage2D = null;
        _glFramebufferTexture2D = null;
        _glCheckFramebufferStatus = null;
        _glReadPixels = null;
        _hwFramebufferId = 0;
        _hwColorTextureId = 0;
        _hwFramebufferWidth = 0;
        _hwFramebufferHeight = 0;
        _hwContextReset = null;
        _hwContextDestroy = null;

        if (_openGlLibrary != IntPtr.Zero)
        {
            NativeLibrary.Free(_openGlLibrary);
            _openGlLibrary = IntPtr.Zero;
        }
    }

    private T GetCoreFunction<T>(string name) where T : Delegate
    {
        if (_coreHandle == IntPtr.Zero)
            throw new InvalidOperationException("Libretro core is not loaded.");

        if (!NativeLibrary.TryGetExport(_coreHandle, name, out var ptr))
            throw new MissingMethodException($"Libretro core does not export '{name}'.");

        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private IntPtr GetOptionPointer(string value)
    {
        if (_optionValuePointers.TryGetValue(value, out var existing))
            return existing;

        var ptr = Marshal.StringToHGlobalAnsi(value);
        _allocatedStrings.Add(ptr);
        _optionValuePointers[value] = ptr;
        return ptr;
    }

    private bool WriteStringPointer(IntPtr data, string? value)
    {
        if (data == IntPtr.Zero)
            return false;

        var resolvedValue = string.IsNullOrWhiteSpace(value) ? Environment.CurrentDirectory : value;
        var pointer = GetPersistentStringPointer(resolvedValue);
        Marshal.WriteIntPtr(data, pointer);
        return true;
    }

    private IntPtr GetPersistentStringPointer(string value)
    {
        if (_persistentStringPointers.TryGetValue(value, out var existing))
            return existing;

        var ptr = Marshal.StringToHGlobalAnsi(value);
        _allocatedStrings.Add(ptr);
        _persistentStringPointers[value] = ptr;
        return ptr;
    }

    private static void FlipRowsInPlaceRgba(byte[] buffer, int width, int height)
    {
        var rowSize = width * 4;
        var temp = new byte[rowSize];
        for (var y = 0; y < height / 2; y++)
        {
            var top = y * rowSize;
            var bottom = (height - 1 - y) * rowSize;
            Buffer.BlockCopy(buffer, top, temp, 0, rowSize);
            Buffer.BlockCopy(buffer, bottom, buffer, top, rowSize);
            Buffer.BlockCopy(temp, 0, buffer, bottom, rowSize);
        }
    }

    private void FreeGamePath()
    {
        if (_gamePathPtr != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_gamePathPtr);
            _gamePathPtr = IntPtr.Zero;
        }
    }

    private void FreeAllocatedStrings()
    {
        foreach (var ptr in _allocatedStrings)
            Marshal.FreeHGlobal(ptr);

        _allocatedStrings.Clear();
        _optionValuePointers.Clear();
        _persistentStringPointers.Clear();
    }

    private void FreeCoreHandle()
    {
        if (_coreHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(_coreHandle);
            _coreHandle = IntPtr.Zero;
        }
    }

    [DllImport(OpenGlFrameworkPath)]
    private static extern int CGLChoosePixelFormat(int[] attribs, out IntPtr pixelFormat, out int pixelCount);

    [DllImport(OpenGlFrameworkPath)]
    private static extern int CGLDestroyPixelFormat(IntPtr pixelFormat);

    [DllImport(OpenGlFrameworkPath)]
    private static extern int CGLCreateContext(IntPtr pixelFormat, IntPtr shareContext, out IntPtr context);

    [DllImport(OpenGlFrameworkPath)]
    private static extern int CGLDestroyContext(IntPtr context);

    [DllImport(OpenGlFrameworkPath)]
    private static extern int CGLSetCurrentContext(IntPtr context);
}
