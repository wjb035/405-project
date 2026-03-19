using System;
using System.Runtime.InteropServices;
using Godot;

public class UnixLibraryLoader : ILibraryLoader {
	private const int RTLD_NOW = 2;

	private const string LibName = "libc";

	[DllImport(LibName, SetLastError = true, EntryPoint = "dlopen")]
	private static extern IntPtr dlopen(string fileName, int flags);

	[DllImport(LibName, SetLastError = true, EntryPoint = "dlsym")]
	private static extern IntPtr dlsym(IntPtr handle, string symbol);

	[DllImport(LibName, SetLastError = true, EntryPoint = "dlclose")]
	private static extern int dlclose(IntPtr handle);

	[DllImport(LibName, SetLastError = true, EntryPoint = "dlerror")]
	private static extern IntPtr dlerror();

	public IntPtr LoadLibrary(string path) {
		FileLogger.Log($"[unix] trying to load: {path}");
		FileLogger.Log($"[unix] file exists: {System.IO.File.Exists(path)}");
		FileLogger.Log($"[unix] cur dir: {System.IO.Directory.GetCurrentDirectory()}");

		IntPtr handle = dlopen(path, RTLD_NOW);

		if (handle == IntPtr.Zero) {
			string err = GetLastErrorString();
			FileLogger.Error($"[unix] failed to load lib: {path}");
			FileLogger.Error($"[unix] dlopen error: {err}");
		}

		else {
			FileLogger.Log($"[unix] lib loaded: {path}");
		}

		return handle;
	}

	IntPtr ILibraryLoader.GetProcAddress(IntPtr handle, string functionName) {
		IntPtr symbol = dlsym(handle, functionName);
		if (symbol == IntPtr.Zero)
		{
			string err = GetLastErrorString();
			FileLogger.Error($"[unix] failed to get symbol '{functionName}': {err}");
		}
		return symbol;
	}

	bool ILibraryLoader.FreeLibrary(IntPtr handle) {
		int result = dlclose(handle);
		if (result != 0)
		{
			string err = GetLastErrorString();
			FileLogger.Error($"[unix] dlclose failed: {err}");
			return false;
		}
		return true;
	}

	public string GetLibraryExtension() {
		string osName = OS.GetName();
		switch (osName)
		{
			case "macOS":
				return ".dylib";
			case "Linux":
			case "FreeBSD":
			case "NetBSD":
			case "OpenBSD":
				return ".so";
			default:
				return ".so";
		}
	}

	private static string GetLastErrorString() {
		IntPtr errPtr = dlerror();
		if (errPtr == IntPtr.Zero)
			return "Unknown error";
		return Marshal.PtrToStringAnsi(errPtr);
	}
}
