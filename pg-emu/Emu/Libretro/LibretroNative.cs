// libretro native interop layer.
// maps the C API to C# delegates and structs.
// to add more cores, just update the mappings below.

using Godot;
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Collections.Generic;

[StructLayout(LayoutKind.Sequential)]
public struct retro_game_info
{
	// libretro expects C strings (char*), not managed C# strings.
	public IntPtr path;
	public IntPtr data;
	// libretro uses size_t here, which is pointer-sized on each platform.
	public nuint size;
	public IntPtr meta;
}

[StructLayout(LayoutKind.Sequential)]
public struct retro_system_info
{
	public IntPtr library_name;
	public IntPtr library_version;
	public IntPtr valid_extensions;
	// libretro booleans are 1 byte; default .NET bool marshaling can differ by platform/ABI.
	[MarshalAs(UnmanagedType.I1)]
	public bool need_fullpath;
	[MarshalAs(UnmanagedType.I1)]
	public bool block_extract;
}

[StructLayout(LayoutKind.Sequential)]
public struct retro_game_geometry
{
	public uint base_width;
	public uint base_height;
	public uint max_width;
	public uint max_height;
	public float aspect_ratio;
}

[StructLayout(LayoutKind.Sequential)]
public struct retro_system_timing
{
	public double fps;
	public double sample_rate;
}

[StructLayout(LayoutKind.Sequential)]
public struct retro_system_av_info
{
	public retro_game_geometry geometry;
	public retro_system_timing timing;
}

public enum retro_pixel_format
{
	RETRO_PIXEL_FORMAT_0RGB1555 = 0,
	RETRO_PIXEL_FORMAT_XRGB8888 = 1,
	RETRO_PIXEL_FORMAT_RGB565 = 2
}

public enum LibretroInput : uint
{
	B = 0, Y = 1, SELECT = 2, START = 3,
	UP = 4, DOWN = 5, LEFT = 6, RIGHT = 7,
	A = 8, X = 9, L = 10, R = 11,
	L2 = 12, R2 = 13, L3 = 14, R3 = 15
}

public enum EmulatorCore
{
	GBA_MGBA,
	GBA_VBAM,
	SNES_SNES9X,
	NES_FCEUMM,
	GB_GAMBATTE,
	GBC_GAMBATTE,
	PSP_PPSSPP
}

public partial class LibretroNative : Node
{
	private static Dictionary<EmulatorCore, string> CorePaths = new Dictionary<EmulatorCore, string>();

	private static Dictionary<string, EmulatorCore> ExtensionMapping = new Dictionary<string, EmulatorCore>
	{
		{ ".gba", EmulatorCore.GBA_MGBA },
		{ ".sfc", EmulatorCore.SNES_SNES9X },
		{ ".smc", EmulatorCore.SNES_SNES9X },
		{ ".nes", EmulatorCore.NES_FCEUMM },
		{ ".gb", EmulatorCore.GB_GAMBATTE },
		{ ".gbc", EmulatorCore.GBC_GAMBATTE },
		{ ".iso", EmulatorCore.PSP_PPSSPP },
		{ ".cso", EmulatorCore.PSP_PPSSPP },
		{ ".pbp", EmulatorCore.PSP_PPSSPP }
	};

	public static EmulatorCore CurrentCore { get; private set; } = EmulatorCore.GBA_MGBA;
	public static string CurrentCorePath { get; set; }
	public static string CurrentCoreId { get; set; } = "";

	private static ILibraryLoader _libraryLoader;
	
	static LibretroNative()
	{
		_libraryLoader = LibraryLoaderFactory.GetLoader();
	}

