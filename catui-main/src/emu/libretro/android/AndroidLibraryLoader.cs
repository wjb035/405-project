using System;
using System.Runtime.InteropServices;
using Godot;

public class AndroidLibraryLoader : ILibraryLoader
{
	private const int RTLD_NOW = 2;
	private const int RTLD_GLOBAL = 256;

	[DllImport("libdl.so", EntryPoint = "dlopen")]
	private static extern IntPtr dlopen(string filename, int flags);

	[DllImport("libdl.so", EntryPoint = "dlsym")]
	private static extern IntPtr dlsym(IntPtr handle, string symbol);

	[DllImport("libdl.so", EntryPoint = "dlclose")]
	private static extern int dlclose(IntPtr handle);

	[DllImport("libdl.so", EntryPoint = "dlerror")]
	private static extern IntPtr dlerror();

	public IntPtr LoadLibrary(string path)
	{
		FileLogger.Log($"[android] trying to load: {path}");
		FileLogger.Log($"[android] file exists: {System.IO.File.Exists(path)}");
		
		string actualPath = path;
		
		if (path.StartsWith("/storage/emulated") || path.StartsWith("/sdcard"))
		{
			FileLogger.Log($"[android] file in external storage, copying to app dir...");
			
			string appDir = ProjectSettings.GlobalizePath("user://cores/");
			if (!System.IO.Directory.Exists(appDir))
			{
				System.IO.Directory.CreateDirectory(appDir);
				FileLogger.Log($"[android] created cores dir: {appDir}");
			}
			
			string fileName = System.IO.Path.GetFileName(path);
			actualPath = System.IO.Path.Combine(appDir, fileName);
			
			try
			{
				System.IO.File.Copy(path, actualPath, true);
				FileLogger.Log($"[android] copied to: {actualPath}");
			}
			catch (System.Exception e)
			{
				FileLogger.Error($"[android] failed to copy file: {e.Message}");
				return IntPtr.Zero;
			}
		}
		
		int flags = 2;
		FileLogger.Log($"[android] using flags: {flags} (rtld_now)");
		FileLogger.Log($"[android] loading from: {actualPath}");
		
		IntPtr handle = dlopen(actualPath, flags);
		if (handle == IntPtr.Zero)
		{
			IntPtr errorPtr = dlerror();
			string error = errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) : "unknown error";
			FileLogger.Error($"[android] failed to load lib: {actualPath}, err: {error}");
			
			FileLogger.Log($"[android] trying rtld_lazy (1)...");
			handle = dlopen(actualPath, 1);
			if (handle == IntPtr.Zero)
			{
				errorPtr = dlerror();
				error = errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) : "unknown error";
				FileLogger.Error($"[android] rtld_lazy failed too: {error}");
			}
			else
			{
				FileLogger.Log($"[android] loaded with rtld_lazy! nice.");
			}
		}
		else
		{
			FileLogger.Log($"[android] lib loaded ok");
		}
		return handle;
	}

	public IntPtr GetProcAddress(IntPtr handle, string functionName)
	{
		IntPtr funcPtr = dlsym(handle, functionName);
		if (funcPtr == IntPtr.Zero)
		{
			IntPtr errorPtr = dlerror();
			string error = errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) : "Unknown error";
			FileLogger.Error($"[Android] Failed to get function '{functionName}': {error}");
		}
		return funcPtr;
	}

	public bool FreeLibrary(IntPtr handle)
	{
		int result = dlclose(handle);
		if (result != 0)
		{
			IntPtr errorPtr = dlerror();
			string error = errorPtr != IntPtr.Zero ? Marshal.PtrToStringAnsi(errorPtr) : "Unknown error";
			FileLogger.Error($"[Android] Failed to free library: {error}");
			return false;
		}
		return true;
	}

	public string GetLibraryExtension()
	{
		return ".so";
	}
}
