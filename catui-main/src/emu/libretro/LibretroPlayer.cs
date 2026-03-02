// libretro player implementation for CatUI.
// handles emulation logic, auto-saving, and state management.

using Godot;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;

public partial class LibretroPlayer : Node
{
	private const string SCREEN_PATH = "/root/main/Menus/game_screen/video/TextureRect";
	private const string AUDIO_PATH = "/root/main/Menus/game_screen/AudioStreamPlayer";

	private TextureRect _screenRect;
	private AudioStreamPlayer _gameAudio;

	private static LibretroPlayer _instance;

	private static LibretroNative.RetroEnvironmentDelegate _envCallback;
	private static LibretroNative.RetroVideoRefreshDelegate _videoCallback;
	private static LibretroNative.RetroInputPollDelegate _inputPollCallback;
	private static LibretroNative.RetroInputStateDelegate _inputStateCallback;
	private static LibretroNative.RetroAudioSampleDelegate _audioCallback;
	private static LibretroNative.RetroAudioSampleBatchDelegate _audioBatchCallback;

	private ImageTexture _gameTexture;
	private AudioStreamGeneratorPlayback _audioPlayback;

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

	private static byte[] _pixelBuffer;
	private static short[] _audioRawBuffer;
	private static Vector2[] _audioGodotBuffer;
	
	[StructLayout(LayoutKind.Sequential)]
	private struct retro_variable
	{
		public IntPtr key;
		public IntPtr value;
	}
	
	private static Dictionary<string, IntPtr> _variableValuePtrs = new Dictionary<string, IntPtr>();

	public override void _Ready()
	{
		_instance = this;
	}

	private void EnsureNodesReady()
	{
		if (_screenRect == null)
		{
			_screenRect = GetNodeOrNull<TextureRect>(SCREEN_PATH);
		}
		if (_gameAudio == null)
		{
			_gameAudio = GetNodeOrNull<AudioStreamPlayer>(AUDIO_PATH);
			if (_gameAudio != null && _audioPlayback == null)
			{
				SetupAudio();
			}
		}
	}

