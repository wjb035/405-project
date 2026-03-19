using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using PGEmu.app;

namespace PGEmu.Emu.Libretro;

// In-process Libretro core runner.
//
// Design notes (inspired by CatUI's LibretroNative/LibretroPlayer):
// - The Libretro core runs on a dedicated background thread, because most cores expect to be called from a
//   consistent thread and may block in `retro_run()`.
// - We keep all unmanaged string pointers alive for the lifetime of the core to avoid the core holding on to
//   freed memory (options/system/save directories).
// - Video frames are delivered to an `ILibretroRunnerFrontend` which can marshal them to the UI thread safely.
//
// This file targets macOS first: hardware rendering (OpenGL/CGL) is implemented for macOS only.
public enum LibretroPixelFormat : uint
{
    // libretro.h: RETRO_PIXEL_FORMAT_0RGB1555 = 0
    ZeroRgb1555 = 0,
    // libretro.h: RETRO_PIXEL_FORMAT_XRGB8888 = 1
    Xrgb8888 = 1,
    // libretro.h: RETRO_PIXEL_FORMAT_RGB565 = 2
    Rgb565 = 2,
    Unknown = uint.MaxValue
}

public interface ILibretroRunnerFrontend
{
    void SubmitFrame(byte[] buffer, int width, int height, int pitch, LibretroPixelFormat format);
    short GetInputState(int port, int device, int index, int id);
    void OnRunnerError(string message);
}

public sealed class LibretroRunner : IDisposable
{
    // Dolphin can take noticeable time to initialize shaders/state on first startup.
    // Keep a safe default while still allowing per-platform override via config.
    private const int MinStartupTimeoutSeconds = 5;
    private const int DefaultStartupTimeoutSeconds = 60;

    // Environment command constants (subset).
    // Values come from libretro.h and match the ones used in CatUI's implementation.
    private const uint RETRO_ENVIRONMENT_GET_CAN_DUPE = 3;
    private const uint RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY = 9;
    private const uint RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 10;
    private const uint RETRO_ENVIRONMENT_SET_HW_RENDER = 14;
    private const uint RETRO_ENVIRONMENT_GET_VARIABLE = 15;
    private const uint RETRO_ENVIRONMENT_SET_VARIABLES = 16;
    private const uint RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE = 17;
    private const uint RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME = 18;
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
    // Final option set provided to RETRO_ENVIRONMENT_GET_VARIABLE.
    // We merge config-provided options with a small set of macOS-safe Dolphin defaults.
    private readonly Dictionary<string, string> _resolvedOptions;
    private readonly ILibraryLoader _libraryLoader;
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
    private readonly Dictionary<string, string[]> _declaredOptionValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly ManualResetEventSlim _startupSignal = new(false);

    private ILibretroRunnerFrontend? _frontend;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    private IntPtr _coreHandle = IntPtr.Zero;
    private IntPtr _gamePathPtr = IntPtr.Zero;
    private LibretroPixelFormat _pixelFormat = LibretroPixelFormat.Xrgb8888;
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
    private string _systemDirectory = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _assetsDirectory = string.Empty;
    private string? _dolphinSysDirectory;

    public LibretroRunner(LibretroConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _resolvedOptions = BuildResolvedOptions(_config);
        _libraryLoader = LibraryLoaderFactory.Create();
        _environmentCallback = EnvironmentCallback;
        _videoRefreshCallback = VideoRefresh;
        _audioSampleCallback = AudioSample;
        _audioSampleBatchCallback = AudioSampleBatch;
        _inputPollCallback = InputPoll;
        _inputStateCallback = InputState;
        _hwGetCurrentFramebufferCallback = GetCurrentFramebuffer;
        _hwGetProcAddressCallback = GetProcAddress;
    }

