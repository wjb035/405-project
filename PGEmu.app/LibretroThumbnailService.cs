using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PGEmu.app;

public static class LibretroThumbnailService
{
    private const string ThumbnailServerBaseUrl = "https://thumbnails.libretro.com";

    private static readonly HttpClient Client = new();
    private static readonly SemaphoreSlim LookupThrottle = new(6, 6);
    private static readonly Dictionary<string, string?> UrlCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex InvalidThumbnailChars = new(@"[&*/:`<>?\\|]", RegexOptions.Compiled);
    private static readonly Regex TrailingSquareBracketTags = new(@"\s*\[[^\]]*\]\s*$", RegexOptions.Compiled);
    private static readonly string[] ThumbnailTypes = { "Named_Boxarts", "Named_Titles" };

    private static readonly Dictionary<string, string> PlaylistNamesByPlatformId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gba"] = "Nintendo - Game Boy Advance",
        ["gc"] = "Nintendo - GameCube",
        ["wii"] = "Nintendo - Wii",
        ["ps2"] = "Sony - PlayStation 2",
        ["psp"] = "Sony - PlayStation Portable",
    };

    private static readonly Dictionary<string, string> PlaylistNamesByPlatformName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Game Boy Advance"] = "Nintendo - Game Boy Advance",
        ["Nintendo GameCube"] = "Nintendo - GameCube",
        ["Nintendo Wii"] = "Nintendo - Wii",
        ["Playstation 2"] = "Sony - PlayStation 2",
        ["Playstation Portable"] = "Sony - PlayStation Portable",
    };

    public static async Task PopulateCoverArtAsync(PlatformConfig? platform, IEnumerable<GameEntry> games)
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
            .Select(game => PopulateCoverArtAsync(playlistName, game));

        await Task.WhenAll(tasks);
    }

    private static async Task PopulateCoverArtAsync(string playlistName, GameEntry game)
    {
        if (!ShouldResolveCoverArt(game))
        {
            return;
        }

        var cacheKey = $"{playlistName}\n{game.Title}";
        if (UrlCache.TryGetValue(cacheKey, out var cachedUrl))
        {
            if (!string.IsNullOrWhiteSpace(cachedUrl))
            {
                game.CoverArtUrl = cachedUrl;
            }

            return;
        }

        foreach (var candidateUrl in BuildCandidateUrls(playlistName, game))
        {
            if (!await UrlExistsAsync(candidateUrl))
            {
                continue;
            }

            UrlCache[cacheKey] = candidateUrl;
            game.CoverArtUrl = candidateUrl;
            return;
        }

        UrlCache[cacheKey] = null;
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

    private static async Task<bool> UrlExistsAsync(string url)
    {
        await LookupThrottle.WaitAsync();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            if (response.StatusCode != HttpStatusCode.MethodNotAllowed)
            {
                return false;
            }

            using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, url);
            using var fallbackResponse = await Client.SendAsync(fallbackRequest, HttpCompletionOption.ResponseHeadersRead);
            return fallbackResponse.IsSuccessStatusCode;
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