	public const uint RETRO_ENVIRONMENT_SET_PIXEL_FORMAT = 10;
	public const uint RETRO_ENVIRONMENT_SET_MESSAGE = 6;
	public const uint RETRO_ENVIRONMENT_SET_INPUT_DESCRIPTORS = 11;
	public const uint RETRO_ENVIRONMENT_GET_SYSTEM_DIRECTORY = 9;
	public const uint RETRO_ENVIRONMENT_GET_SAVE_DIRECTORY = 31;
	public const uint RETRO_ENVIRONMENT_SET_SYSTEM_AV_INFO = 32;
	public const uint RETRO_ENVIRONMENT_SET_SUPPORT_NO_GAME = 18;
	public const uint RETRO_ENVIRONMENT_SET_PERFORMANCE_LEVEL = 23;
	public const uint RETRO_ENVIRONMENT_GET_LOG_INTERFACE = 27;
	public const uint RETRO_ENVIRONMENT_GET_CAN_DUPE = 3;
	public const uint RETRO_ENVIRONMENT_SET_VARIABLES = 16;
	public const uint RETRO_ENVIRONMENT_GET_VARIABLE = 15;
	public const uint RETRO_ENVIRONMENT_GET_VARIABLE_UPDATE = 17;
	// Needed by some cores (including Dolphin) to locate runtime assets.
	public const uint RETRO_ENVIRONMENT_GET_CORE_ASSETS_DIRECTORY = 30;
	public const uint RETRO_ENVIRONMENT_SET_CONTROLLER_INFO = 35;
	public const uint RETRO_ENVIRONMENT_SET_GEOMETRY = 37;
	public const uint RETRO_ENVIRONMENT_GET_USERNAME = 38;
	public const uint RETRO_ENVIRONMENT_GET_LANGUAGE = 39;
	public const uint RETRO_ENVIRONMENT_SET_HW_RENDER = 14;
	public const uint RETRO_ENVIRONMENT_GET_PREFERRED_HW_RENDER = 56;
	public const uint RETRO_ENVIRONMENT_GET_INPUT_BITMASKS_LEGACY = 51;
	public const uint RETRO_ENVIRONMENT_SET_CORE_OPTIONS_DISPLAY = 55;
	public const uint RETRO_ENVIRONMENT_GET_CORE_OPTIONS_VERSION = 52;
	public const uint RETRO_ENVIRONMENT_GET_DISK_CONTROL_INTERFACE_VERSION = 57;
	public const uint RETRO_ENVIRONMENT_GET_MESSAGE_INTERFACE_VERSION_LEGACY = 58;
	public const uint RETRO_ENVIRONMENT_GET_MESSAGE_INTERFACE_VERSION = 59;
	public const uint RETRO_ENVIRONMENT_SET_MESSAGE_EXT = 60;
	public const uint RETRO_ENVIRONMENT_SET_CORE_OPTIONS_UPDATE_DISPLAY_CALLBACK_LEGACY = 62;
	public const uint RETRO_ENVIRONMENT_EXPERIMENTAL = 0x10000;
	public const uint RETRO_ENVIRONMENT_GET_INPUT_BITMASKS = 51 | RETRO_ENVIRONMENT_EXPERIMENTAL;
	public const uint RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2 = 67;
	public const uint RETRO_ENVIRONMENT_SET_CORE_OPTIONS_V2_INTL = 68;
	public const uint RETRO_ENVIRONMENT_SET_CORE_OPTIONS_UPDATE_DISPLAY_CALLBACK = 69;
	public const uint RETRO_ENVIRONMENT_GET_VFS_INTERFACE = 45 | RETRO_ENVIRONMENT_EXPERIMENTAL;
	public const uint RETRO_ENVIRONMENT_GET_HW_RENDER_INTERFACE = 41 | RETRO_ENVIRONMENT_EXPERIMENTAL;
	public const uint RETRO_ENVIRONMENT_SET_HW_RENDER_CONTEXT_NEGOTIATION_INTERFACE = 43 | RETRO_ENVIRONMENT_EXPERIMENTAL;
	public const uint RETRO_ENVIRONMENT_SET_HW_SHARED_CONTEXT = 44 | RETRO_ENVIRONMENT_EXPERIMENTAL;

