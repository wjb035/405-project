using System;
using System.Runtime.InteropServices;
using Godot;

public class WindowsLibraryLoader : ILibraryLoader
{
	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
	private static extern IntPtr LoadLibraryA(string dllToLoad);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool FreeLibrary(IntPtr hModule);

	public IntPtr LoadLibrary(string path)
	{
		FileLogger.Log($"[windows] trying to load: {path}");
		FileLogger.Log($"[windows] file exists: {System.IO.File.Exists(path)}");
		FileLogger.Log($"[windows] cur dir: {System.IO.Directory.GetCurrentDirectory()}");
		
		IntPtr handle = LoadLibraryA(path);
		if (handle == IntPtr.Zero)
		{
			int error = Marshal.GetLastWin32Error();
			FileLogger.Error($"[windows] failed to load lib: {path}");
			FileLogger.Error($"[windows] error code: {error}");
			
			if (error == 126)
			{
				FileLogger.Error("[windows] error 126: missing dependencies? check vc++ redist");
				FileLogger.Error("[windows] visual c++ link: https://aka.ms/vs/17/release/vc_redist.x64.exe");
			}
		}
		else
		{
			FileLogger.Log($"[windows] lib loaded: {path}");
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
