// libretro player implementation for CatUI.
// handles emulation logic, auto-saving, and state management.

using Godot;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;

public partial class LibretroPlayer : Node
{
	private TextureRect? _screenRect;
	private AudioStreamPlayer? _gameAudio;

	private static LibretroPlayer _instance;

	private static LibretroNative.RetroEnvironmentDelegate _envCallback;
	private static LibretroNative.RetroVideoRefreshDelegate _videoCallback;
	private static LibretroNative.RetroInputPollDelegate _inputPollCallback;
	private static LibretroNative.RetroInputStateDelegate _inputStateCallback;
	private static LibretroNative.RetroAudioSampleDelegate _audioCallback;
	private static LibretroNative.RetroAudioSampleBatchDelegate _audioBatchCallback;

	private ImageTexture? _gameTexture;
	private AudioStreamGeneratorPlayback? _audioPlayback;

	private bool _isGameRunning = false;
	private bool _coreInitialized = false;
	private double _timeAccumulator = 0.0;
	private const double TargetFrameTime = 1.0 / 60.0;

	private string _currentRomPath = "";
	private retro_pixel_format _currentPixelFormat = retro_pixel_format.RETRO_PIXEL_FORMAT_RGB565;

	private double _ignoreInputTimer = 0.0;

	// optimization: using static buffers here so the garbage collector doesn't go crazy with allocations
	private static HashSet<string> _validActions = new HashSet<string>();
	private static bool _actionsCached = false;

	private static readonly object _videoFrameLock = new object();
	private static byte[]? _pendingVideoFrame;
	private static int _pendingVideoWidth;
	private static int _pendingVideoHeight;
	private static int _pendingVideoFormat;
	private static bool _hasPendingVideoFrame;
	private static bool _loggedFirstVideoFrame;
	private static bool _loggedHwVideoPointer;
	private byte[]? _uploadVideoFrame;

	private static readonly object _audioFrameLock = new object();
	private static short[]? _pendingAudioRaw;
	private static int _pendingAudioSamples;
	private static bool _hasPendingAudioBatch;
	private static bool _hasPendingAudioSample;
	private static short _pendingAudioSampleLeft;
	private static short _pendingAudioSampleRight;
	private short[]? _audioUploadRaw;
	private Vector2[]? _audioUploadFrames;

	private static readonly short[] _inputStateCache = new short[16];
	private const uint RetroDeviceJoypad = 1;
	private const uint RetroDeviceIdJoypadMask = 256;
	private static bool _isDolphinCore = false;
	private static bool _isPpssppCore = false;
	private static bool _forcePpssppSoftwareMode = false;
	// Current frontend has no libretro GPU context bridge. Non-PPSSPP HW cores abort startup.
	private static bool _frontendSupportsHardwareRender = false;
	private static bool _coreRequestedHardwareRender = false;
	private static bool _abortStartupDueToHardwareRender = false;
	private static bool _loggedHwRenderUnsupported = false;
	private static bool _loggedHwRenderUnsupportedGeneric = false;
	private static bool _loggedHwRenderInterfaceUnsupported = false;
	private static bool _loggedHwSharedContextUnsupported = false;
	private static bool _loggedHwNegotiationUnsupported = false;
	private static bool _loggedPpssppSoftwarePolicy = false;
	private static bool _loggedPpssppBackendOverride = false;
	private static bool _loggedPpssppHwRenderDenied = false;
	private static uint _requestedMinimumAudioLatencyMs = 0;
	private bool _loggedFirstRetroRun = false;
	private bool _loggedFirstRetroRunComplete = false;
	
