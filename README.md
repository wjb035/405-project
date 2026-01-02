# PGEmu

Simple emulator launcher for a local ROM library.

## Overview
PGEmu provides a small core library (`PGEmu.app`) for config loading, library scanning, and launching, plus an Avalonia UI (`PGEmu.gui`) that lets you pick a platform and game and then launch it.

## Prerequisites
- .NET SDK 9 (or newer)

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

## Run the GUI
From the solution folder:
```bash
dotnet run --project PGEmu.gui/PGEmu.gui.csproj
```

Or:
```bash
cd PGEmu.gui
dotnet run
```

## Project layout
- `PGEmu.app`: core library (config models, scanner, launcher)
- `PGEmu.gui`: Avalonia UI (uses `PGEmu.app`)
- `config.json`: sample config for local paths
