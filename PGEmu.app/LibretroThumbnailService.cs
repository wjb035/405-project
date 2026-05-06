using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace PGEmu.app;

public static class LibretroThumbnailService
{
    private const string ThumbnailServerBaseUrl = "https://thumbnails.libretro.com";

    private static readonly System.Net.Http.HttpClient Client = new();
    private static readonly SemaphoreSlim LookupThrottle = new(6, 6);
    private static readonly ConcurrentDictionary<string, string?> UrlCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PersistentUrlCacheLock = new();
    private static readonly Lazy<string> PersistentUrlCachePath = new(
        () => ProjectSettings.GlobalizePath("user://cover-art-url-cache.json"));
    private static Dictionary<string, string> PersistentUrlCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool PersistentUrlCacheLoaded;
    private static readonly Regex InvalidThumbnailChars = new(@"[&*/:`<>?\\|]", RegexOptions.Compiled);
    private static readonly Regex TrailingSquareBracketTags = new(@"\s*\[[^\]]*\]\s*$", RegexOptions.Compiled);
    private static readonly string[] ThumbnailTypes = { "Named_Boxarts", "Named_Titles" };

    private static readonly Dictionary<string, string> PlaylistNamesByPlatformId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gba"] = "Nintendo - Game Boy Advance",
        ["ds"] = "Nintendo - Nintendo DS",
        ["nes"] = "Nintendo - Nintendo Entertainment System",
        ["snes"] = "Nintendo - Super Nintendo Entertainment System",
        ["n64"] = "Nintendo - Nintendo 64",
        ["gc"] = "Nintendo - GameCube",
        ["wii"] = "Nintendo - Wii",
        ["ps1"] = "Sony - PlayStation",
        ["ps2"] = "Sony - PlayStation 2",
        ["psp"] = "Sony - PlayStation Portable",
    };

    private static readonly Dictionary<string, string> PlaylistNamesByPlatformName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Game Boy Advance"] = "Nintendo - Game Boy Advance",
        ["Nintendo DS"] = "Nintendo - Nintendo DS",
        ["Nintendo Entertainment System"] = "Nintendo - Nintendo Entertainment System",
        ["NES"] = "Nintendo - Nintendo Entertainment System",
        ["Super Nintendo"] = "Nintendo - Super Nintendo Entertainment System",
        ["Super Nintendo Entertainment System"] = "Nintendo - Super Nintendo Entertainment System",
        ["SNES"] = "Nintendo - Super Nintendo Entertainment System",
        ["Nintendo 64"] = "Nintendo - Nintendo 64",
        ["Nintendo GameCube"] = "Nintendo - GameCube",
        ["Nintendo Wii"] = "Nintendo - Wii",
        ["Sony PlayStation"] = "Sony - PlayStation",
        ["PlayStation"] = "Sony - PlayStation",
        ["Playstation"] = "Sony - PlayStation",
        ["Playstation 2"] = "Sony - PlayStation 2",
        ["Playstation Portable"] = "Sony - PlayStation Portable",
    };

    public static async Task PopulateCoverArtAsync(PlatformConfig? platform, IEnumerable<GameEntry> games, CancellationToken cancellationToken = default)
    {
        if (platform == null || games == null)
        {
            return;
        }

        var playlistName = ResolvePlaylistName(platform);
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            return;
        }

        var tasks = games
            .Where(game => !string.IsNullOrWhiteSpace(game.Path))
            .Select(game => PopulateCoverArtAsync(playlistName, game, cancellationToken));

        await Task.WhenAll(tasks);
    }

    public static void ApplyCachedCoverArt(PlatformConfig? platform, IEnumerable<GameEntry>? games)
    {
        if (platform == null || games == null)
        {
            return;
        }

        var playlistName = ResolvePlaylistName(platform);
        if (string.IsNullOrWhiteSpace(playlistName))
        {
            return;
        }

        foreach (var game in games.Where(game => !string.IsNullOrWhiteSpace(game.Path)))
        {
            if (!ShouldResolveCoverArt(game))
            {
                continue;
            }

            TryApplyCachedCoverArtUrl(playlistName, game);
        }
    }

    private static async Task PopulateCoverArtAsync(string playlistName, GameEntry game, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!ShouldResolveCoverArt(game))
        {
            return;
        }

        if (TryApplyCachedCoverArtUrl(playlistName, game))
        {
            return;
        }

        foreach (var candidateUrl in BuildCandidateUrls(playlistName, game))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await UrlExistsAsync(candidateUrl, cancellationToken))
            {
                continue;
            }

            StoreCachedCoverArtUrl(GetCacheKey(playlistName, game), candidateUrl);
            game.CoverArtUrl = candidateUrl;
            return;
        }

        UrlCache[GetCacheKey(playlistName, game)] = null;
    }

    private static bool TryApplyCachedCoverArtUrl(string playlistName, GameEntry game)
    {
        var cacheKey = GetCacheKey(playlistName, game);
        if (UrlCache.TryGetValue(cacheKey, out var cachedUrl))
        {
            if (!string.IsNullOrWhiteSpace(cachedUrl))
            {
                game.CoverArtUrl = cachedUrl;
            }

            return true;
        }

        if (TryGetPersistentCoverArtUrl(cacheKey, out cachedUrl))
        {
            UrlCache[cacheKey] = cachedUrl;
            game.CoverArtUrl = cachedUrl;
            return true;
        }

        return false;
    }

    private static string GetCacheKey(string playlistName, GameEntry game)
    {
        return $"{playlistName}\n{game.Title}";
    }

    private static bool TryGetPersistentCoverArtUrl(string cacheKey, out string? coverArtUrl)
    {
        EnsurePersistentUrlCacheLoaded();
        lock (PersistentUrlCacheLock)
        {
            return PersistentUrlCache.TryGetValue(cacheKey, out coverArtUrl);
        }
    }

    private static void StoreCachedCoverArtUrl(string cacheKey, string coverArtUrl)
    {
        UrlCache[cacheKey] = coverArtUrl;
        EnsurePersistentUrlCacheLoaded();

        lock (PersistentUrlCacheLock)
        {
            if (PersistentUrlCache.TryGetValue(cacheKey, out var existingUrl) &&
                string.Equals(existingUrl, coverArtUrl, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            PersistentUrlCache[cacheKey] = coverArtUrl;

            try
            {
                var cachePath = PersistentUrlCachePath.Value;
                var cacheDirectory = Path.GetDirectoryName(cachePath);
                if (!string.IsNullOrWhiteSpace(cacheDirectory))
                {
                    Directory.CreateDirectory(cacheDirectory);
                }

                var json = JsonSerializer.Serialize(PersistentUrlCache);
                File.WriteAllText(cachePath, json);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to persist cover art URL cache: {ex.Message}");
            }
        }
    }

    private static void EnsurePersistentUrlCacheLoaded()
    {
        lock (PersistentUrlCacheLock)
        {
            if (PersistentUrlCacheLoaded)
            {
                return;
            }

            PersistentUrlCacheLoaded = true;
            try
            {
                var cachePath = PersistentUrlCachePath.Value;
                if (!File.Exists(cachePath))
                {
                    return;
                }

                var json = File.ReadAllText(cachePath);
                var cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (cache != null)
                {
                    PersistentUrlCache = new Dictionary<string, string>(cache, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                PersistentUrlCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                GD.PrintErr($"Failed to read cover art URL cache: {ex.Message}");
            }
        }
    }

    private static IEnumerable<string> BuildCandidateUrls(string playlistName, GameEntry game)
    {
        foreach (var thumbnailType in ThumbnailTypes)
        {
            foreach (var candidateName in BuildCandidateNames(game))
            {
                yield return
                    $"{ThumbnailServerBaseUrl}/{Uri.EscapeDataString(playlistName)}/{thumbnailType}/{Uri.EscapeDataString(candidateName)}.png";
            }
        }
    }

    private static IEnumerable<string> BuildCandidateNames(GameEntry game)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return;
            }

            var normalized = NormalizeCandidateName(rawValue);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                candidates.Add(normalized);
            }
        }

        var fileBaseName = Path.GetFileNameWithoutExtension(game.Path);
        var shortTitle = ShortenAtFirstParenthesis(game.Title);
        var shortFileName = ShortenAtFirstParenthesis(fileBaseName);

        Add(game.Title);
        Add(fileBaseName);
        Add(shortTitle);
        Add(shortFileName);
        Add(RemoveTrailingBracketTags(game.Title));
        Add(RemoveTrailingBracketTags(fileBaseName));
        Add(MoveTrailingArticleToFront(shortTitle));
        Add(MoveTrailingArticleToFront(shortFileName));

        return candidates;
    }

    private static string NormalizeCandidateName(string rawValue)
    {
        var normalized = rawValue.Trim();
        normalized = InvalidThumbnailChars.Replace(normalized, "_");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

    private static string ShortenAtFirstParenthesis(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var index = value.IndexOf('(');
        return index >= 0 ? value[..index].TrimEnd() : value.Trim();
    }

    private static string RemoveTrailingBracketTags(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        while (TrailingSquareBracketTags.IsMatch(trimmed))
        {
            trimmed = TrailingSquareBracketTags.Replace(trimmed, string.Empty).TrimEnd();
        }

        return trimmed;
    }

    private static string MoveTrailingArticleToFront(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        var suffixMatch = Regex.Match(trimmed, @"^(?<head>.+), (?<article>The|A|An)$", RegexOptions.IgnoreCase);
        if (suffixMatch.Success)
        {
            return $"{suffixMatch.Groups["article"].Value} {suffixMatch.Groups["head"].Value}".Trim();
        }

        var hyphenMatch = Regex.Match(trimmed, @"^(?<head>.+), (?<article>The|A|An) - (?<tail>.+)$", RegexOptions.IgnoreCase);
        if (hyphenMatch.Success)
        {
            return $"{hyphenMatch.Groups["article"].Value} {hyphenMatch.Groups["head"].Value} - {hyphenMatch.Groups["tail"].Value}".Trim();
        }

        return trimmed;
    }

    private static async Task<bool> UrlExistsAsync(string url, CancellationToken cancellationToken)
    {
        await LookupThrottle.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode != HttpStatusCode.MethodNotAllowed)
            {
                return false;
            }

            using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, url);
            using var fallbackResponse = await Client.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return fallbackResponse.IsSuccessStatusCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            LookupThrottle.Release();
        }
    }

    private static bool ShouldResolveCoverArt(GameEntry game)
    {
        if (string.IsNullOrWhiteSpace(game.CoverArtUrl))
        {
            return true;
        }

        if (!Uri.TryCreate(game.CoverArtUrl, UriKind.Absolute, out var existingUri))
        {
            return false;
        }

        return existingUri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) ||
               existingUri.Host.EndsWith("retroachievements.org", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolvePlaylistName(PlatformConfig platform)
    {
        if (!string.IsNullOrWhiteSpace(platform.Id) &&
            PlaylistNamesByPlatformId.TryGetValue(platform.Id, out var byId))
        {
            return byId;
        }

        if (!string.IsNullOrWhiteSpace(platform.Name) &&
            PlaylistNamesByPlatformName.TryGetValue(platform.Name, out var byName))
        {
            return byName;
        }

        return null;
    }
}
