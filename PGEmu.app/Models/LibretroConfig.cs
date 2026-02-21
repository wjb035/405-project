using System.Collections.Generic;
using System.IO;
using System;

namespace PGEmu.app;

public class LibretroConfig
{
    public string CorePath { get; set; } = string.Empty;
    public string LibraryName { get; set; } = "Libretro Core";
    public string LibraryVersion { get; set; } = "1.0";
    public string ValidExtensions { get; set; } = string.Empty;
    public bool NeedFullPath { get; set; } = true;
    public bool BlockExtract { get; set; } = true;
    public int TargetWidth { get; set; } = 640;
    public int TargetHeight { get; set; } = 480;
    public int MaxWidth { get; set; } = 1280;
    public int MaxHeight { get; set; } = 720;
    public float AspectRatio { get; set; } = 4f / 3f;
    public double TargetFps { get; set; } = 60.0;
    public double SampleRate { get; set; } = 48000.0;
    public Dictionary<string, string> Options { get; set; } = new();

    public string ResolveCorePath(string? baseDirectory)
    {
        var candidate = ExpandHomePath(CorePath);
        if (Path.IsPathRooted(candidate))
            return Path.GetFullPath(candidate);

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            return Path.GetFullPath(Path.Combine(baseDirectory!, candidate));
        }

        return Path.GetFullPath(candidate);
    }

    public LibretroConfig WithResolvedCorePath(string resolvedPath)
    {
        return new LibretroConfig
        {
            CorePath = resolvedPath,
            LibraryName = LibraryName,
            LibraryVersion = LibraryVersion,
            ValidExtensions = ValidExtensions,
            NeedFullPath = NeedFullPath,
            BlockExtract = BlockExtract,
            TargetWidth = TargetWidth,
            TargetHeight = TargetHeight,
            MaxWidth = MaxWidth,
            MaxHeight = MaxHeight,
            AspectRatio = AspectRatio,
            TargetFps = TargetFps,
            SampleRate = SampleRate,
            Options = new Dictionary<string, string>(Options, StringComparer.OrdinalIgnoreCase),
        };
    }

    private static string ExpandHomePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path.Substring(2));
        }

        return path;
    }
}
