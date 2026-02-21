# PGEmu

Simple emulator launcher for a local ROM library.

## Overview
PGEmu provides a small core library (`PGEmu.app`) for config loading, library scanning, and launching, plus a Godot 4.6 C# UI found in `pg-emu` that lets you pick a platform and game and launch it.

## Prerequisites
- .NET SDK 9 (or newer)
- MYSQL 8 (or newer - temp)

## Configuration
PGEmu reads `config.json` from the current working directory or next to the built app.

Example:
```json
{
  "libraryRoot": "/Volumes/HARD DRIVE/PGEmu",
  "platforms": [
    {
      "id": "gc",
      "name": "Nintendo GameCube",
      "romPath": "games/GameCube",
      "extensions": [".iso", ".gcm", ".gcz", ".ciso", ".rvz", ".wia", ".wbfs"],
      "defaultEmulatorId": "dolphin"
    }
  ],
  "emulators": [
    {
      "id": "dolphin",
      "name": "Dolphin",
      "exePath": "Dolphin.app/Contents/MacOS/Dolphin",
      "argsTemplate": "--exec \"{ROM}\" --batch"
    }
  ]
}
```

Notes:
- `libraryRoot` can be an absolute path or `~/...`.
- `romPath` is relative to `libraryRoot` (or absolute).
- `exePath` can be absolute or relative to `libraryRoot`.
- `platforms` can provide a `libretro` block to launch an in-process Libretro core.

### In-process Libretro loader

- Add a `libretro` object to any platform you want to host directly.
- The block accepts the same fields defined by `LibretroConfig` (core path, geometry limits, target FPS/sample rate, `options`, etc.).
- `corePath` is resolved relative to `config.json`; place your native DLL/SO/DYLIB (for example, `cores/dolphin_libretro.dylib`) next to the configuration or provide an absolute path.
- When Libretro data is configured, the UI routes the selection to `LibretroPlayer.tscn`, which instantiates `LibretroHost`, streams the framebuffer through `LibretroPlayer.cs`, polls Godot input, and presents a built-in **Stop** control.
- Since Libretro cores are native, you must distribute the binary yourself and ensure its dependencies are satisfied on the target machine.

## Run the GUI (Godot)
- Install Godot 4.6 with .NET support.
- From the repo root, launch the project: `godot4 --path pg-emu`
- If you prefer the editor, open `pg-emu/project.godot` in Godot and press Play.

## Project layout
- `PGEmu.app`: core library (config models, scanner, launcher)
- `PGEmu.backend`: API/server bits
- `pg-emu`: Godot 4.6 C# front-end (uses `PGEmu.app`)
- `config.json`: sample config for local paths
