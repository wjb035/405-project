using Godot;

namespace PGEmu.Services;

public enum BrowseLayoutMode
{
    Carousel,
    List,
    Grid,
    ThreeD,
}

public static class BrowseLayoutSettings
{
    private const string SavePath = "user://appearance.cfg";
    private const string Section = "library";
    private const string LayoutKey = "browse_layout";

    public static BrowseLayoutMode GetLayout()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(SavePath) != Error.Ok)
            return BrowseLayoutMode.Carousel;

        var raw = cfg.GetValue(Section, LayoutKey, "carousel").AsString();
        return FromStoredValue(raw);
    }

    public static void SetLayout(BrowseLayoutMode layout)
    {
        var cfg = new ConfigFile();
        cfg.Load(SavePath);
        cfg.SetValue(Section, LayoutKey, ToStoredValue(layout));
        cfg.Save(SavePath);
    }

    public static string GetLabel(BrowseLayoutMode layout)
    {
        return layout switch
        {
            BrowseLayoutMode.List => "List View",
            BrowseLayoutMode.Grid => "Grid View",
            BrowseLayoutMode.ThreeD => "3D View",
            _ => "Carousel View",
        };
    }

    private static BrowseLayoutMode FromStoredValue(string raw)
    {
        return raw.ToLowerInvariant() switch
        {
            "list" => BrowseLayoutMode.List,
            "grid" => BrowseLayoutMode.Grid,
            "threed" => BrowseLayoutMode.ThreeD, 
            _ => BrowseLayoutMode.Carousel,
        };
    }

    private static string ToStoredValue(BrowseLayoutMode layout)
    {
        return layout switch
        {
            BrowseLayoutMode.List => "list",
            BrowseLayoutMode.Grid => "grid",
            BrowseLayoutMode.ThreeD => "threed",
            _ => "carousel",
        };
    }
}