	public static bool SetCoreFromRomPath(string romPath)
	{
		var sceneTree = (SceneTree)Engine.GetMainLoop();
		var emulatorConfig = sceneTree.Root.GetNodeOrNull<Node>("EmulatorConfig");
		
		if (emulatorConfig == null)
		{
			FileLogger.Error("EmulatorConfig not found!");
			return false;
		}

		// Try to resolve using CurrentCoreId first if available
		if (!string.IsNullOrEmpty(CurrentCoreId))
		{
			string pathFromId = (string)emulatorConfig.Call("get_libretro_core", CurrentCoreId);
			if (!string.IsNullOrEmpty(pathFromId) && File.Exists(pathFromId))
			{
				CurrentCorePath = pathFromId;
				FileLogger.Log($"found core for id '{CurrentCoreId}': {CurrentCorePath}");
				return true;
			}
		}
		
		string corePath = (string)emulatorConfig.Call("get_core_path_for_rom", romPath);
		
		if (!string.IsNullOrEmpty(corePath) && File.Exists(corePath))
		{
			CurrentCorePath = corePath;
			FileLogger.Log($"found core for rom: {corePath}");
			return true;
		}
		
		string ext = Path.GetExtension(romPath).ToLower();
		
		if (ExtensionMapping.ContainsKey(ext))
		{
			CurrentCore = ExtensionMapping[ext];
			// hardcoded fallback removed
			
			FileLogger.Log($"selected fallback core: {CurrentCore} for ext {ext}");
			// return false because we don't have a path
			return false;
		}
		
		FileLogger.Error($"unsupported rom extension: {ext}");
		FileLogger.Error("please import core in settings -> emulation");
		return false;
	}

	private static IntPtr _coreHandle = IntPtr.Zero;

	private static T GetDelegateForFunction<T>(string functionName) where T : Delegate
	{
		IntPtr funcPtr = _libraryLoader.GetProcAddress(_coreHandle, functionName);
		if (funcPtr == IntPtr.Zero)
		{
			FileLogger.Error($"failed to get function: {functionName}");
			return null;
		}
		return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
	}

	public static bool LoadCore()
	{
		// FileLogger.Log("[LibretroNative] LoadCore - BEGIN");
		// FileLogger.Log($"[LibretroNative] Core path: {CurrentCorePath}");
		
		if (_coreHandle != IntPtr.Zero)
		{
			// FileLogger.Log("[LibretroNative] Freeing previous core...");
			_libraryLoader.FreeLibrary(_coreHandle);
		}

		// FileLogger.Log("[LibretroNative] Loading library...");
		_coreHandle = _libraryLoader.LoadLibrary(CurrentCorePath);
		if (_coreHandle == IntPtr.Zero)
		{
			FileLogger.Error($"[libretronative] failed to load core: {CurrentCorePath}");
			return false;
		}
		// FileLogger.Log("[libretronative] lib loaded ok");

		// FileLogger.Log("[LibretroNative] Getting function pointers...");
		retro_init = GetDelegateForFunction<RetroInitDelegate>("retro_init");
		if (retro_init == null) { FileLogger.Error("[LibretroNative] failed to get retro_init"); return false; }
		
		retro_deinit = GetDelegateForFunction<RetroDeinitDelegate>("retro_deinit");
		retro_api_version = GetDelegateForFunction<RetroApiVersionDelegate>("retro_api_version");
		retro_get_system_info = GetDelegateForFunction<RetroGetSystemInfoDelegate>("retro_get_system_info");
		retro_get_system_av_info = GetDelegateForFunction<RetroGetSystemAvInfoDelegate>("retro_get_system_av_info");
		retro_load_game = GetDelegateForFunction<RetroLoadGameDelegate>("retro_load_game");
		retro_run = GetDelegateForFunction<RetroRunDelegate>("retro_run");
		retro_set_video_refresh = GetDelegateForFunction<RetroSetVideoRefreshDelegate>("retro_set_video_refresh");
		retro_set_audio_sample = GetDelegateForFunction<RetroSetAudioSampleDelegate>("retro_set_audio_sample");
		retro_set_audio_sample_batch = GetDelegateForFunction<RetroSetAudioSampleBatchDelegate>("retro_set_audio_sample_batch");
		retro_set_input_poll = GetDelegateForFunction<RetroSetInputPollDelegate>("retro_set_input_poll");
		retro_set_input_state = GetDelegateForFunction<RetroSetInputStateDelegate>("retro_set_input_state");
		retro_set_environment = GetDelegateForFunction<RetroSetEnvironmentDelegate>("retro_set_environment");
		retro_get_memory_data = GetDelegateForFunction<RetroGetMemoryDataDelegate>("retro_get_memory_data");
		retro_get_memory_size = GetDelegateForFunction<RetroGetMemorySizeDelegate>("retro_get_memory_size");
		retro_serialize_size = GetDelegateForFunction<RetroSerializeSizeDelegate>("retro_serialize_size");
		retro_serialize = GetDelegateForFunction<RetroSerializeDelegate>("retro_serialize");
		retro_unserialize = GetDelegateForFunction<RetroUnserializeDelegate>("retro_unserialize");

		FileLogger.Log("[libretronative] all function pointers ok!");
		FileLogger.Log("[libretronative] loadcore - end");
		return retro_init != null;
	}

