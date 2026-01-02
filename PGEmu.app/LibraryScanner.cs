using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PGEmu.app;

public static class LibraryScanner
{
    public static List<GameEntry> Scan(PlatformConfig platform, string libraryRoot)
    {
        var list = new List<GameEntry>();
        var dir = Path.Combine(libraryRoot, platform.RomPath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
        if (!Directory.Exists(dir)) return list;

        var exts = platform.Extensions.Select(e => e.StartsWith('.') ? e : "." + e).ToArray();
        var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Where(f => exts.Contains(Path.GetExtension(f).ToLower()));

        foreach (var f in files)
        {
            list.Add(new GameEntry { Name = Path.GetFileNameWithoutExtension(f), Path = f });
        }

        return list;
    }
}
