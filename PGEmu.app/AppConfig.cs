using System.Text.Json;
using System.Collections.Generic;
using System.IO;

namespace PGEmu.app;

public class AppConfig
{
    public string LibraryRoot { get; set; } = string.Empty;
    public List<PlatformConfig> Platforms { get; set; } = new();
    public List<EmulatorConfig> Emulators { get; set; } = new();
    public static AppConfig Load(string path)
    {
        var txt = File.ReadAllText(path);
        var opt = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return JsonSerializer.Deserialize<AppConfig>(txt, opt)!;
    }
}

public class EmulatorConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ExePath { get; set; } = string.Empty;
    public string? ArgsTemplate { get; set; }
}
