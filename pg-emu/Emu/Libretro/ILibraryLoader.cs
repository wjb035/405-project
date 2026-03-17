using System;

<<<<<<< Updated upstream
public interface ILibraryLoader
{
	IntPtr LoadLibrary(string path);
	IntPtr GetProcAddress(IntPtr handle, string functionName);
	bool FreeLibrary(IntPtr handle);
	string GetLibraryExtension();
=======
namespace PGEmu.Emu.Libretro;

// CatUI-style native library abstraction so the runner logic stays platform-agnostic.
public interface ILibraryLoader
{
    IntPtr LoadLibrary(string path);
    bool TryGetExport(IntPtr handle, string symbolName, out IntPtr symbol);
    void FreeLibrary(IntPtr handle);
>>>>>>> Stashed changes
}
