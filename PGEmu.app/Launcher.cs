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

        var configuredExePath = SelectPlatformPath(
            emu.ExePath,
            emu.ExePathWindows,
            emu.ExePathMac,
            emu.ExePathLinux);

        var fullExe = ResolvePath(cfg, configuredExePath, allowDirectory: true);
        if (fullExe == null)
        {
            throw new FileNotFoundException($"Emulator executable not found. exePath='{configuredExePath ?? "(not configured)"}'.");
        }

        var args = emu.ArgsTemplate ?? string.Empty;
        args = args.Replace("{ROM}", game.Path);
        if (args.Contains("{CORE}", StringComparison.Ordinal))
        {
            var configuredCorePath = SelectPlatformPath(
                emu.CorePath,
                emu.CorePathWindows,
                emu.CorePathMac,
                emu.CorePathLinux);

            var fullCorePath = ResolvePath(cfg, configuredCorePath, allowDirectory: false);
            if (fullCorePath == null)
            {
                throw new FileNotFoundException($"Libretro core not found. corePath='{configuredCorePath ?? "(not configured)"}'.");
            }

            args = args.Replace("{CORE}", fullCorePath);
        }

        var psi = BuildProcessStartInfo(fullExe, args);

        Process.Start(psi)?.Dispose();
    }
    
    
    // this is a secondary one for launching the emulator by itself 
    public static void LaunchEmulator(AppConfig cfg, string ExePath)
    {
        
        if (ExePath == "")
            throw new InvalidOperationException("No emulator configured for platform");
        
        var fullExe = ResolvePath(cfg, ExePath, allowDirectory: true);
        if (fullExe == null)
            throw new FileNotFoundException($"Emulator executable not found. exePath='{ExePath ?? "(not configured)"}'.");

        var args = "";
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
                var openArgs = string.IsNullOrWhiteSpace(args)
                    ? $"-a \"{appBundle}\""
                    : $"-a \"{appBundle}\" --args {args}";

                return new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = openArgs,
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

    private static string? ResolvePath(AppConfig cfg, string? configuredPath, bool allowDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;

        var exePath = configuredPath;
        exePath = ExpandHomePath(exePath);
        exePath = exePath.Replace('/', Path.DirectorySeparatorChar);

        // Absolute path: use it directly if it exists.
        if (IsProbablyAbsolutePath(exePath))
        {
            if (File.Exists(exePath) || (allowDirectory && Directory.Exists(exePath)))
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
            if (File.Exists(c) || (allowDirectory && Directory.Exists(c))) return c;
        }

        return null;
    }

    private static string? SelectPlatformPath(
        string? genericPath,
        string? windowsPath,
        string? macPath,
        string? linuxPath)
    {
        string? selected = null;

        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(windowsPath))
            selected = windowsPath;
        else if (OperatingSystem.IsMacOS() && !string.IsNullOrWhiteSpace(macPath))
            selected = macPath;
        else if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(linuxPath))
            selected = linuxPath;
        else
            selected = genericPath;

        return string.IsNullOrWhiteSpace(selected) ? null : selected;
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