	[StructLayout(LayoutKind.Sequential)]
	private struct retro_variable
	{
		public IntPtr key;
		public IntPtr value;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct retro_log_callback
	{
		public IntPtr log;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct retro_message
	{
		public IntPtr msg;
		public uint frames;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct retro_message_ext
	{
		public IntPtr msg;
		public uint duration;
		public uint priority;
		public int level;
		public uint target;
		public uint type;
		public int progress;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct retro_rumble_interface
	{
		public IntPtr set_rumble_state;
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate void RetroLogPrintfShimDelegate(int level, IntPtr format);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate bool RetroSetRumbleStateDelegate(uint port, uint effect, ushort strength);
	
	// Stores unmanaged strings returned via RETRO_ENVIRONMENT_GET_VARIABLE.
	private static Dictionary<string, IntPtr> _variableValuePtrs = new Dictionary<string, IntPtr>();
	private static readonly object _variablePtrLock = new object();
	private static readonly object _registeredVariablesLock = new object();
	private static readonly Dictionary<string, string[]> _registeredCoreVariables = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<string> _loggedDolphinResolvedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<string> _loggedPpssppResolvedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	private static readonly object _environmentLogLock = new object();
	private static readonly HashSet<uint> _loggedUnhandledEnvironmentCommands = new HashSet<uint>();
	private static RetroLogPrintfShimDelegate? _retroLogPrintfShim;
	private static RetroSetRumbleStateDelegate? _retroSetRumbleStateShim;
	private static IntPtr _nativeRetroLogCallbackPtr = IntPtr.Zero;
	private static IntPtr _nativeRetroLogLibraryHandle = IntPtr.Zero;
	private static IntPtr _usernamePtr = IntPtr.Zero;
	private const uint FrontendTargetSampleRateHz = 48000;

	public override void _Ready()
	{
		_instance = this;
	}

	public void AttachOutput(TextureRect screenRect, AudioStreamPlayer gameAudio)
	{
		_screenRect = screenRect;
		_gameAudio = gameAudio;

		if (_gameAudio != null && _audioPlayback == null)
			SetupAudio();
	}

	public async void LoadGame(string path, string coreId = "")
	{
		// FileLogger.Log($"[LibretroPlayer] LoadGame called - Path: {path}, CoreId: {coreId}");
		// FileLogger.Log($"[LibretroPlayer] Platform: {OS.GetName()}");
		
		if (_screenRect == null || _gameAudio == null)
		{
			FileLogger.Error("[LibretroPlayer] ERROR: output nodes are not bound. Call AttachOutput first.");
			return;
		}
		
		if (_isGameRunning)
		{
			FileLogger.Log("[LibretroPlayer] Stopping previous game...");
			StopGame();
			
			if (OS.GetName() == "Android")
			{
				// give it a moment to cleanup resources properly
				await ToSignal(GetTree().CreateTimer(0.2f), "timeout");
			}
		}

		_currentRomPath = path;
		LibretroNative.CurrentCoreId = coreId;
		

		if (!LibretroNative.SetCoreFromRomPath(path))
		{
			FileLogger.Error("[libretroplayer] failed to determine core");
			return;
		}
		
		FileLogger.Log($"[libretroplayer] core found: {LibretroNative.CurrentCorePath}");
		// FileLogger.Log("[LibretroPlayer] Loading core...");
		
		if (!LibretroNative.LoadCore())
		{
			FileLogger.Error("[libretroplayer] failed to load core");
			return;
		}

		FileLogger.Log("[libretroplayer] core loaded!");
		FileLogger.Log("[libretroplayer] resetting audio...");
		ResetAudio();
		
		FileLogger.Log("[libretroplayer] starting emulator...");
		StartEmulator();
	}

	public void LoadGameWithCore(string path, string corePath, string coreId)
	{
		if (_screenRect == null || _gameAudio == null)
		{
			FileLogger.Error("[LibretroPlayer] ERROR: output nodes are not bound. Call AttachOutput first.");
			return;
		}
		
		if (_isGameRunning)
		{
			StopGame();
		}

		_currentRomPath = path;
		LibretroNative.CurrentCorePath = corePath;
		LibretroNative.CurrentCoreId = coreId;
		
		if (!LibretroNative.LoadCore())
		{
			FileLogger.Error("Failed to load core");
			return;
		}

		ResetAudio();
		StartEmulator();
	}

	public void StopGame()
	{
		if (_coreInitialized && LibretroNative.retro_deinit != null)
		{
			if (_isGameRunning)
				SaveSRAM();

			LibretroNative.retro_deinit();
		}
		
		LibretroNative.UnloadCore();
		
		if (_romDataPtr != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(_romDataPtr);
			_romDataPtr = IntPtr.Zero;
		}
		
		_isGameRunning = false;
			_isPaused = false;
			_coreInitialized = false;
			_actionsCached = false;
		// _gameTexture = null; // keeping the texture reference to avoid unnecessary GC churn
		lock (_videoFrameLock)
		{
			_hasPendingVideoFrame = false;
		}
		lock (_audioFrameLock)
		{
			_hasPendingAudioBatch = false;
			_hasPendingAudioSample = false;
			_pendingAudioSamples = 0;
		}
			Array.Clear(_inputStateCache, 0, _inputStateCache.Length);
			_isDolphinCore = false;
			_isPpssppCore = false;
			_forcePpssppSoftwareMode = false;
			_loggedFirstVideoFrame = false;
			_loggedHwVideoPointer = false;
			_coreRequestedHardwareRender = false;
			_abortStartupDueToHardwareRender = false;
				_loggedHwRenderUnsupported = false;
				_loggedHwRenderUnsupportedGeneric = false;
				_loggedHwRenderInterfaceUnsupported = false;
				_loggedHwSharedContextUnsupported = false;
				_loggedHwNegotiationUnsupported = false;
			_loggedPpssppSoftwarePolicy = false;
			_loggedPpssppBackendOverride = false;
			_loggedPpssppHwRenderDenied = false;
			_requestedMinimumAudioLatencyMs = 0;
			_loggedFirstRetroRun = false;
			_loggedFirstRetroRunComplete = false;

		if (_saveDirectoryPtr != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(_saveDirectoryPtr);
			_saveDirectoryPtr = IntPtr.Zero;
			_resolvedSaveDirectory = string.Empty;
		}
		if (_systemDirectoryPtr != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(_systemDirectoryPtr);
			_systemDirectoryPtr = IntPtr.Zero;
			_resolvedSystemDirectory = string.Empty;
		}
		if (_coreAssetsDirectoryPtr != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(_coreAssetsDirectoryPtr);
			_coreAssetsDirectoryPtr = IntPtr.Zero;
			_resolvedAssetsDirectory = string.Empty;
		}
		// Free all unmanaged variable strings returned to the core.
		lock (_variablePtrLock)
		{
			foreach (var pair in _variableValuePtrs)
			{
				if (pair.Value != IntPtr.Zero)
					Marshal.FreeHGlobal(pair.Value);
			}
			_variableValuePtrs.Clear();
		}
		lock (_registeredVariablesLock)
		{
			_registeredCoreVariables.Clear();
			_loggedDolphinResolvedKeys.Clear();
			_loggedPpssppResolvedKeys.Clear();
		}

		if (_romPathPtr != IntPtr.Zero)
		{
			Marshal.FreeHGlobal(_romPathPtr);
			_romPathPtr = IntPtr.Zero;
		}
		
		if (_gameAudio != null)
		{
			_gameAudio.Stop();
		}
	}

	private void SetupAudio()
	{
		if (_gameAudio == null) return;

		float bufferLength = OS.GetName() == "Android" ? 0.05f : 0.1f;
		
		var generator = new AudioStreamGenerator
		{
			BufferLength = bufferLength
		};
		
		_gameAudio.Stream = generator;
		_gameAudio.Play();
		
		_audioPlayback = (AudioStreamGeneratorPlayback)_gameAudio.GetStreamPlayback();
	}

	private void ResetAudio()
	{
		if (_gameAudio == null) return;
		_gameAudio.Stop();
		_audioPlayback = null;
		SetupAudio();
	}

	private double _autoSaveTimer = 0.0;
	private const double AutoSaveInterval = 5.0;

	public override void _Process(double delta)
	{
		if (_ignoreInputTimer > 0)
		{
			_ignoreInputTimer -= delta;
		}

		if (_isGameRunning && !_isPaused && LibretroNative.retro_run != null)
		{
			UpdateInputStateCache();

			_autoSaveTimer += delta;
			if (_autoSaveTimer >= AutoSaveInterval)
			{
				SaveSRAM();
				_autoSaveTimer = 0.0;
			}
			
			_timeAccumulator += delta;
			
			// Keep catch-up bounded so heavy cores don't lock the main thread.
			if (_timeAccumulator > TargetFrameTime * 3)
			{
				_timeAccumulator = TargetFrameTime;
			}

				// Run at most one core step per Godot frame for responsiveness.
				if (_timeAccumulator >= TargetFrameTime)
				{
					if (!_loggedFirstRetroRun)
					{
						_loggedFirstRetroRun = true;
						FileLogger.Log("[libretroplayer] first retro_run begin");
					}

					LibretroNative.retro_run();

					if (!_loggedFirstRetroRunComplete)
					{
						_loggedFirstRetroRunComplete = true;
						FileLogger.Log("[libretroplayer] first retro_run end");
					}

					_timeAccumulator = 0.0;
				}
		}

		FlushPendingVideoFrame();
		FlushPendingAudioFrames();
	}

	private IntPtr _romDataPtr = IntPtr.Zero;
	// Keep ROM path alive as unmanaged memory while the core may retain the pointer.
	private IntPtr _romPathPtr = IntPtr.Zero;

	private static void CacheValidActions(bool forceRefresh = false)
	{
		if (_actionsCached && !forceRefresh) return;
		
		_validActions.Clear();
		
		// Snapshot existing action names so InputStateCallback can do cheap lookups.
		foreach (var action in InputMap.GetActions())
		{
			_validActions.Add(action.ToString());
		}
		
		_actionsCached = true;
		FileLogger.Log($"[LibretroPlayer] Cached {_validActions.Count} input actions");
	}

	private void UpdateInputStateCache()
	{
		string coreId = !string.IsNullOrEmpty(LibretroNative.CurrentCoreId)
			? LibretroNative.CurrentCoreId
			: GetCoreFallbackId();

		for (int i = 0; i < _inputStateCache.Length; i++)
		{
			string actionName = InputMapper.GetActionName(coreId, (LibretroInput)i);
			bool pressed = !string.IsNullOrEmpty(actionName) &&
				_validActions.Contains(actionName) &&
				Input.IsActionPressed(actionName);

			if (!pressed)
				pressed = IsFallbackInputPressed((LibretroInput)i);

			_inputStateCache[i] = pressed ? (short)1 : (short)0;
		}
	}

	private void PrepareEnvironmentDirectories()
	{
		string saveDir;
		string systemDir;
		string assetsDir;

		if (_isDolphinCore)
		{
			// Mirror RetroArch directory semantics and let the core resolve its own subfolders.
			saveDir = ProjectSettings.GlobalizePath("user://saves/");
			systemDir = ProjectSettings.GlobalizePath("user://system/");
			// Cores commonly append "Sys"/"User" beneath this root.
			assetsDir = Path.Combine(systemDir, "dolphin-emu");
		}
		else
		{
			saveDir = ProjectSettings.GlobalizePath("user://saves/");
			systemDir = ProjectSettings.GlobalizePath("user://system/");
			assetsDir = systemDir;
		}

		Directory.CreateDirectory(saveDir);
		Directory.CreateDirectory(systemDir);
		Directory.CreateDirectory(assetsDir);

		UpdateDirectoryPointer(ref _saveDirectoryPtr, ref _resolvedSaveDirectory, saveDir);
		UpdateDirectoryPointer(ref _systemDirectoryPtr, ref _resolvedSystemDirectory, systemDir);
		UpdateDirectoryPointer(ref _coreAssetsDirectoryPtr, ref _resolvedAssetsDirectory, assetsDir);

		if (_isDolphinCore)
		{
			string sysPath = Path.Combine(systemDir, "dolphin-emu", "Sys");
			string userPath = Path.Combine(saveDir, "User");
			FileLogger.Log($"[dolphin] save_dir={saveDir}");
			FileLogger.Log($"[dolphin] system_dir={systemDir}");
			FileLogger.Log($"[dolphin] assets_dir={assetsDir}");
			FileLogger.Log($"[dolphin] sys_exists={Directory.Exists(sysPath)} user_exists={Directory.Exists(userPath)}");
		}
	}

	private static void UpdateDirectoryPointer(ref IntPtr ptr, ref string currentPath, string nextPath)
	{
		if (string.Equals(currentPath, nextPath, StringComparison.Ordinal) && ptr != IntPtr.Zero)
			return;

		if (ptr != IntPtr.Zero)
			Marshal.FreeHGlobal(ptr);

		ptr = Marshal.StringToHGlobalAnsi(nextPath);
		currentPath = nextPath;
	}

	private void FlushPendingVideoFrame()
	{
		byte[]? frameToUpload = null;
		int width = 0;
		int height = 0;
		int format = 0;

		lock (_videoFrameLock)
		{
			if (!_hasPendingVideoFrame || _pendingVideoFrame == null)
				return;

			if (_uploadVideoFrame == null || _uploadVideoFrame.Length != _pendingVideoFrame.Length)
				_uploadVideoFrame = new byte[_pendingVideoFrame.Length];

			Buffer.BlockCopy(_pendingVideoFrame, 0, _uploadVideoFrame, 0, _pendingVideoFrame.Length);
			frameToUpload = _uploadVideoFrame;
			width = _pendingVideoWidth;
			height = _pendingVideoHeight;
			format = _pendingVideoFormat;
			_hasPendingVideoFrame = false;
		}

		UpdateGameTexture(frameToUpload, width, height, format);
	}

	private void FlushPendingAudioFrames()
	{
		if (_audioPlayback == null) return;

		int samples = 0;
		bool hasBatch = false;
		bool hasSample = false;
		short left = 0;
		short right = 0;

		lock (_audioFrameLock)
		{
			if (_hasPendingAudioBatch && _pendingAudioRaw != null && _pendingAudioSamples > 0)
			{
				samples = _pendingAudioSamples;
				if (_audioUploadRaw == null || _audioUploadRaw.Length < samples)
					_audioUploadRaw = new short[samples];

				Array.Copy(_pendingAudioRaw, _audioUploadRaw, samples);
				_hasPendingAudioBatch = false;
				_pendingAudioSamples = 0;
				hasBatch = true;
			}

			if (_hasPendingAudioSample)
			{
				left = _pendingAudioSampleLeft;
				right = _pendingAudioSampleRight;
				_hasPendingAudioSample = false;
				hasSample = true;
			}
		}

		if (hasBatch && _audioUploadRaw != null)
		{
			int frames = samples / 2;
			if (frames > 0 && _audioPlayback.GetFramesAvailable() >= frames)
			{
				if (_audioUploadFrames == null || _audioUploadFrames.Length != frames)
					_audioUploadFrames = new Vector2[frames];

				const float invShort = 0.00003051757f;
				for (int i = 0; i < frames; i++)
				{
					_audioUploadFrames[i].X = _audioUploadRaw[i * 2] * invShort;
					_audioUploadFrames[i].Y = _audioUploadRaw[i * 2 + 1] * invShort;
				}

				_audioPlayback.PushBuffer(_audioUploadFrames);
			}
		}
		else if (hasSample && _audioPlayback.GetFramesAvailable() > 0)
		{
			_audioPlayback.PushFrame(new Vector2(left / 32768f, right / 32768f));
		}
	}

	private void StartEmulator()
	{
		// FileLogger.Log("[LibretroPlayer] StartEmulator - BEGIN");
		try
		{
			// Reset per-boot capability state before a core begins environment negotiation.
			_coreRequestedHardwareRender = false;
			_abortStartupDueToHardwareRender = false;

			// FileLogger.Log("[LibretroPlayer] Creating callbacks...");
			_envCallback = new LibretroNative.RetroEnvironmentDelegate(EnvironmentCallback);
			_videoCallback = new LibretroNative.RetroVideoRefreshDelegate(VideoCallback);
			_inputPollCallback = new LibretroNative.RetroInputPollDelegate(InputPollCallback);
			_inputStateCallback = new LibretroNative.RetroInputStateDelegate(InputStateCallback);
			_audioCallback = new LibretroNative.RetroAudioSampleDelegate(AudioSampleCallback);
			_audioBatchCallback = new LibretroNative.RetroAudioSampleBatchDelegate(AudioBatchCallback);

			// Ensure CATui-style defaults (gba_a, gba_b, etc.) exist before the core polls input.
				string coreIdForBindings = !string.IsNullOrWhiteSpace(LibretroNative.CurrentCoreId)
					? LibretroNative.CurrentCoreId
					: GetCoreFallbackId();
				_isDolphinCore = string.Equals(coreIdForBindings, "dolphin", StringComparison.OrdinalIgnoreCase);
				_isPpssppCore = string.Equals(coreIdForBindings, "ppsspp", StringComparison.OrdinalIgnoreCase) ||
					LibretroNative.CurrentCore == EmulatorCore.PSP_PPSSPP;
				// PPSSPP software-only policy for macOS while no HW context bridge exists.
				_forcePpssppSoftwareMode = _isPpssppCore && OS.GetName() == "macOS";
				if (_forcePpssppSoftwareMode && !_loggedPpssppSoftwarePolicy)
				{
					_loggedPpssppSoftwarePolicy = true;
					FileLogger.Log("[ppsspp] software-only policy active on macOS (forcing backend=none, denying HW context requests).");
				}
				InputMapper.EnsureDefaultActions(coreIdForBindings);
				CacheValidActions(forceRefresh: true);
				PrepareEnvironmentDirectories();

			// FileLogger.Log("[LibretroPlayer] Setting callbacks to core...");
			LibretroNative.retro_set_environment(_envCallback);
			LibretroNative.retro_set_video_refresh(_videoCallback);
			LibretroNative.retro_set_input_poll(_inputPollCallback);
			LibretroNative.retro_set_input_state(_inputStateCallback);
			LibretroNative.retro_set_audio_sample(_audioCallback);
			LibretroNative.retro_set_audio_sample_batch(_audioBatchCallback);

			FileLogger.Log("[libretroplayer] calling retro_init...");
			LibretroNative.retro_init();
			FileLogger.Log("[libretroplayer] retro_init complete.");
			_coreInitialized = true;
			// FileLogger.Log("[LibretroPlayer] Core initialized!");

			// FileLogger.Log($"[libretroplayer] checking rom: {_currentRomPath}");
				if (!System.IO.File.Exists(_currentRomPath))
				{
					FileLogger.Error($"[libretroplayer] rom not found: {_currentRomPath}");
					return;
				}
				FileLogger.Log($"[libretroplayer] rom path: {_currentRomPath}");
				try
				{
					long romSize = new FileInfo(_currentRomPath).Length;
					FileLogger.Log($"[libretroplayer] rom size bytes: {romSize}");
				}
				catch (Exception ex)
				{
					FileLogger.Error($"[libretroplayer] failed to stat rom: {ex.Message}");
				}
				// FileLogger.Log("[LibretroPlayer] ROM file exists!");

			FileLogger.Log("[LibretroPlayer] Getting system info...");
			retro_system_info sysInfo = new retro_system_info();
			LibretroNative.retro_get_system_info(ref sysInfo);
			
			bool needFullPath = sysInfo.need_fullpath;
			
				retro_game_info gameInfo = new retro_game_info();
				// libretro receives a raw C-string pointer for gameInfo.path.
				if (_romPathPtr != IntPtr.Zero)
					Marshal.FreeHGlobal(_romPathPtr);
				_romPathPtr = Marshal.StringToHGlobalAnsi(_currentRomPath);
				gameInfo.path = _romPathPtr;
				gameInfo.meta = IntPtr.Zero;

			if (!needFullPath)
			{
				FileLogger.Log("[LibretroPlayer] Loading ROM data into memory...");
				byte[] romData = File.ReadAllBytes(_currentRomPath);
				
				if (_romDataPtr != IntPtr.Zero)
					Marshal.FreeHGlobal(_romDataPtr);
				
				_romDataPtr = Marshal.AllocHGlobal(romData.Length);
				Marshal.Copy(romData, 0, _romDataPtr, romData.Length);
				
				gameInfo.data = _romDataPtr;
				gameInfo.size = (nuint)romData.LongLength;
			}
			else
			{
				gameInfo.data = IntPtr.Zero;
				gameInfo.size = 0;
				FileLogger.Log("[LibretroPlayer] Using full path mode");
			}

			FileLogger.Log("[libretroplayer] loading game...");
			if (LibretroNative.retro_load_game(ref gameInfo))
			{
				if (_abortStartupDueToHardwareRender)
				{
					FileLogger.Error("[libretroplayer] startup aborted: core requires HW rendering but this frontend has no HW context bridge.");
					StopGame();
					return;
				}

				FileLogger.Log("[libretroplayer] game loaded!");
				
				retro_system_av_info avInfo = new retro_system_av_info();
				LibretroNative.retro_get_system_av_info(ref avInfo);

				FileLogger.Log($"[libretroplayer] sample rate: {avInfo.timing.sample_rate}");
				((AudioStreamGenerator)_gameAudio.Stream).MixRate = (float)avInfo.timing.sample_rate;

				LoadSRAM();

				_isGameRunning = true;
				FileLogger.Log("[libretroplayer] game running now");
			}
			else
			{
				FileLogger.Error("[libretroplayer] failed to load game :(");
				if (_abortStartupDueToHardwareRender)
				{
					FileLogger.Error("[libretroplayer] core boot failed because RETRO_ENVIRONMENT_SET_HW_RENDER is unsupported by this frontend.");
				}
				StopGame();
			}
		}
		catch (Exception e)
		{
			FileLogger.Error($"[libretroplayer] exception: {e.Message}");
		}
		FileLogger.Log("[libretroplayer] startemulator - end");
	}

	public override void _ExitTree()
	{
		StopGame();
	}

	private bool _isPaused = false;

	public void SetPaused(bool paused)
	{
		if (paused && _isGameRunning && !_isPaused)
		{
			SaveSRAM();
		}
		
		_isPaused = paused;
		
		if (!paused)
		{
			_ignoreInputTimer = 0.2;
		}
		
		if (_gameAudio != null)
		{
			_gameAudio.StreamPaused = paused;
		}
	}

	public bool IsRunning()
	{
		return _isGameRunning;
	}

	// optimization: audio processing with static buffers
	private static nuint AudioBatchCallback(IntPtr data, nuint frames)
	{
		if (_instance == null || data == IntPtr.Zero) return 0;
		if (frames == 0) return 0;
		if (frames > (nuint)(int.MaxValue / 2)) return 0;

		int samples = checked((int)frames * 2);
		if (samples <= 0) return 0;

		lock (_audioFrameLock)
		{
			if (_pendingAudioRaw == null || _pendingAudioRaw.Length < samples)
				_pendingAudioRaw = new short[samples];

			Marshal.Copy(data, _pendingAudioRaw, 0, samples);
			_pendingAudioSamples = samples;
			_hasPendingAudioBatch = true;
		}

		return frames;
	}

	private static void AudioSampleCallback(short left, short right)
	{
		if (_instance == null) return;
		lock (_audioFrameLock)
		{
			_pendingAudioSampleLeft = left;
			_pendingAudioSampleRight = right;
			_hasPendingAudioSample = true;
		}
	}

	private static void InputPollCallback() { }

	private static short InputStateCallback(uint port, uint device, uint index, uint id)
	{
		if (_instance != null && _instance._ignoreInputTimer > 0) return 0;

		if (port != 0) return 0;
		if (device != RetroDeviceJoypad) return 0;
		if (id == RetroDeviceIdJoypadMask) return BuildJoypadInputBitmask();
		if (id >= (uint)_inputStateCache.Length) return 0;
		return _inputStateCache[(int)id];
	}

	private static short BuildJoypadInputBitmask()
	{
		ushort mask = 0;
		for (int i = 0; i < _inputStateCache.Length; i++)
		{
			if (_inputStateCache[i] != 0)
				mask |= (ushort)(1 << i);
		}

		return unchecked((short)mask);
	}

	private static bool SetRumbleStateCallback(uint port, uint effect, ushort strength)
	{
		// No frontend haptics bridge yet. Return true so cores don't treat this as fatal.
		return true;
	}

	private static string GetCoreFallbackId()
	{
		return LibretroNative.CurrentCore switch
		{
			EmulatorCore.GBA_MGBA => "gba",
			EmulatorCore.GBA_VBAM => "gba",
			EmulatorCore.SNES_SNES9X => "snes",
			EmulatorCore.NES_FCEUMM => "nes",
			EmulatorCore.GB_GAMBATTE => "gb",
			EmulatorCore.GBC_GAMBATTE => "gbc",
			EmulatorCore.PSP_PPSSPP => "ppsspp",
			_ => "gba"
		};
	}

	private static IntPtr _saveDirectoryPtr = IntPtr.Zero;
	private static IntPtr _systemDirectoryPtr = IntPtr.Zero;
	private static IntPtr _coreAssetsDirectoryPtr = IntPtr.Zero;
	private static string _resolvedSaveDirectory = string.Empty;
	private static string _resolvedSystemDirectory = string.Empty;
	private static string _resolvedAssetsDirectory = string.Empty;

	private static bool EnvironmentCallback(uint cmd, IntPtr data)
	{
		switch (cmd)
		{
			case LibretroNative.RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
				if (data != IntPtr.Zero && _instance != null)
				{
					_instance._currentPixelFormat = (retro_pixel_format)Marshal.ReadInt32(data);
				}
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
				if (data == IntPtr.Zero || _saveDirectoryPtr == IntPtr.Zero) return false;
				Marshal.WriteIntPtr(data, _saveDirectoryPtr);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
				if (data == IntPtr.Zero || _systemDirectoryPtr == IntPtr.Zero) return false;
				Marshal.WriteIntPtr(data, _systemDirectoryPtr);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY:
				if (data == IntPtr.Zero || _coreAssetsDirectoryPtr == IntPtr.Zero) return false;
				Marshal.WriteIntPtr(data, _coreAssetsDirectoryPtr);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_CAN_DUPE:
				if (data != IntPtr.Zero) Marshal.WriteByte(data, 1);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_LOG_INTERFACE:
				if (data == IntPtr.Zero)
					return false;

				if (!TryGetRetroLogCallbackPointer(out IntPtr logPtr))
					return false;

				var logCb = new retro_log_callback { log = logPtr };
				Marshal.StructureToPtr(logCb, data, false);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_SET_MESSAGE:
				return TryLogFrontendMessage(data, extended: false);

			case LibretroNative.RETRO_ENVIRONMENT_SET_MESSAGE_EXT:
				return TryLogFrontendMessage(data, extended: true);

			case LibretroNative.RETRO_ENVIRONMENT_GET_MESSAGE_INTERFACE_VERSION:
				if (data != IntPtr.Zero)
				{
					// We handle RETRO_ENVIRONMENT_SET_MESSAGE_EXT in this frontend.
					Marshal.WriteInt32(data, 1);
				}
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION:
				if (data != IntPtr.Zero)
				{
					// Support modern core options negotiation path.
					Marshal.WriteInt32(data, 2);
				}
				return true;

				case LibretroNative.RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL:
					// Optional frontend hint; safe to accept as a no-op.
					return true;

				case LibretroNative.RETRO_ENVIRONMENT_GET_RUMBLE_INTERFACE:
					if (data == IntPtr.Zero)
						return false;

					_retroSetRumbleStateShim ??= SetRumbleStateCallback;
					IntPtr rumbleCallbackPtr = Marshal.GetFunctionPointerForDelegate(_retroSetRumbleStateShim);
					if (rumbleCallbackPtr == IntPtr.Zero)
						return false;

					var rumbleInterface = new retro_rumble_interface
					{
						set_rumble_state = rumbleCallbackPtr
					};
					Marshal.StructureToPtr(rumbleInterface, data, false);
					return true;
				
				case LibretroNative.RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO:
				case LibretroNative.RETRO_ENVIRONMENT_SET_GEOMETRY:
					// Safe no-op for software-only mode; core can continue running.
					return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_DISK_CONTROL_INTERFACE_VERSION:
			case LibretroNative.RETRO_ENVIRONMENT_GET_MESSAGE_INTERFACE_VERSION_LEGACY:
				if (data != IntPtr.Zero)
				{
					Marshal.WriteInt32(data, 1);
				}
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS:
			case LibretroNative.RETRO_ENVIRONMENT_SET_CONTROLLER_INFO:
			case LibretroNative.RETRO_ENVIRONMENT_SET_MEMORY_MAPS:
			case LibretroNative.RETRO_ENVIRONMENT_SET_SUPPORT_ACHIEVEMENTS:
			case LibretroNative.RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2:
			case LibretroNative.RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL:
			case LibretroNative.RETRO_ENVIRONMENT_SET_CORE_OPTIONS_DISPLAY:
			case LibretroNative.RETRO_ENVIRONMENT_SET_CORE_OPTIONS_UPDATE_DISPLAY_CALLBACK_LEGACY:
			case LibretroNative.RETRO_ENVIRONMENT_SET_CORE_OPTIONS_UPDATE_DISPLAY_CALLBACK:
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_SET_MINIMUM_AUDIO_LATENCY:
				if (data != IntPtr.Zero)
					_requestedMinimumAudioLatencyMs = unchecked((uint)Marshal.ReadInt32(data));
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_INPUT_BITMASKS_LEGACY:
			case LibretroNative.RETRO_ENVIRONMENT_GET_INPUT_BITMASKS:
				// We only poll per-button states today; still advertise bitmask support for compatibility.
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_USERNAME:
				if (data == IntPtr.Zero)
					return false;

				IntPtr usernamePtr = GetOrCreateAnsiPointer(ref _usernamePtr, System.Environment.UserName);
				Marshal.WriteIntPtr(data, usernamePtr);
				return true;

				case LibretroNative.RETRO_ENVIRONMENT_GET_LANGUAGE:
					if (data != IntPtr.Zero)
					{
						// RETRO_LANGUAGE_ENGLISH = 0
						Marshal.WriteInt32(data, 0);
					}
					return true;

				case LibretroNative.RETRO_ENVIRONMENT_GET_TARGET_SAMPLE_RATE:
					if (data != IntPtr.Zero)
						Marshal.WriteInt32(data, unchecked((int)FrontendTargetSampleRateHz));
					return true;

				case LibretroNative.RETRO_ENVIRONMENT_GET_VFS_INTERFACE:
					// The frontend does not expose libretro VFS yet; cores should fall back to stdio.
					return false;

				case LibretroNative.RETRO_ENVIRONMENT_GET_PREFERRED_HW_RENDER:
					if (_forcePpssppSoftwareMode)
					{
						// RETRO_HW_CONTEXT_NONE = 0
						if (data != IntPtr.Zero)
							Marshal.WriteInt32(data, 0);
						return true;
					}
					if (_isDolphinCore)
					{
						// Tell Dolphin we don't have a preferred hardware API.
						if (data != IntPtr.Zero)
							Marshal.WriteInt32(data, 0);
					return true;
				}
				return false;

				case LibretroNative.RETRO_ENVIRONMENT_SET_HW_RENDER:
					if (_forcePpssppSoftwareMode)
					{
						// Keep PPSSPP in software mode on macOS; do not trigger HW-abort paths.
						if (!_loggedPpssppHwRenderDenied)
						{
							_loggedPpssppHwRenderDenied = true;
							FileLogger.Error("[ppsspp] software-only policy denied RETRO_ENVIRONMENT_SET_HW_RENDER on macOS.");
						}
						return false;
					}
					_coreRequestedHardwareRender = true;
					if (_frontendSupportsHardwareRender)
					{
						return true;
					}
					if (_isDolphinCore)
					{
						_abortStartupDueToHardwareRender = true;
					if (!_loggedHwRenderUnsupported)
					{
						_loggedHwRenderUnsupported = true;
						FileLogger.Error("[dolphin] core requested RETRO_ENVIRONMENT_SET_HW_RENDER, but this frontend has no HW context bridge.");
					}
				}
				else if (!_loggedHwRenderUnsupportedGeneric)
				{
					_loggedHwRenderUnsupportedGeneric = true;
					FileLogger.Error("[libretroplayer] core requested RETRO_ENVIRONMENT_SET_HW_RENDER, frontend has no HW context bridge; attempting fallback.");
				}
				return false;

				case LibretroNative.RETRO_ENVIRONMENT_GET_HW_RENDER_INTERFACE:
					if (_forcePpssppSoftwareMode)
						return false;
					if (_coreRequestedHardwareRender && !_frontendSupportsHardwareRender)
						_abortStartupDueToHardwareRender = true;
					if (_isDolphinCore && !_loggedHwRenderInterfaceUnsupported)
				{
					_loggedHwRenderInterfaceUnsupported = true;
					FileLogger.Error("[dolphin] core requested RETRO_ENVIRONMENT_GET_HW_RENDER_INTERFACE, unsupported.");
				}
				return false;

				case LibretroNative.RETRO_ENVIRONMENT_SET_HW_SHARED_CONTEXT:
					if (_forcePpssppSoftwareMode)
						return false;
					if (_coreRequestedHardwareRender && !_frontendSupportsHardwareRender)
						_abortStartupDueToHardwareRender = true;
					if (_isDolphinCore && !_loggedHwSharedContextUnsupported)
				{
					_loggedHwSharedContextUnsupported = true;
					FileLogger.Error("[dolphin] core requested RETRO_ENVIRONMENT_SET_HW_SHARED_CONTEXT, unsupported.");
				}
				return false;

				case LibretroNative.RETRO_ENVIRONMENT_SET_HW_RENDER_CONTEXT_NEGOTIATION_INTERFACE:
					if (_forcePpssppSoftwareMode)
						return false;
					if (_coreRequestedHardwareRender && !_frontendSupportsHardwareRender)
						_abortStartupDueToHardwareRender = true;
					if (_isDolphinCore && !_loggedHwNegotiationUnsupported)
				{
					_loggedHwNegotiationUnsupported = true;
					FileLogger.Error("[dolphin] core requested RETRO_ENVIRONMENT_SET_HW_RENDER_CONTEXT_NEGOTIATION_INTERFACE, unsupported.");
				}
				return false;

			case LibretroNative.RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME:
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_SET_VARIABLES:
				RegisterCoreVariables(data);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_VARIABLE:
				if (data == IntPtr.Zero) return false;
				retro_variable variable = Marshal.PtrToStructure<retro_variable>(data);
				string key = Marshal.PtrToStringAnsi(variable.key);
				if (string.IsNullOrEmpty(key)) return false;

				if (_isDolphinCore)
				{
					// Avoid touching Godot scene/config objects from core callback threads.
					if (!TryGetDolphinVariableValue(key, out string dolphinValue))
						return false;

					SetVariableValuePointer(key, dolphinValue, ref variable, data);
					LogDolphinResolvedVariable(key, dolphinValue);
					return true;
				}

				if (key.StartsWith("ppsspp_", StringComparison.OrdinalIgnoreCase))
				{
					if (!TryGetPpssppVariableValue(key, out string ppssppValue))
						return false;

					SetVariableValuePointer(key, ppssppValue, ref variable, data);
					LogPpssppResolvedVariable(key, ppssppValue);
					return true;
				}

				if (!TryGetRegisteredFallbackValue(key, out string fallbackValue))
					return false;

				// Returning only registered/default option values keeps this callback thread-safe.
				SetVariableValuePointer(key, fallbackValue, ref variable, data);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE:
				if (data != IntPtr.Zero)
				{
					Marshal.WriteByte(data, 0);
				}
				return true;

			default:
				LogUnhandledEnvironmentCommand(cmd);
				return false;
		}
	}

	private static bool TryGetRetroLogCallbackPointer(out IntPtr callbackPtr)
	{
		// Use a native variadic logger when available so cores that call log_cb(..., fmt, ...)
		// during retro_init don't hit reverse P/Invoke varargs limitations.
		if (TryGetNativeSyslogCallback(out callbackPtr))
			return true;

		_retroLogPrintfShim ??= RetroLogPrintfShim;
		callbackPtr = Marshal.GetFunctionPointerForDelegate(_retroLogPrintfShim);
		return callbackPtr != IntPtr.Zero;
	}

	private static bool TryGetNativeSyslogCallback(out IntPtr callbackPtr)
	{
		if (_nativeRetroLogCallbackPtr != IntPtr.Zero)
		{
			callbackPtr = _nativeRetroLogCallbackPtr;
			return true;
		}

		string[] libraryCandidates = OS.GetName() switch
		{
			"macOS" => new[] { "/usr/lib/libSystem.B.dylib", "libSystem.B.dylib" },
			"Linux" => new[] { "libc.so.6", "libc.so" },
			_ => Array.Empty<string>()
		};

		foreach (string libraryPath in libraryCandidates)
		{
			try
			{
				if (!NativeLibrary.TryLoad(libraryPath, out IntPtr handle))
					continue;

				if (NativeLibrary.TryGetExport(handle, "syslog", out IntPtr syslogPtr) && syslogPtr != IntPtr.Zero)
				{
					_nativeRetroLogLibraryHandle = handle;
					_nativeRetroLogCallbackPtr = syslogPtr;
					callbackPtr = syslogPtr;
					return true;
				}

				NativeLibrary.Free(handle);
			}
			catch
			{
				// Ignore and continue trying other system library names.
			}
		}

		callbackPtr = IntPtr.Zero;
		return false;
	}

	private static IntPtr GetOrCreateAnsiPointer(ref IntPtr ptr, string value)
	{
		if (ptr == IntPtr.Zero)
			ptr = Marshal.StringToHGlobalAnsi(value ?? string.Empty);

		return ptr;
	}

	private static bool TryLogFrontendMessage(IntPtr data, bool extended)
	{
		if (data == IntPtr.Zero)
			return false;

		IntPtr messagePtr = IntPtr.Zero;
		if (extended)
		{
			retro_message_ext msg = Marshal.PtrToStructure<retro_message_ext>(data);
			messagePtr = msg.msg;
		}
		else
		{
			retro_message msg = Marshal.PtrToStructure<retro_message>(data);
			messagePtr = msg.msg;
		}

		if (messagePtr == IntPtr.Zero)
			return true;

		string message = Marshal.PtrToStringAnsi(messagePtr) ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(message))
			FileLogger.Log($"[libretro-msg] {message.TrimEnd()}");

		return true;
	}

	private static void LogUnhandledEnvironmentCommand(uint cmd)
	{
		lock (_environmentLogLock)
		{
			if (!_loggedUnhandledEnvironmentCommands.Add(cmd))
				return;
		}

		FileLogger.Log($"[libretro-env] unhandled cmd: {cmd}");
	}

	private static void SetVariableValuePointer(string key, string value, ref retro_variable variable, IntPtr dataPtr)
	{
		lock (_variablePtrLock)
		{
			if (_variableValuePtrs.TryGetValue(key, out IntPtr existingPtr) && existingPtr != IntPtr.Zero)
			{
				Marshal.FreeHGlobal(existingPtr);
			}

			// Core keeps this pointer after callback returns, so we allocate unmanaged memory.
			IntPtr valuePtr = Marshal.StringToHGlobalAnsi(value);
			_variableValuePtrs[key] = valuePtr;
			variable.value = valuePtr;
			Marshal.StructureToPtr(variable, dataPtr, false);
		}
	}

	private static void RegisterCoreVariables(IntPtr data)
	{
		if (data == IntPtr.Zero)
			return;

		lock (_registeredVariablesLock)
		{
			_registeredCoreVariables.Clear();
			_loggedDolphinResolvedKeys.Clear();

			int structSize = Marshal.SizeOf<retro_variable>();
			IntPtr current = data;

			while (true)
			{
				retro_variable variable = Marshal.PtrToStructure<retro_variable>(current);
				if (variable.key == IntPtr.Zero)
					break;

				string key = Marshal.PtrToStringAnsi(variable.key);
				string definition = variable.value != IntPtr.Zero ? Marshal.PtrToStringAnsi(variable.value) : string.Empty;

						if (!string.IsNullOrEmpty(key))
						{
							string[] options = ParseCoreVariableOptions(definition);
							_registeredCoreVariables[key] = options;
							if (key.StartsWith("dolphin_", StringComparison.OrdinalIgnoreCase) && options.Length > 0)
							{
								FileLogger.Log($"[dolphin] options {key} = {string.Join("|", options)}");
								if (string.Equals(key, "dolphin_renderer", StringComparison.OrdinalIgnoreCase) &&
									string.IsNullOrEmpty(SelectOptionByToken(options, "software")))
								{
									FileLogger.Error("[dolphin] renderer options are hardware-only in this core build.");
								}
							}
							else if (key.StartsWith("ppsspp_", StringComparison.OrdinalIgnoreCase) && options.Length > 0)
							{
								FileLogger.Log($"[ppsspp] options {key} = {string.Join("|", options)}");
							}
						}

				current = IntPtr.Add(current, structSize);
			}
		}
	}

	private static string[] ParseCoreVariableOptions(string definition)
	{
		if (string.IsNullOrWhiteSpace(definition))
			return Array.Empty<string>();

		string payload = definition;
		int delimiter = definition.IndexOf(';');
		if (delimiter >= 0 && delimiter + 1 < definition.Length)
			payload = definition.Substring(delimiter + 1);

		string[] split = payload.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return split.Length == 0 ? Array.Empty<string>() : split;
	}

	private static bool TryGetRegisteredOption(string key, out string[] options)
	{
		lock (_registeredVariablesLock)
		{
			if (_registeredCoreVariables.TryGetValue(key, out var stored))
			{
				options = stored;
				return true;
			}
		}

		options = Array.Empty<string>();
		return false;
	}

	private static bool TryGetRegisteredFallbackValue(string key, out string value)
	{
		if (TryGetRegisteredOption(key, out string[] options) && options.Length > 0)
		{
			value = options[0];
			return true;
		}

		value = string.Empty;
		return false;
	}

	private static string SelectOptionByToken(string[] options, params string[] tokens)
	{
		foreach (string option in options)
		{
			string normalized = option.ToLowerInvariant();
			foreach (string token in tokens)
			{
				if (normalized.Contains(token, StringComparison.Ordinal))
					return option;
			}
		}

		return string.Empty;
	}

	private static bool TrySelectLowestNumericOption(string[] options, out string value)
	{
		int min = int.MaxValue;
		string selected = string.Empty;

		foreach (string option in options)
		{
			if (!int.TryParse(option, out int numeric))
				continue;

			if (numeric < min)
			{
				min = numeric;
				selected = option;
			}
		}

		value = selected;
		return !string.IsNullOrEmpty(value);
	}

	private static void LogDolphinResolvedVariable(string key, string value)
	{
		lock (_registeredVariablesLock)
		{
			if (!_loggedDolphinResolvedKeys.Add(key))
				return;
		}

		FileLogger.Log($"[dolphin] variable {key} = {value}");
	}

	private static void LogPpssppResolvedVariable(string key, string value)
	{
		lock (_registeredVariablesLock)
		{
			if (!_loggedPpssppResolvedKeys.Add(key))
				return;
		}

		FileLogger.Log($"[ppsspp] variable {key} = {value}");
	}

	private static bool TryGetDolphinVariableValue(string key, out string value)
	{
		// Stable defaults for in-process Dolphin; avoids thread-unsafe config lookups.
		string normalized = key.ToLowerInvariant();
		if (!normalized.StartsWith("dolphin_", StringComparison.Ordinal))
		{
			value = string.Empty;
			return false;
		}

		if (normalized.Contains("renderer"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				// Use only advertised option values; forcing hidden values (e.g. "Software")
				// can crash some release Dolphin core builds during boot.
				value = SelectOptionByToken(options, "hardware");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			// Let the core keep its internal default when no option table is available.
			value = string.Empty;
			return false;
		}

		if (normalized.Contains("cpu_thread"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				// Prefer dual-core scheduling; single-core startup can stall before video backend init.
				value = SelectOptionByToken(options, "enabled", "on", "true", "dual");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "enabled";
			return true;
		}

		if (normalized.Contains("fastmem"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				value = SelectOptionByToken(options, "disabled", "off", "false");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "disabled";
			return true;
		}

		if (normalized.Contains("dsp_thread"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				value = SelectOptionByToken(options, "disabled", "off", "false");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "disabled";
			return true;
		}

		if (normalized.Contains("skip_gc_bios") || normalized.Contains("skip_ipl"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				value = SelectOptionByToken(options, "enabled", "on", "true", "skip");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "enabled";
			return true;
		}

		if (normalized.Contains("cpu_core"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				// Numeric enums are common here (e.g. 0 = Interpreter). Prefer lowest value for stability.
				if (TrySelectLowestNumericOption(options, out value)) return true;

				value = SelectOptionByToken(options, "cached interpreter", "interpreter");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "cached_interpreter";
			return true;
		}

		value = string.Empty;
		return false;
	}

	private static bool TryGetPpssppVariableValue(string key, out string value)
	{
		string normalized = key.ToLowerInvariant();
		if (!normalized.StartsWith("ppsspp_", StringComparison.Ordinal))
		{
			value = string.Empty;
			return false;
		}

		if (normalized.Contains("software_rendering"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				value = SelectOptionByToken(options, "enabled", "on", "true", "software");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "enabled";
			return true;
		}

			if (normalized.Contains("backend"))
			{
				if (_forcePpssppSoftwareMode)
				{
					value = "none";
					if (!_loggedPpssppBackendOverride)
					{
						_loggedPpssppBackendOverride = true;
						FileLogger.Log("[ppsspp] software-only policy forcing ppsspp_backend=none.");
					}
					return true;
				}

				if (TryGetRegisteredOption(key, out var options))
				{
					value = SelectOptionByToken(options, "software");
					if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = string.Empty;
			return false;
		}

		if (normalized.Contains("cpu_core"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				if (TrySelectLowestNumericOption(options, out value)) return true;

				value = SelectOptionByToken(options, "interpreter");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "0";
			return true;
		}

		if (normalized.Contains("fast_memory") ||
			normalized.Contains("gpu_hardware_transform") ||
			normalized.Contains("hardware_tesselation"))
		{
			if (TryGetRegisteredOption(key, out var options))
			{
				value = SelectOptionByToken(options, "disabled", "off", "false");
				if (!string.IsNullOrEmpty(value)) return true;
				if (options.Length > 0) { value = options[0]; return true; }
			}

			value = "disabled";
			return true;
		}

		value = string.Empty;
		return false;
	}
	
	private static string GetCoreVariableValue(string key)
	{
		if (string.IsNullOrEmpty(LibretroNative.CurrentCoreId)) return null;
		
		var sceneTree = (SceneTree)Engine.GetMainLoop();
		var emulatorConfig = sceneTree.Root.GetNodeOrNull<Node>("EmulatorConfig");
		
		if (emulatorConfig == null) return null;
		
		string coreId = LibretroNative.CurrentCoreId;
		string settingKey = ExtractSettingKeyFromVariable(key);
		
		if (string.IsNullOrEmpty(settingKey)) return null;
		
		var value = emulatorConfig.Call("get_emulator_setting", coreId, "core", settingKey, default(Variant));
		
		if (value.Obj == null) return null;
		
		return ConvertSettingToLibretroValue(key, settingKey, value);
	}
	
	private static string ExtractSettingKeyFromVariable(string variableKey)
	{
		if (variableKey.Contains("resolution")) return "internal_resolution";
		if (variableKey.Contains("dithering")) return "dithering";
		if (variableKey.Contains("enhanced")) return "enhanced_resolution";
		if (variableKey.Contains("interpolation")) return "audio_interpolation";
		if (variableKey.Contains("superfx") || variableKey.Contains("overclock")) return "superfx_overclock";
		if (variableKey.Contains("slowdown")) return "reduce_slowdown";
		if (variableKey.Contains("region")) return "region";
		if (variableKey.Contains("audio") && variableKey.Contains("quality")) return "audio_quality";
		if (variableKey.Contains("68k")) return "m68k_overclock";
		
		return null;
	}
	
	private static string ConvertSettingToLibretroValue(string variableKey, string settingKey, Variant value)
	{
		switch (settingKey)
		{
			case "internal_resolution":
				string res = value.ToString();
				if (res.Contains("1x")) return "1";
				if (res.Contains("2x")) return "2";
				if (res.Contains("4x")) return "4";
				if (res.Contains("8x")) return "8";
				return "1";
			
			case "dithering":
			case "enhanced_resolution":
			case "reduce_slowdown":
				return value.AsBool() ? "enabled" : "disabled";
			
			case "audio_interpolation":
				return value.ToString().ToLower();
			
			case "superfx_overclock":
			case "m68k_overclock":
				// Handle float to int conversion safely
				float floatVal = 100.0f;
				try { floatVal = value.AsSingle(); } catch { try { floatVal = value.AsInt32(); } catch { } }
				return ((int)floatVal).ToString();
			
			case "region":
				string region = value.ToString();
				if (region.Contains("Auto")) return "auto";
				if (region.Contains("Japan")) return "ntsc-j";
				if (region.Contains("USA")) return "ntsc-u";
				if (region.Contains("Europe")) return "pal";
				return "auto";
			
			case "audio_quality":
				return value.ToString().ToLower();
			
			default:
				return value.ToString();
		}
	}

	// Copy frame bytes in callback thread, then upload texture in _Process on the main thread.
	private static void VideoCallback(IntPtr data, uint width, uint height, uint pitch)
	{
		if (_instance == null || data == IntPtr.Zero) return;
		if (width == 0 || height == 0 || pitch == 0) return;

		// libretro signals GPU-backed frames with RETRO_HW_FRAME_BUFFER_VALID ((void*)-1).
		// Without an HW context bridge, attempting to Marshal.Copy from this sentinel would crash.
		if (data == new IntPtr(-1))
		{
			if (!_loggedHwVideoPointer)
			{
				_loggedHwVideoPointer = true;
				FileLogger.Error("[libretroplayer] received RETRO_HW_FRAME_BUFFER_VALID in VideoCallback without HW bridge; dropping frame.");
			}
			return;
		}

		int bytesPerPixel = GetBytesPerPixel(_instance._currentPixelFormat);
		long lineSizeLong = (long)width * bytesPerPixel;
		if (lineSizeLong <= 0 || lineSizeLong > int.MaxValue) return;
		int lineSize = (int)lineSizeLong;
		long totalSizeLong = lineSizeLong * height;
		if (totalSizeLong <= 0 || totalSizeLong > int.MaxValue) return;
		int totalSize = (int)totalSizeLong;
		int rowCopyBytes = (int)Math.Min((uint)lineSize, pitch);
		if (rowCopyBytes <= 0) return;

		if (!_loggedFirstVideoFrame)
		{
			_loggedFirstVideoFrame = true;
			FileLogger.Log($"[libretroplayer] first video frame ptr=0x{data.ToInt64():X} w={width} h={height} pitch={pitch} bpp={bytesPerPixel}");
		}
		
		lock (_videoFrameLock)
		{
			if (_pendingVideoFrame == null || _pendingVideoFrame.Length != totalSize)
				_pendingVideoFrame = new byte[totalSize];

			for (int y = 0; y < height; y++)
			{
				IntPtr src = IntPtr.Add(data, y * (int)pitch);
				Marshal.Copy(src, _pendingVideoFrame, y * lineSize, rowCopyBytes);
			}

			_pendingVideoWidth = (int)width;
			_pendingVideoHeight = (int)height;
			_pendingVideoFormat = (int)_instance._currentPixelFormat;
			_hasPendingVideoFrame = true;
		}
	}

	private static int GetBytesPerPixel(retro_pixel_format format)
	{
		return format switch
		{
			retro_pixel_format.RETRO_PIXEL_FORMAT_0RGB1555 => 2,
			retro_pixel_format.RETRO_PIXEL_FORMAT_RGB565 => 2,
			retro_pixel_format.RETRO_PIXEL_FORMAT_XRGB8888 => 4,
			_ => 2
		};
	}

	private static Image.Format GetGodotFormat(retro_pixel_format format)
	{
		return format switch
		{
			retro_pixel_format.RETRO_PIXEL_FORMAT_0RGB1555 => Image.Format.Rgb565,
			retro_pixel_format.RETRO_PIXEL_FORMAT_RGB565 => Image.Format.Rgb565,
			retro_pixel_format.RETRO_PIXEL_FORMAT_XRGB8888 => Image.Format.Rgba8,
			_ => Image.Format.Rgb565
		};
	}

	public void SaveSRAM()
	{
		if (!_coreInitialized || LibretroNative.retro_get_memory_data == null) return;

		nuint size = LibretroNative.retro_get_memory_size(LibretroNative.RETRO_MEMORY_SAVE_RAM);
		if (size == 0 || size > (nuint)int.MaxValue) return;

		IntPtr ptr = LibretroNative.retro_get_memory_data(LibretroNative.RETRO_MEMORY_SAVE_RAM);
		if (ptr == IntPtr.Zero) return;

		byte[] data = new byte[(int)size];
		Marshal.Copy(ptr, data, 0, (int)size);

		string saveName = Path.GetFileNameWithoutExtension(_currentRomPath);
		string saveDir = ProjectSettings.GlobalizePath("user://saves/");
		
		if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

		string srmPath = Path.Combine(saveDir, saveName + ".srm");
		File.WriteAllBytes(srmPath, data);

		string jsonPath = Path.Combine(saveDir, saveName + ".json");
		string jsonContent = $@"{{
			""game_name"": ""{saveName}"",
			""rom_path"": ""{_currentRomPath.Replace("\\", "\\\\")}"",
			""timestamp"": ""{DateTime.Now.ToString("s")}""
		}}";
		File.WriteAllText(jsonPath, jsonContent);
	}

	public void SaveState(string path)
	{
		if (!_coreInitialized || LibretroNative.retro_serialize_size == null) return;
		nuint size = LibretroNative.retro_serialize_size();
		if (size == 0 || size > (nuint)int.MaxValue) return;
		IntPtr ptr = Marshal.AllocHGlobal((int)size);
		try
		{
			if (LibretroNative.retro_serialize(ptr, size))
			{
				byte[] data = new byte[(int)size];
				Marshal.Copy(ptr, data, 0, (int)size);
				string dir = Path.GetDirectoryName(path);
				if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllBytes(path, data);
			}
		}
		finally { Marshal.FreeHGlobal(ptr); }
	}

	public void LoadState(string path)
	{
		if (!_coreInitialized || LibretroNative.retro_unserialize == null) return;
		if (!File.Exists(path)) return;
		byte[] data = File.ReadAllBytes(path);
		nuint size = (nuint)data.LongLength;
		IntPtr ptr = Marshal.AllocHGlobal((int)size);
		try
		{
			Marshal.Copy(data, 0, ptr, (int)size);
			LibretroNative.retro_unserialize(ptr, size);
		}
		finally { Marshal.FreeHGlobal(ptr); }
	}

	public void CaptureScreenshot(string path)
	{
		if (_gameTexture == null) return;
		Image img = _gameTexture.GetImage();
		if (img != null)
		{
			string dir = Path.GetDirectoryName(path);
			if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
			img.SavePng(path);
		}
	}

	private void LoadSRAM()
	{
		if (!_coreInitialized || LibretroNative.retro_get_memory_data == null) return;
		nuint size = LibretroNative.retro_get_memory_size(LibretroNative.RETRO_MEMORY_SAVE_RAM);
		if (size == 0 || size > (nuint)int.MaxValue) return;

		string saveName = Path.GetFileNameWithoutExtension(_currentRomPath);
		string saveDir = ProjectSettings.GlobalizePath("user://saves/");
		string srmPath = Path.Combine(saveDir, saveName + ".srm");
		if (!File.Exists(srmPath)) return;

		byte[] data = File.ReadAllBytes(srmPath);
		int copySize = Math.Min((int)size, data.Length);
		IntPtr ptr = LibretroNative.retro_get_memory_data(LibretroNative.RETRO_MEMORY_SAVE_RAM);
		if (ptr != IntPtr.Zero) Marshal.Copy(data, 0, ptr, copySize);
	}

	public void UpdateGameTexture(byte[] pixels, int width, int height, int formatInt)
	{
		if (_screenRect == null) return;

		retro_pixel_format format = (retro_pixel_format)formatInt;
		Image.Format godotFormat = GetGodotFormat(format);
		
		byte[] imageData = pixels;
		int expectedSize = width * height * GetBytesPerPixel(format);
		
		if (pixels.Length > expectedSize)
		{
			imageData = new byte[expectedSize];
			Array.Copy(pixels, imageData, expectedSize);
		}
		
		Image img = Image.CreateFromData(width, height, false, godotFormat, imageData);
		
		if (_gameTexture == null || _gameTexture.GetWidth() != width || _gameTexture.GetHeight() != height)
		{
			_gameTexture = ImageTexture.CreateFromImage(img);
			_screenRect.Texture = _gameTexture;
		}
		else
		{
			_gameTexture.Update(img);
		}
	}

	private static bool IsFallbackInputPressed(LibretroInput input)
	{
		return input switch
		{
			LibretroInput.A => Input.IsActionPressed("ui_accept") || Input.IsKeyPressed(Key.X),
			LibretroInput.B => Input.IsActionPressed("ui_cancel") || Input.IsKeyPressed(Key.Z),
			LibretroInput.X => Input.IsKeyPressed(Key.S),
			LibretroInput.Y => Input.IsKeyPressed(Key.A),
			LibretroInput.START => Input.IsKeyPressed(Key.Enter),
			LibretroInput.SELECT => Input.IsKeyPressed(Key.Shift),
			LibretroInput.UP => Input.IsActionPressed("ui_up"),
			LibretroInput.DOWN => Input.IsActionPressed("ui_down"),
			LibretroInput.LEFT => Input.IsActionPressed("ui_left"),
			LibretroInput.RIGHT => Input.IsActionPressed("ui_right"),
			LibretroInput.L => Input.IsKeyPressed(Key.Q),
			LibretroInput.R => Input.IsKeyPressed(Key.W),
			LibretroInput.L2 => Input.IsKeyPressed(Key.E),
			LibretroInput.R2 => Input.IsKeyPressed(Key.R),
			_ => false,
		};
	}

	private static void RetroLogPrintfShim(int level, IntPtr format)
	{
		if (format == IntPtr.Zero)
			return;

		// retro_log_printf_t is variadic in C; this shim intentionally ignores extra arguments
		// and logs the format string itself so cores with mandatory log interface won't crash.
		string text = Marshal.PtrToStringAnsi(format) ?? string.Empty;
		if (string.IsNullOrWhiteSpace(text))
			return;

		FileLogger.Log($"[libretro-log:{level}] {text.TrimEnd()}");
	}
}
