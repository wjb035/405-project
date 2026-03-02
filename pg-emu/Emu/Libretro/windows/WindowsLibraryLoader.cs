using System;
using System.Runtime.InteropServices;
using Godot;

public class WindowsLibraryLoader : ILibraryLoader
{
	private const uint LOAD_WITH_ALTERED_SEARCH_PATH = 0x00000008;

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW")]
	private static extern IntPtr LoadLibraryW(string dllToLoad);

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW")]
	private static extern IntPtr LoadLibraryExW(string fileName, IntPtr fileHandle, uint flags);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool FreeLibrary(IntPtr hModule);

	public IntPtr LoadLibrary(string path)
	{
		string fullPath = System.IO.Path.GetFullPath(path);
		FileLogger.Log($"[windows] trying to load: {fullPath}");
		FileLogger.Log($"[windows] file exists: {System.IO.File.Exists(fullPath)}");
		FileLogger.Log($"[windows] cur dir: {System.IO.Directory.GetCurrentDirectory()}");
		
		// Prefer altered search path so adjacent dependency DLLs can be found.
		IntPtr handle = LoadLibraryExW(fullPath, IntPtr.Zero, LOAD_WITH_ALTERED_SEARCH_PATH);
		if (handle == IntPtr.Zero)
		{
			handle = LoadLibraryW(fullPath);
		}

		if (handle == IntPtr.Zero)
		{
			int error = Marshal.GetLastWin32Error();
			FileLogger.Error($"[windows] failed to load lib: {fullPath}");
			FileLogger.Error($"[windows] error code: {error}");
			
			if (error == 126)
			{
				FileLogger.Error("[windows] error 126: missing dependencies? check vc++ redist");
				FileLogger.Error("[windows] visual c++ link: https://aka.ms/vs/17/release/vc_redist.x64.exe");
			}
		}
		else
		{
			FileLogger.Log($"[windows] lib loaded: {fullPath}");
		}
		return handle;
	}

	IntPtr ILibraryLoader.GetProcAddress(IntPtr handle, string functionName)
	{
		return GetProcAddress(handle, functionName);
	}

	bool ILibraryLoader.FreeLibrary(IntPtr handle)
	{
		return FreeLibrary(handle);
	}

	public string GetLibraryExtension()
	{
		return ".dll";
	}
}
