using System;
using System.Runtime.InteropServices;

namespace PGEmu.Emu.Libretro;

// Default .NET native loader. Works on macOS/Linux/Windows with the platform's dynamic linker.
public sealed class NativeLibraryLoader : ILibraryLoader
{
    public IntPtr LoadLibrary(string path)
    {
        return NativeLibrary.Load(path);
    }

    public bool TryGetExport(IntPtr handle, string symbolName, out IntPtr symbol)
    {
        return NativeLibrary.TryGetExport(handle, symbolName, out symbol);
    }

    public void FreeLibrary(IntPtr handle)
    {
        NativeLibrary.Free(handle);
    }
}
