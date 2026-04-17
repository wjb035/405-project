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
public static class CollectionStorage
{
    public static List<KeyValuePair<string, List<GameEntry>>> collections = new List<KeyValuePair<string, List<GameEntry>>>();
    public static List<GameEntry> currentCollection = null;
    public static string SearchResult;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    
    public static async Task saveToJson()
    {
        await using FileStream createStream = File.Create("collections.json");
        await JsonSerializer.SerializeAsync(createStream, collections, _jsonOptions);
        Console.WriteLine($"Object serialized and saved to path");
    }
    
    public static async Task LoadFromJson()
    {
        if (!File.Exists("collections.json"))
        {
            Console.WriteLine("No collections.json found.");
            collections = new List<KeyValuePair<string, List<GameEntry>>>();
            currentCollection = null;
            return;
        }

        await using FileStream openStream = File.OpenRead("collections.json");

        var loaded = await JsonSerializer.DeserializeAsync<
            List<KeyValuePair<string, List<GameEntry>>>
        >(openStream, _jsonOptions);

        collections = loaded ?? new List<KeyValuePair<string, List<GameEntry>>>();

        // Reset currentCollection so Godot uses the restored list
        currentCollection = collections.Count > 0
            ? collections[0].Value
            : null;

        Console.WriteLine($"Loaded {collections.Count} collections.");
    }
}