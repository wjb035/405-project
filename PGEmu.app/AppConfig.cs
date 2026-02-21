using System.Text.Json;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Linq;

namespace PGEmu.app;

public class AppConfig
{
    [JsonIgnore]
    public string? SourcePath { get; private set; }

    public string LibraryRoot { get; set; } = string.Empty;
    public List<PlatformConfig> Platforms { get; set; } = new();
    public List<EmulatorConfig> Emulators { get; set; } = new();
    public static AppConfig Load(string path)
    {
        var txt = File.ReadAllText(path);
        var opt = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var cfg = JsonSerializer.Deserialize<AppConfig>(txt, opt)!;
        cfg.SourcePath = path;

        // If a local override exists next to the base config, merge it in.
        var localPath = Path.Combine(Path.GetDirectoryName(path)!, "config.local.json");
        if (File.Exists(localPath))
        {
            var localTxt = File.ReadAllText(localPath);
            var localCfg = JsonSerializer.Deserialize<AppConfig>(localTxt, opt);
            if (localCfg != null)
            {
                cfg.ApplyOverrides(localCfg);
            }
        }

        return cfg;
    }

    public void Save(string path)
    {
        var opt = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        var txt = JsonSerializer.Serialize(this, opt);
        File.WriteAllText(path, txt);
    }

    private void ApplyOverrides(AppConfig local)
    {
        if (!string.IsNullOrWhiteSpace(local.LibraryRoot))
            LibraryRoot = local.LibraryRoot;

        if (local.Platforms != null)
        {
            foreach (var p in local.Platforms)
            {
                if (string.IsNullOrWhiteSpace(p.Id)) continue;
                var existing = Platforms.FirstOrDefault(x => x.Id == p.Id);
                if (existing == null)
                {
                    Platforms.Add(p);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(p.Name)) existing.Name = p.Name;
                if (!string.IsNullOrWhiteSpace(p.RomPath)) existing.RomPath = p.RomPath;
                if (p.Extensions != null && p.Extensions.Count > 0) existing.Extensions = p.Extensions;
                if (!string.IsNullOrWhiteSpace(p.DefaultEmulatorId)) existing.DefaultEmulatorId = p.DefaultEmulatorId;
                if (p.retroachievementsPlatformID != 0) existing.retroachievementsPlatformID = p.retroachievementsPlatformID;
            }
        }

        if (local.Emulators != null)
        {
            foreach (var e in local.Emulators)
            {
                if (string.IsNullOrWhiteSpace(e.Id)) continue;
                var existing = Emulators.FirstOrDefault(x => x.Id == e.Id);
                if (existing == null)
                {
                    Emulators.Add(e);
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(e.Name)) existing.Name = e.Name;
                if (!string.IsNullOrWhiteSpace(e.ExePath)) existing.ExePath = e.ExePath;
                if (!string.IsNullOrWhiteSpace(e.ArgsTemplate)) existing.ArgsTemplate = e.ArgsTemplate;
            }
        }
    }
}

public class EmulatorConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string? ArgsTemplate { get; set; }
}
