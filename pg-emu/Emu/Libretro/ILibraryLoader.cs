using System;

public interface ILibraryLoader
{
	IntPtr LoadLibrary(string path);
	IntPtr GetProcAddress(IntPtr handle, string functionName);
	bool FreeLibrary(IntPtr handle);
	string GetLibraryExtension();

	bool TryGetExport(IntPtr handle, string symbolName, out IntPtr symbol)
	{
		symbol = GetProcAddress(handle, symbolName);
		return symbol != IntPtr.Zero;
	}
}
