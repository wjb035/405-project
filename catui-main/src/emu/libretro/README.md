# Libretro Multi-Platform Support

So, this is the magic that makes Libretro emulators work on everything (Windows, Android, Linux, macOS).

## File Structure

```
src/emu/libretro/
├── ILibraryLoader.cs              # Common loader interface
├── LibraryLoaderFactory.cs        # Picks the right loader for the OS
├── LibretroNative.cs              # The heavy lifting (C# <-> C interop)
├── LibretroPlayer.cs              # Godot node that runs the game
├── windows/
│   └── WindowsLibraryLoader.cs    # Windows stuff (kernel32.dll)
└── android/
    └── AndroidLibraryLoader.cs    # Android stuff (libdl.so)
```

## How it works

Basically, `LibraryLoaderFactory` checks what OS you're on and grabs the right tool to load the core (`.dll` or `.so`).

Then `LibretroNative` hooks into the core's C functions, and `LibretroPlayer` runs the show in Godot:
*   **Video**: Puts the pixel buffer onto a `TextureRect`.
*   **Audio**: Feeds sound into an `AudioStreamGenerator`.
*   **Input**: Maps Godot keys/buttons to the emulator.

## Platform Notes

*   **Windows**: Uses standard `.dll` files.
*   **Android**: Uses `.so` files. **Important**: make sure you use the right arch (`armeabi-v7a` or `arm64-v8a`) or it explodes.
*   **Linux/macOS**: Should work similar to Android but haven't tested much.

To play, just call `LibretroPlayer.LoadGame(romPath)` and enjoy :3
