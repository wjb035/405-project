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

## Run the GUI (Godot)
- Install Godot 4.6 with .NET support.
- From the repo root, launch the project: `godot4 --path pg-emu`
- If you prefer the editor, open `pg-emu/project.godot` in Godot and press Play.

## Project layout
- `PGEmu.app`: core library (config models, scanner, launcher)
- `PGEmu.backend`: API/server bits
- `pg-emu`: Godot 4.6 C# front-end (uses `PGEmu.app`)
- `config.json`: sample config for local paths
