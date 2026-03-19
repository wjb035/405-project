using System;
using System.Net;

namespace PGEmu.app;
using System.Collections.Generic;
using RetroAchievements.Api;
using RetroAchievements.Api.Response.Users.Records;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

using System.Collections;
public static class PlaytimeStorage
{
    
    // maps the name of an emulator to the list of the games pf that platform that have a playtime tracked
    public static List<KeyValuePair<string, List<GameEntry>>> playtimeTracked = new List<KeyValuePair<string, List<GameEntry>>>();
    public static List<GameEntry> currentCollection = null;
    public static Dictionary<string, string> EmulatorToName = new Dictionary<string, string>
    {
        {"ppsspp","PPSSPPWindows"},
        {"dolphin", "dolphin"},
        {"pcsx2", "pcsx2-qt"}
    };
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static async Task SaveToJson()
    {
        await using FileStream createStream = File.Create("playtime.json");
        await JsonSerializer.SerializeAsync(createStream, playtimeTracked, _jsonOptions);
        Console.WriteLine("Playtime data saved.");
    }

    public static async Task LoadFromJson()
    {
        if (!File.Exists("playtime.json"))
        {
            Console.WriteLine("No playtime.json found.");
            playtimeTracked = new List<KeyValuePair<string, List<GameEntry>>>();
            currentCollection = null;
            return;
        }

        await using FileStream openStream = File.OpenRead("playtime.json");

        var loaded = await JsonSerializer.DeserializeAsync<
            List<KeyValuePair<string, List<GameEntry>>>
        >(openStream, _jsonOptions);

        playtimeTracked = loaded ?? new List<KeyValuePair<string, List<GameEntry>>>();

        // reset current collection
        currentCollection = playtimeTracked.Count > 0
            ? playtimeTracked[0].Value
            : null;

        Console.WriteLine($"Loaded playtime data for {playtimeTracked.Count} emulators.");
    }
}
   
