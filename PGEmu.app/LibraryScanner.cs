using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;

namespace PGEmu.app;

public static class LibraryScanner
{
    public static string NormalizeLibraryRoot(string libraryRoot, IEnumerable<PlatformConfig>? platforms)
    {
        libraryRoot = ExpandHomePath(libraryRoot);
        if (string.IsNullOrWhiteSpace(libraryRoot) || platforms == null)
            return libraryRoot;

        var normalizedRoot = NormalizeDirectoryPath(libraryRoot);
        if (!Directory.Exists(normalizedRoot))
            return normalizedRoot;

        if (LooksLikeLibraryRoot(normalizedRoot, platforms))
            return normalizedRoot;

        foreach (var platform in platforms)
        {
            var romSegments = GetRelativePathSegments(platform.RomPath);
            if (romSegments.Count == 0)
                continue;

            var overlap = GetTrailingLeadingSegmentOverlap(normalizedRoot, romSegments);
            if (overlap <= 0)
                continue;

            var candidate = normalizedRoot;
            for (int index = 0; index < overlap; index++)
            {
                var parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrWhiteSpace(parent))
                    break;

                candidate = parent;
            }

            candidate = NormalizeDirectoryPath(candidate);
            if (Directory.Exists(candidate) && LooksLikeLibraryRoot(candidate, platforms))
                return candidate;
        }

        return normalizedRoot;
    }

    public static List<GameEntry> Scan(PlatformConfig platform, string libraryRoot)
    {
        return Scan(platform, libraryRoot, out _);
    }

    public static List<GameEntry> Scan(PlatformConfig platform, string libraryRoot, out string resolvedDir)
    {
        var list = new List<GameEntry>();
        resolvedDir = ResolvePlatformDirectory(platform, libraryRoot);

        if (!Directory.Exists(resolvedDir)) return list;

        var exts = platform.Extensions.Select(e => e.StartsWith('.') ? e : "." + e).ToArray();
        var files = Directory.EnumerateFiles(resolvedDir, "*", SearchOption.AllDirectories)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLower()))
            // Ignore macOS AppleDouble sidecar files (e.g. "._MyGame.rvz").
            .Where(f => !Path.GetFileName(f).StartsWith("._", StringComparison.Ordinal));

        foreach (var f in files)
        {
            list.Add(new GameEntry { Name = Path.GetFileNameWithoutExtension(f), Path = f });
        }

        return list;
    }

    private static string ResolvePlatformDirectory(PlatformConfig platform, string libraryRoot)
    {
        libraryRoot = NormalizeLibraryRoot(libraryRoot, new[] { platform });

        var romPath = platform.RomPath ?? string.Empty;
        romPath = ExpandHomePath(romPath);
        romPath = romPath.Replace('/', Path.DirectorySeparatorChar);

        // If romPath is empty, treat libraryRoot as the platform folder.
        // If romPath is absolute, use it as-is.
        // Otherwise, treat romPath as relative to libraryRoot.
        if (string.IsNullOrWhiteSpace(romPath))
            return libraryRoot;

        if (IsProbablyAbsolutePath(romPath))
            return romPath;

        return Path.Combine(libraryRoot, romPath.TrimStart(Path.DirectorySeparatorChar));
    }

    private static bool LooksLikeLibraryRoot(string candidateRoot, IEnumerable<PlatformConfig> platforms)
    {
        foreach (var platform in platforms)
        {
            var romPath = platform.RomPath ?? string.Empty;
            romPath = ExpandHomePath(romPath);
            romPath = romPath.Replace('/', Path.DirectorySeparatorChar);

            if (string.IsNullOrWhiteSpace(romPath) || IsProbablyAbsolutePath(romPath))
                continue;

            var scanDir = Path.Combine(candidateRoot, romPath.TrimStart(Path.DirectorySeparatorChar));
            if (Directory.Exists(scanDir))
                return true;
        }

        return false;
    }

    private static List<string> GetRelativePathSegments(string romPath)
    {
        if (string.IsNullOrWhiteSpace(romPath))
            return new List<string>();

        var normalized = ExpandHomePath(romPath).Replace('/', Path.DirectorySeparatorChar);
        if (IsProbablyAbsolutePath(normalized))
            return new List<string>();

        return normalized
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static int GetTrailingLeadingSegmentOverlap(string fullPath, IReadOnlyList<string> relativeSegments)
    {
        var pathSegments = NormalizeDirectoryPath(fullPath)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        var maxOverlap = Math.Min(pathSegments.Length, relativeSegments.Count);
        for (int overlap = maxOverlap; overlap >= 1; overlap--)
        {
            var matches = true;
            for (int index = 0; index < overlap; index++)
            {
                var pathSegment = pathSegments[pathSegments.Length - overlap + index];
                var relativeSegment = relativeSegments[index];
                if (!string.Equals(pathSegment, relativeSegment, StringComparison.OrdinalIgnoreCase))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return overlap;
        }

        return 0;
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (fullPath.Length > 1)
            fullPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath;
    }

    private static bool IsProbablyAbsolutePath(string path)
    {
        // Path.IsPathRooted is OS-specific. This also recognizes Windows-style drive paths on non-Windows hosts.
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

    private static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var rest = path.Substring(2);
            return Path.Combine(home, rest);
        }

        return path;
    }
}
