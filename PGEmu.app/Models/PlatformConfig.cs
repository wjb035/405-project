using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace PGEmu.app;

public class PlatformConfig
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string RomPath { get; set; } = string.Empty;
    public List<string> Extensions { get; set; } = new();
    public string? DefaultEmulatorId { get; set; }

    public int retroachievementsPlatformID { get; set; } = -1;
}