	public async void LoadGame(string path, string coreId = "")
	{
		// FileLogger.Log($"[LibretroPlayer] LoadGame called - Path: {path}, CoreId: {coreId}");
		// FileLogger.Log($"[LibretroPlayer] Platform: {OS.GetName()}");
		
		EnsureNodesReady();
		
		if (_screenRect == null || _gameAudio == null)
		{
			FileLogger.Error("[LibretroPlayer] ERROR: GameScreen nodes not found. Make sure the scene is loaded.");
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
		EnsureNodesReady();
		
		if (_screenRect == null || _gameAudio == null)
		{
			FileLogger.Error("[ERROR] GameScreen nodes not found. Make sure the scene is loaded.");
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
		
		if (_gameAudio != null)
		{
			_gameAudio.Stop();
		}
	}

	private void SetupAudio()
	{
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
		if (_gameAudio != null)
		{
			_gameAudio.Stop();
			_audioPlayback = null;
			SetupAudio();
		}
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
			_autoSaveTimer += delta;
			if (_autoSaveTimer >= AutoSaveInterval)
			{
				SaveSRAM();
				_autoSaveTimer = 0.0;
			}
			
			_timeAccumulator += delta;
			
			// optimization: limit frameskip to avoid spiraling out of control
			if (_timeAccumulator > TargetFrameTime * 3) {
				_timeAccumulator = TargetFrameTime;
			}
			
			while (_timeAccumulator >= TargetFrameTime)
			{
				LibretroNative.retro_run();
				_timeAccumulator -= TargetFrameTime;
			}
		}
	}

	private IntPtr _romDataPtr = IntPtr.Zero;

	private static void CacheValidActions()
	{
		if (_actionsCached) return;
		
		_validActions.Clear();
		var actions = Input.GetConnectedJoypads();
		
		foreach (var action in InputMap.GetActions())
		{
			_validActions.Add(action.ToString());
		}
		
		_actionsCached = true;
		FileLogger.Log($"[LibretroPlayer] Cached {_validActions.Count} input actions");
	}

	private void StartEmulator()
	{
		// FileLogger.Log("[LibretroPlayer] StartEmulator - BEGIN");
		try
		{
			// FileLogger.Log("[LibretroPlayer] Creating callbacks...");
			_envCallback = new LibretroNative.RetroEnvironmentDelegate(EnvironmentCallback);
			_videoCallback = new LibretroNative.RetroVideoRefreshDelegate(VideoCallback);
			_inputPollCallback = new LibretroNative.RetroInputPollDelegate(InputPollCallback);
			_inputStateCallback = new LibretroNative.RetroInputStateDelegate(InputStateCallback);
			_audioCallback = new LibretroNative.RetroAudioSampleDelegate(AudioSampleCallback);
			_audioBatchCallback = new LibretroNative.RetroAudioSampleBatchDelegate(AudioBatchCallback);

			CacheValidActions();

			// FileLogger.Log("[LibretroPlayer] Setting callbacks to core...");
			LibretroNative.retro_set_environment(_envCallback);
			LibretroNative.retro_set_video_refresh(_videoCallback);
			LibretroNative.retro_set_input_poll(_inputPollCallback);
			LibretroNative.retro_set_input_state(_inputStateCallback);
			LibretroNative.retro_set_audio_sample(_audioCallback);
			LibretroNative.retro_set_audio_sample_batch(_audioBatchCallback);

			// FileLogger.Log("[LibretroPlayer] Initializing core...");
			LibretroNative.retro_init();
			_coreInitialized = true;
			// FileLogger.Log("[LibretroPlayer] Core initialized!");

			// FileLogger.Log($"[libretroplayer] checking rom: {_currentRomPath}");
			if (!System.IO.File.Exists(_currentRomPath))
			{
				FileLogger.Error($"[libretroplayer] rom not found: {_currentRomPath}");
				return;
			}
			// FileLogger.Log("[LibretroPlayer] ROM file exists!");

			FileLogger.Log("[LibretroPlayer] Getting system info...");
			retro_system_info sysInfo = new retro_system_info();
			LibretroNative.retro_get_system_info(ref sysInfo);
			
			bool needFullPath = sysInfo.need_fullpath;
			
			retro_game_info gameInfo = new retro_game_info();
			gameInfo.path = _currentRomPath;
			gameInfo.meta = null;

			if (!needFullPath)
			{
				FileLogger.Log("[LibretroPlayer] Loading ROM data into memory...");
				byte[] romData = File.ReadAllBytes(_currentRomPath);
				
				if (_romDataPtr != IntPtr.Zero)
					Marshal.FreeHGlobal(_romDataPtr);
				
				_romDataPtr = Marshal.AllocHGlobal(romData.Length);
				Marshal.Copy(romData, 0, _romDataPtr, romData.Length);
				
				gameInfo.data = _romDataPtr;
				gameInfo.size = (uint)romData.Length;
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
	private static void AudioBatchCallback(IntPtr data, uint frames)
	{
		if (_instance == null || _instance._audioPlayback == null) return;
		if (_instance._audioPlayback.GetFramesAvailable() < frames) return;

		int samples = (int)frames * 2;
		
		if (_audioRawBuffer == null || _audioRawBuffer.Length < samples)
		{
			_audioRawBuffer = new short[samples * 2];
		}
		
		if (_audioGodotBuffer == null || _audioGodotBuffer.Length < frames)
		{
			_audioGodotBuffer = new Vector2[frames * 2];
		}

		Marshal.Copy(data, _audioRawBuffer, 0, samples);

		const float invShort = 0.00003051757f;
		for (int i = 0; i < frames; i++)
		{
			_audioGodotBuffer[i].X = _audioRawBuffer[i*2] * invShort;
			_audioGodotBuffer[i].Y = _audioRawBuffer[i*2+1] * invShort;
		}
		
		if (_audioGodotBuffer.Length == frames)
		{
			_instance._audioPlayback.PushBuffer(_audioGodotBuffer);
		}
		else
		{
			Vector2[] pushBuffer = new Vector2[frames];
			Array.Copy(_audioGodotBuffer, pushBuffer, frames);
			_instance._audioPlayback.PushBuffer(pushBuffer);
		}
	}

	private static void AudioSampleCallback(short left, short right)
	{
		if (_instance == null || _instance._audioPlayback == null) return;
		_instance._audioPlayback.PushFrame(new Vector2(left / 32768f, right / 32768f));
	}

	private static void InputPollCallback() { }

	private static short InputStateCallback(uint port, uint device, uint index, uint id)
	{
		if (_instance != null && _instance._ignoreInputTimer > 0) return 0;

		if (port != 0) return 0;
		if (device != 1) return 0;

		string coreId = !string.IsNullOrEmpty(LibretroNative.CurrentCoreId) 
			? LibretroNative.CurrentCoreId 
			: GetCoreFallbackId();
		
		string actionName = InputMapper.GetActionName(coreId, (LibretroInput)id);
		
		if (string.IsNullOrEmpty(actionName)) return 0;
		
		if (!_validActions.Contains(actionName)) return 0;
		
		if (Input.IsActionPressed(actionName))
			return 1;
			
		return 0;
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
			_ => "gba"
		};
	}

	private static IntPtr _saveDirectoryPtr = IntPtr.Zero;
	private static IntPtr _systemDirectoryPtr = IntPtr.Zero;

	private static bool EnvironmentCallback(uint cmd, IntPtr data)
	{
		switch (cmd)
		{
			case LibretroNative.RETRO_ENVIRONMENT_SET_PIXEL_FORMAT:
				if (data != IntPtr.Zero)
				{
					_instance._currentPixelFormat = (retro_pixel_format)Marshal.ReadInt32(data);
				}
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY:
				if (data != IntPtr.Zero)
				{
					string saveDir = ProjectSettings.GlobalizePath("user://saves/");
					if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);
					
					if (_saveDirectoryPtr != IntPtr.Zero) Marshal.FreeHGlobal(_saveDirectoryPtr);
					_saveDirectoryPtr = Marshal.StringToHGlobalAnsi(saveDir);
					Marshal.WriteIntPtr(data, _saveDirectoryPtr);
				}
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY:
				if (data != IntPtr.Zero)
				{
					string systemDir = ProjectSettings.GlobalizePath("user://system/");
					if (!Directory.Exists(systemDir)) Directory.CreateDirectory(systemDir);
					
					if (_systemDirectoryPtr != IntPtr.Zero) Marshal.FreeHGlobal(_systemDirectoryPtr);
					_systemDirectoryPtr = Marshal.StringToHGlobalAnsi(systemDir);
					Marshal.WriteIntPtr(data, _systemDirectoryPtr);
				}
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_CAN_DUPE:
				if (data != IntPtr.Zero) Marshal.WriteByte(data, 1);
				return true;

			case LibretroNative.RETRO_ENVIRONMENT_GET_LOG_INTERFACE:
				return false;

			case LibretroNative.RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME:
				return true;
			
			case LibretroNative.RETRO_ENVIRONMENT_SET_VARIABLES:
				return true;
			
			case LibretroNative.RETRO_ENVIRONMENT_GET_VARIABLE:
				if (data != IntPtr.Zero)
				{
					retro_variable variable = Marshal.PtrToStructure<retro_variable>(data);
					string key = Marshal.PtrToStringAnsi(variable.key);
					
					if (!string.IsNullOrEmpty(key))
					{
						string value = GetCoreVariableValue(key);
						if (!string.IsNullOrEmpty(value))
						{
							if (_variableValuePtrs.ContainsKey(key))
							{
								Marshal.FreeHGlobal(_variableValuePtrs[key]);
							}
							
							IntPtr valuePtr = Marshal.StringToHGlobalAnsi(value);
							_variableValuePtrs[key] = valuePtr;
							
							variable.value = valuePtr;
							Marshal.StructureToPtr(variable, data, false);
							return true;
						}
					}
				}
				return false;
			
			case LibretroNative.RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE:
				if (data != IntPtr.Zero)
				{
					Marshal.WriteByte(data, 0);
				}
				return true;

			default:
				return false;
		}
	}
	
	private static string GetCoreVariableValue(string key)
	{
		if (string.IsNullOrEmpty(LibretroNative.CurrentCoreId)) return null;
		
		var sceneTree = (SceneTree)Engine.GetMainLoop();
		var emulatorConfig = sceneTree.Root.GetNode("EmulatorConfig");
		
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

	// optimization: video processing with static buffers
	private static void VideoCallback(IntPtr data, uint width, uint height, uint pitch)
	{
		if (data == IntPtr.Zero || _instance == null) return;

		int bytesPerPixel = GetBytesPerPixel(_instance._currentPixelFormat);
		int lineSize = (int)(width * bytesPerPixel);
		int totalSize = lineSize * (int)height;
		
		if (_pixelBuffer == null || _pixelBuffer.Length < totalSize)
		{
			_pixelBuffer = new byte[totalSize];
		}
		
		for (int y = 0; y < height; y++)
		{
			IntPtr src = IntPtr.Add(data, y * (int)pitch);
			Marshal.Copy(src, _pixelBuffer, y * lineSize, lineSize);
		}
		
		// optimization: calling this directly instead of using CallDeferred for better performance
		// since retro_run is called from _Process, we are on the main thread.
		_instance.UpdateGameTexture(_pixelBuffer, (int)width, (int)height, (int)_instance._currentPixelFormat);
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

		uint size = LibretroNative.retro_get_memory_size(LibretroNative.RETRO_MEMORY_SAVE_RAM);
		if (size == 0) return;

		IntPtr ptr = LibretroNative.retro_get_memory_data(LibretroNative.RETRO_MEMORY_SAVE_RAM);
		if (ptr == IntPtr.Zero) return;

		byte[] data = new byte[size];
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
		uint size = LibretroNative.retro_serialize_size();
		if (size == 0) return;
		IntPtr ptr = Marshal.AllocHGlobal((int)size);
		try
		{
			if (LibretroNative.retro_serialize(ptr, size))
			{
				byte[] data = new byte[size];
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
		uint size = (uint)data.Length;
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
		uint size = LibretroNative.retro_get_memory_size(LibretroNative.RETRO_MEMORY_SAVE_RAM);
		if (size == 0) return;

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
}