	public static void UnloadCore()
	{
		if (_coreHandle != IntPtr.Zero)
		{
			_libraryLoader.FreeLibrary(_coreHandle);
			_coreHandle = IntPtr.Zero;
		}
	}

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroInitDelegate();
	public static RetroInitDelegate retro_init;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroDeinitDelegate();
	public static RetroDeinitDelegate retro_deinit;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate uint RetroApiVersionDelegate();
	public static RetroApiVersionDelegate retro_api_version;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroGetSystemInfoDelegate(ref retro_system_info info);
	public static RetroGetSystemInfoDelegate retro_get_system_info;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroGetSystemAvInfoDelegate(ref retro_system_av_info info);
	public static RetroGetSystemAvInfoDelegate retro_get_system_av_info;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	// libretro returns bool as a C bool; force 1-byte marshalling.
	[return: MarshalAs(UnmanagedType.I1)]
	public delegate bool RetroLoadGameDelegate(ref retro_game_info game);
	public static RetroLoadGameDelegate retro_load_game;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroRunDelegate();
	public static RetroRunDelegate retro_run;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroVideoRefreshDelegate(IntPtr data, uint width, uint height, uint pitch);
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroAudioSampleDelegate(short left, short right);
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	// Callback returns number of frames consumed (size_t).
	public delegate nuint RetroAudioSampleBatchDelegate(IntPtr data, nuint frames);
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroInputPollDelegate();
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate short RetroInputStateDelegate(uint port, uint device, uint index, uint id);
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	// Environment callback returns C bool.
	[return: MarshalAs(UnmanagedType.I1)]
	public delegate bool RetroEnvironmentDelegate(uint cmd, IntPtr data);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroSetVideoRefreshDelegate(RetroVideoRefreshDelegate cb);
	public static RetroSetVideoRefreshDelegate retro_set_video_refresh;
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroSetAudioSampleDelegate(RetroAudioSampleDelegate cb);
	public static RetroSetAudioSampleDelegate retro_set_audio_sample;
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroSetAudioSampleBatchDelegate(RetroAudioSampleBatchDelegate cb);
	public static RetroSetAudioSampleBatchDelegate retro_set_audio_sample_batch;
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroSetInputPollDelegate(RetroInputPollDelegate cb);
	public static RetroSetInputPollDelegate retro_set_input_poll;
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroSetInputStateDelegate(RetroInputStateDelegate cb);
	public static RetroSetInputStateDelegate retro_set_input_state;
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate void RetroSetEnvironmentDelegate(RetroEnvironmentDelegate cb);
	public static RetroSetEnvironmentDelegate retro_set_environment;

	public const uint RETRO_MEMORY_SAVE_RAM = 0;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate IntPtr RetroGetMemoryDataDelegate(uint id);
	public static RetroGetMemoryDataDelegate retro_get_memory_data;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate nuint RetroGetMemorySizeDelegate(uint id);
	public static RetroGetMemorySizeDelegate retro_get_memory_size;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	public delegate nuint RetroSerializeSizeDelegate();
	public static RetroSerializeSizeDelegate retro_serialize_size;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	// Serialization APIs also use C bool returns.
	[return: MarshalAs(UnmanagedType.I1)]
	public delegate bool RetroSerializeDelegate(IntPtr data, nuint size);
	public static RetroSerializeDelegate retro_serialize;

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	[return: MarshalAs(UnmanagedType.I1)]
	public delegate bool RetroUnserializeDelegate(IntPtr data, nuint size);
	public static RetroUnserializeDelegate retro_unserialize;
}
