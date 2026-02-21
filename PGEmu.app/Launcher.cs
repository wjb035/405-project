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

        var fullExe = ResolveExePath(cfg, emu.ExePath);
        if (fullExe == null)
        {
            throw new FileNotFoundException($"Emulator executable not found. exePath='{emu.ExePath}'.");
        }

        var args = emu.ArgsTemplate ?? string.Empty;
        args = args.Replace("{ROM}", game.Path);

        var psi = BuildProcessStartInfo(fullExe, args);

        Process.Start(psi)?.Dispose();
    }

    private static ProcessStartInfo BuildProcessStartInfo(string exePath, string args)
    {
        // macOS: if an .app bundle is provided, launch it via `open -a` so the OS handles it properly.
        // If the executable inside the bundle is provided (e.g. *.app/Contents/MacOS/<bin>), just run it directly.
        if (OperatingSystem.IsMacOS())
        {
            var appBundle = TryFindMacAppBundle(exePath);
            if (appBundle != null && Directory.Exists(appBundle) && (exePath.EndsWith(".app", StringComparison.OrdinalIgnoreCase)))
            {
                // `--args` passes arguments through to the app.
                // We quote only the app bundle path here; args are already templated (and may include quoting).
                return new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-a \"{appBundle}\" --args {args}",
                    UseShellExecute = false,
                };
            }

            return new ProcessStartInfo { FileName = exePath, Arguments = args, UseShellExecute = false };
        }

        // Linux: prefer direct execution.
        if (OperatingSystem.IsLinux())
        {
            return new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = false,
            };
        }

        // Windows: shell execute helps for .exe/.lnk/etc.
        return new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = args,
            UseShellExecute = true,
        };
    }

    private static string? ResolveExePath(AppConfig cfg, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return null;

        exePath = ExpandHomePath(exePath);
        exePath = exePath.Replace('/', Path.DirectorySeparatorChar);

        // Absolute path: use it directly if it exists.
        if (IsProbablyAbsolutePath(exePath))
        {
            if (File.Exists(exePath) || Directory.Exists(exePath))
                return exePath;
        }

        // Relative path: try a few sensible anchors.
        var candidates = new[]
        {
            // Next to the running app/binary.
            Path.Combine(AppContext.BaseDirectory, exePath),

            // Current working directory.
            Path.Combine(System.Environment.CurrentDirectory, exePath),

            // Relative to config.json location (best match for "portable config").
            cfg.SourcePath != null ? Path.Combine(Path.GetDirectoryName(cfg.SourcePath)!, exePath) : null,

            // Relative to configured LibraryRoot (useful when emulator lives inside the library folder).
            !string.IsNullOrWhiteSpace(cfg.LibraryRoot) ? Path.Combine(ExpandHomePath(cfg.LibraryRoot), exePath) : null,
        };

        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            if (File.Exists(c) || Directory.Exists(c)) return c;
        }

        return null;
    }

    private static bool IsProbablyAbsolutePath(string path)
    {
        if (Path.IsPathRooted(path)) return true;

        // Windows drive letter, e.g. C:\Games or C:/Games
        if (path.Length >= 3 &&
            char.IsLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/'))
        {
            return true;
        }

        // UNC path, e.g. \\server\share
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        return false;
    }

    private static string? TryFindMacAppBundle(string exePath)
    {
        // If you pass ".../Dolphin.app/Contents/MacOS/Dolphin", this returns ".../Dolphin.app".
        var idx = exePath.LastIndexOf(".app", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        return exePath.Substring(0, idx + 4);
    }

    private static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        if (path == "~")
            return System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            var rest = path.Substring(2);
            return Path.Combine(home, rest);
        }

        return path;
    }
}
