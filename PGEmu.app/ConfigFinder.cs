using System;
using System.IO;

namespace PGEmu.app;

public static class ConfigFinder
{
    public static string? FindConfigPath()
    {
        // Check current directory
        var cwd = Environment.CurrentDirectory;
        var path = Path.Combine(cwd, "config.json");
        if (File.Exists(path)) return path;

        // Check executable directory
        var exeDir = AppContext.BaseDirectory;
        path = Path.Combine(exeDir, "config.json");
        if (File.Exists(path)) return path;

        // Walk up a few levels
        var dir = new DirectoryInfo(cwd);
        for (int i = 0; i < 4 && dir != null; i++)
        {
            path = Path.Combine(dir.FullName, "config.json");
            if (File.Exists(path)) return path;
            dir = dir.Parent!;
        }

        return null;
    }
}
