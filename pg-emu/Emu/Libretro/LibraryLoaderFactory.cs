<<<<<<< Updated upstream
using Godot;
using System;

public static class LibraryLoaderFactory
{
	private static ILibraryLoader _instance;

	public static ILibraryLoader GetLoader()
	{
		if (_instance != null)
			return _instance;

		string osName = OS.GetName();
		
		GD.Print($"[LibraryLoaderFactory] Detected OS: {osName}");

		switch (osName)
		{
			case "Windows":
				_instance = new WindowsLibraryLoader();
				GD.Print("[LibraryLoaderFactory] Using Windows library loader");
				break;

			// not implemented
			//case "Android":
				//_instance = new AndroidLibraryLoader();
				//GD.Print("[LibraryLoaderFactory] Using Android library loader");
				//break;

			case "Linux":
			case "FreeBSD":
			case "NetBSD":
			case "OpenBSD":
			case "macOS":
				_instance = new UnixLibraryLoader();
				GD.Print($"[LibraryLoaderFactory] Using Unix library loader for {osName} (dlopen)");
				break;

			default:
				throw new NotSupportedException($"Unsupported platform: {osName}");
		}

		return _instance;
	}
=======
namespace PGEmu.Emu.Libretro;

// Factory kept intentionally simple for now; structure mirrors CatUI so we can add per-platform loaders later.
public static class LibraryLoaderFactory
{
    public static ILibraryLoader Create()
    {
        return new NativeLibraryLoader();
    }
>>>>>>> Stashed changes
}