    private static Dictionary<string, string> BuildResolvedOptions(LibretroConfig config)
    {
        var options = config.Options != null
            ? new Dictionary<string, string>(config.Options, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // macOS guard-port crashes with Dolphin libretro are commonly triggered by the fast JIT path.
        // Use conservative defaults unless the user explicitly overrides them in config.json.
        if (OperatingSystem.IsMacOS() && IsDolphinCore(config.CorePath))
        {
            SetIfMissing(options, "dolphin_renderer", "OpenGL");
            SetIfMissing(options, "dolphin_cpu_core", "Interpreter (slowest)");
            SetIfMissing(options, "dolphin_main_cpu_thread", "Disabled");
            SetIfMissing(options, "dolphin_fastmem", "Disabled");
            SetIfMissing(options, "dolphin_fastmem_arena", "Disabled");
            SetIfMissing(options, "dolphin_wiimote_continuous_scanning", "Disabled");
            SetIfMissing(options, "dolphin_bluetooth_passthrough", "Disabled");
        }

        return options;
    }

    private static void SetIfMissing(Dictionary<string, string> options, string key, string value)
    {
        if (!options.ContainsKey(key))
            options[key] = value;
    }

    private static bool IsDolphinCore(string corePath)
    {
        var fileName = Path.GetFileName(corePath);
        return fileName.Contains("dolphin", StringComparison.OrdinalIgnoreCase);
    }

    private static void LogInfo(string message)
    {
        Console.WriteLine($"[LibretroRunner][thread {Environment.CurrentManagedThreadId}] {message}");
    }

    private void PrepareRuntimeDirectories()
    {
        var coreDirectory = Path.GetDirectoryName(_config.CorePath);
        if (string.IsNullOrWhiteSpace(coreDirectory))
            coreDirectory = Environment.CurrentDirectory;

        _systemDirectory = Path.Combine(coreDirectory, "system");
        _saveDirectory = Path.Combine(coreDirectory, "save");
        _assetsDirectory = Path.Combine(coreDirectory, "assets");

        EnsureWritableDirectory(_systemDirectory, "system");
        EnsureWritableDirectory(_saveDirectory, "save");
        EnsureWritableDirectory(_assetsDirectory, "assets");

        LogInfo($"System directory set to '{_systemDirectory}'.");
        LogInfo($"Save directory set to '{_saveDirectory}'.");
        LogInfo($"Assets directory set to '{_assetsDirectory}'.");

        if (!IsDolphinCore(_config.CorePath))
            return;

        _dolphinSysDirectory = Path.Combine(_systemDirectory, "dolphin-emu", "Sys");
        LogInfo($"Expected Dolphin Sys directory: '{_dolphinSysDirectory}'.");

        if (!Directory.Exists(_dolphinSysDirectory))
        {
            throw new InvalidOperationException(
                $"Dolphin Sys files are missing. Expected directory: '{_dolphinSysDirectory}'. " +
                "Create that folder and copy Dolphin's 'Sys' contents there.");
        }
    }

    private static void EnsureWritableDirectory(string path, string role)
    {
        try
        {
            Directory.CreateDirectory(path);
            var probeFilePath = Path.Combine(path, ".pgemu_write_probe");
            File.WriteAllText(probeFilePath, "ok");
            File.Delete(probeFilePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Libretro {role} directory is not writable: '{path}'. {ex.Message}",
                ex);
        }
    }

    public void Start(string romPath, ILibretroRunnerFrontend frontend)
    {
        if (string.IsNullOrWhiteSpace(romPath))
            throw new ArgumentException("ROM path is required", nameof(romPath));

        if (_frontend != null || _loopTask != null)
            throw new InvalidOperationException("Libretro runner already running.");

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

        var startupTimeoutSeconds = _config.StartupTimeoutSeconds > 0
            ? Math.Max(_config.StartupTimeoutSeconds, MinStartupTimeoutSeconds)
            : DefaultStartupTimeoutSeconds;

        if (!_startupSignal.Wait(TimeSpan.FromSeconds(startupTimeoutSeconds)))
        {
            Stop();
            throw new TimeoutException($"Timed out waiting for Libretro core startup after {startupTimeoutSeconds} seconds.");
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
            PrepareRuntimeDirectories();
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
            _frontend?.OnRunnerError($"Libretro runtime error: {ex.Message}");
            _startupSignal.Set();
        }
        finally
        {
            try { _hwContextDestroy?.Invoke(); }
            catch (Exception ex) { _frontend?.OnRunnerError($"Libretro context destroy failed: {ex.Message}"); }

            DestroyHardwareContext();

            try { _retroUnloadGame?.Invoke(); }
            catch (Exception ex) { _frontend?.OnRunnerError($"Libretro unload failed: {ex.Message}"); }

            try { _retroDeinit?.Invoke(); }
            catch (Exception ex) { _frontend?.OnRunnerError($"Libretro deinit failed: {ex.Message}"); }

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

        _coreHandle = _libraryLoader.LoadLibrary(_config.CorePath);
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
        _gamePathPtr = AllocUtf8String(romPath);

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
            case RETRO_ENVIRONMENT_GET_CAN_DUPE:
                // Tell the core we can accept duplicate frames.
                if (data != IntPtr.Zero)
                    Marshal.WriteByte(data, 1);
                return true;

            case RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
                if (data == IntPtr.Zero)
                    return false;
                _pixelFormat = (LibretroPixelFormat)Marshal.ReadInt32(data);
                return true;

            case RETRO_ENVIRONMENT_SET_HW_RENDER:
                return HandleSetHwRender(data);

            case RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME:
                // We don't support "no game" mode yet, but returning true is safe for cores that probe it.
                return true;

            case RETRO_ENVIRONMENT_SET_VARIABLES:
                // Core registers its available options. We don't need to persist these (options are provided via
                // `LibretroConfig.Options`), but we record the declared values so user-provided values can be matched
                // safely even when labels vary by build (for example: "Interpreter" vs "Interpreter (slowest)").
                CaptureDeclaredOptions(data);
                return true;

            case RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
                LogInfo($"RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY -> '{_systemDirectory}'.");
                return WriteStringPointer(data, _systemDirectory);

            case RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY:
                LogInfo($"RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY -> '{_assetsDirectory}'.");
                return WriteStringPointer(data, _assetsDirectory);

            case RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
                LogInfo($"RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY -> '{_saveDirectory}'.");
                return WriteStringPointer(data, _saveDirectory);

            case RETRO_ENVIRONMENT_GET_VARIABLE:
            {
                if (data == IntPtr.Zero)
                    return false;

                var variable = Marshal.PtrToStructure<retro_variable>(data);
                var key = variable.key == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(variable.key);
                variable.value = IntPtr.Zero;

                // The core provides `key`, we provide `value` (as a stable unmanaged pointer).
                // Like CatUI, we cache the unmanaged strings so the GC doesn't move/free them mid-frame.
                if (!string.IsNullOrEmpty(key) && _resolvedOptions.TryGetValue(key, out var value) && value != null)
                    variable.value = GetOptionPointer(ResolveOptionValue(key, value));

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

    private void CaptureDeclaredOptions(IntPtr data)
    {
        _declaredOptionValues.Clear();

        if (data == IntPtr.Zero)
            return;

        var variableSize = Marshal.SizeOf<retro_variable>();
        for (var offset = 0; ; offset += variableSize)
        {
            var variablePtr = IntPtr.Add(data, offset);
            var variable = Marshal.PtrToStructure<retro_variable>(variablePtr);

            if (variable.key == IntPtr.Zero)
                break;

            var key = Marshal.PtrToStringUTF8(variable.key);
            var definition = variable.value == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(variable.value);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(definition))
                continue;

            var values = ParseDeclaredValues(definition);
            if (values.Length > 0)
                _declaredOptionValues[key] = values;
        }
    }

    private static string[] ParseDeclaredValues(string definition)
    {
        var separatorIndex = definition.IndexOf(';');
        var valuesPart = separatorIndex >= 0
            ? definition[(separatorIndex + 1)..]
            : definition;

        return valuesPart
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private string ResolveOptionValue(string key, string configuredValue)
    {
        if (!_declaredOptionValues.TryGetValue(key, out var declaredValues) || declaredValues.Length == 0)
            return configuredValue;

        foreach (var declared in declaredValues)
        {
            if (string.Equals(declared, configuredValue, StringComparison.OrdinalIgnoreCase))
                return declared;
        }

        var normalizedConfigured = NormalizeOptionToken(configuredValue);
        foreach (var declared in declaredValues)
        {
            if (NormalizeOptionToken(declared) == normalizedConfigured)
                return declared;
        }

        foreach (var declared in declaredValues)
        {
            if (declared.Contains(configuredValue, StringComparison.OrdinalIgnoreCase) ||
                configuredValue.Contains(declared, StringComparison.OrdinalIgnoreCase))
            {
                return declared;
            }
        }

        return configuredValue;
    }

    private static string NormalizeOptionToken(string value)
    {
        Span<char> scratch = stackalloc char[value.Length];
        var count = 0;
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
                scratch[count++] = char.ToLowerInvariant(ch);
        }

        return new string(scratch[..count]);
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
            _frontend?.OnRunnerError($"Frame conversion failed: {ex.Message}");
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

            _frontend?.SubmitFrame(buffer, width, height, width * 4, LibretroPixelFormat.Unknown);
        }
        catch (Exception ex)
        {
            _frontend?.OnRunnerError($"Hardware frame readback failed: {ex.Message}");
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

        if (!_libraryLoader.TryGetExport(_coreHandle, name, out var ptr))
            throw new MissingMethodException($"Libretro core does not export '{name}'.");

        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private IntPtr GetOptionPointer(string value)
    {
        if (_optionValuePointers.TryGetValue(value, out var existing))
            return existing;

        var ptr = AllocUtf8String(value);
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

        var ptr = AllocUtf8String(value);
        _allocatedStrings.Add(ptr);
        _persistentStringPointers[value] = ptr;
        return ptr;
    }

    private static IntPtr AllocUtf8String(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var pointer = Marshal.AllocHGlobal(bytes.Length + 1);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        Marshal.WriteByte(pointer, bytes.Length, 0);
        return pointer;
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
            _libraryLoader.FreeLibrary(_coreHandle);
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
