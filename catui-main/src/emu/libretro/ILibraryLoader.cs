using System;

public interface ILibraryLoader
{
	IntPtr LoadLibrary(string path);
	IntPtr GetProcAddress(IntPtr handle, string functionName);
	bool FreeLibrary(IntPtr handle);
	string GetLibraryExtension();
}
