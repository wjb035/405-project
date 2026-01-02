using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PGEmu.app;

public static class Launcher
{
    public static void LaunchFromConfig(AppConfig cfg, PlatformConfig platform, GameEntry game)
    {
        var emulatorId = platform.DefaultEmulatorId ?? cfg.Emulators.FirstOrDefault()?.Id;
        var emu = cfg.Emulators.FirstOrDefault(e => e.Id == emulatorId);
        if (emu == null) throw new InvalidOperationException("No emulator configured for platform");

        var exePath = emu.ExePath.Replace('/', Path.DirectorySeparatorChar);
        var fullExe = Path.Combine(AppContext.BaseDirectory, exePath);
        if (!File.Exists(fullExe)) fullExe = exePath; // try as-is

        var args = emu.ArgsTemplate ?? string.Empty;
        args = args.Replace("{ROM}", game.Path);

        var psi = new ProcessStartInfo
        {
            FileName = fullExe,
            Arguments = args,
            UseShellExecute = true,
        };

        Process.Start(psi)?.Dispose();
    }
}
