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

    /// <summary>Minimum normalized similarity (0–1) to accept a fuzzy directory match.</summary>
    private const double MinFuzzyNameSimilarity = 0.76;
    private const int MaxConcurrentCoverArtResolutions = 3;

    private static readonly System.Net.Http.HttpClient Client = new();
    private static readonly SemaphoreSlim LookupThrottle = new(6, 6);
    private static readonly SemaphoreSlim ResolutionThrottle =
        new(MaxConcurrentCoverArtResolutions, MaxConcurrentCoverArtResolutions);
    private static readonly ConcurrentDictionary<string, Lazy<Task<ThumbnailDirectoryIndex>>> DirectoryIndexLazy =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex DirectoryListingPngHref = new(
        @"<a href=""([^""]+\.png)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly ConcurrentDictionary<string, string?> UrlCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PersistentUrlCacheLock = new();
    private static readonly Lazy<string> PersistentUrlCachePath = new(
        () => ProjectSettings.GlobalizePath("user://cover-art-url-cache.json"));
    private static Dictionary<string, string> PersistentUrlCache = new(StringComparer.OrdinalIgnoreCase);
    private static bool PersistentUrlCacheLoaded;
    private static readonly Regex InvalidThumbnailChars = new(@"[&*/:`<>?\\|]", RegexOptions.Compiled);
    private static readonly Regex TrailingSquareBracketTags = new(@"\s*\[[^\]]*\]\s*$", RegexOptions.Compiled);
    private static readonly Regex AnywhereSquareBracketTags = new(@"\[[^\]]*\]", RegexOptions.Compiled);
    private static readonly Regex TrailingParenTag = new(@"\s*\([^)]*\)\s*$", RegexOptions.Compiled);
    private static readonly Regex LeadingCatalogNumber = new(@"^\s*\d+\s*-\s+", RegexOptions.Compiled);
    private static readonly Regex UnderscoreRuns = new(@"_+", RegexOptions.Compiled);
    private static readonly Regex FuzzyTokenSplit = new(@"[\s\-\+\.,;:!\?&_'/\(\)\[\]]+", RegexOptions.Compiled);
    private static readonly HashSet<string> WeakFuzzyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "usa", "europe", "japan", "germany", "france", "italy", "spain", "australia", "canada", "korea",
        "united", "kingdom", "en", "fr", "de", "es", "it", "nl", "sv", "da", "rev", "beta", "demo",
        "kiosk", "virtual", "console", "enhanced", "ndsi", "not", "for", "resale"
    };
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

    public static async Task PopulateCoverArtAsync(
        PlatformConfig? platform,
        IEnumerable<GameEntry> games,
        CancellationToken cancellationToken = default,
        Action<GameEntry>? coverArtResolved = null)
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
            .Select(game => PopulateCoverArtThrottledAsync(playlistName, game, cancellationToken, coverArtResolved));

        await Task.WhenAll(tasks);
    }

    private static async Task PopulateCoverArtThrottledAsync(
        string playlistName,
        GameEntry game,
        CancellationToken cancellationToken,
        Action<GameEntry>? coverArtResolved)
    {
        await ResolutionThrottle.WaitAsync(cancellationToken);
        try
        {
            await PopulateCoverArtAsync(playlistName, game, cancellationToken, coverArtResolved);
        }
        finally
        {
            ResolutionThrottle.Release();
        }
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

    private static async Task PopulateCoverArtAsync(
        string playlistName,
        GameEntry game,
        CancellationToken cancellationToken,
        Action<GameEntry>? coverArtResolved)
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
            coverArtResolved?.Invoke(game);
            return;
        }

        foreach (var thumbnailType in ThumbnailTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fuzzyUrl = await TryResolveClosestThumbnailUrlAsync(playlistName, thumbnailType, game, cancellationToken);
            if (string.IsNullOrWhiteSpace(fuzzyUrl))
            {
                continue;
            }

            StoreCachedCoverArtUrl(GetCacheKey(playlistName, game), fuzzyUrl);
            game.CoverArtUrl = fuzzyUrl;
            coverArtResolved?.Invoke(game);
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
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return;
            }

            var normalized = NormalizeCandidateName(rawValue);
            if (string.IsNullOrWhiteSpace(normalized) || !seen.Add(normalized))
            {
                return;
            }

            candidates.Add(normalized);
        }

        void AddBracketStrippedVariants(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return;
            }

            var debadged = RemoveAnywhereBracketTags(rawValue);
            if (string.IsNullOrWhiteSpace(debadged))
            {
                return;
            }

            Add(debadged);
            Add(ReplaceUnderscoresWithSpaces(debadged));
            Add(RemoveLeadingCatalogNumber(debadged));
            foreach (var peeled in EnumerateTrailingParenPeels(debadged))
            {
                Add(peeled);
                Add(MoveTrailingArticleToFront(peeled));
                Add(MoveTrailingArticleToFront(ShortenAtFirstParenthesis(peeled)));
            }
        }

        var fileBaseName = Path.GetFileNameWithoutExtension(game.Path);
        var title = game.Title;
        var shortTitle = ShortenAtFirstParenthesis(title);
        var shortFileName = ShortenAtFirstParenthesis(fileBaseName);

        Add(title);
        Add(fileBaseName);
        Add(shortTitle);
        Add(shortFileName);
        Add(RemoveTrailingBracketTags(title));
        Add(RemoveTrailingBracketTags(fileBaseName));
        Add(MoveTrailingArticleToFront(shortTitle));
        Add(MoveTrailingArticleToFront(shortFileName));

        AddBracketStrippedVariants(title);
        AddBracketStrippedVariants(fileBaseName);
        AddBracketStrippedVariants(shortTitle);
        AddBracketStrippedVariants(shortFileName);

        return candidates;
    }

    private static string NormalizeCandidateName(string rawValue)
    {
        var normalized = NormalizeTypography(rawValue.Trim());
        normalized = InvalidThumbnailChars.Replace(normalized, "_");
        normalized = Regex.Replace(normalized, @"\s+", " ");
        return normalized.Trim();
    }

    private static string NormalizeTypography(string value)
    {
        return value
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u2032', '\'')
            .Replace('\u00B4', '\'')
            .Replace('\u201C', '"')
            .Replace('\u201D', '"');
    }

    private static string RemoveAnywhereBracketTags(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var stripped = AnywhereSquareBracketTags.Replace(value.Trim(), " ");
        return Regex.Replace(stripped, @"\s+", " ").Trim();
    }

    private static string ReplaceUnderscoresWithSpaces(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withSpaces = UnderscoreRuns.Replace(value.Trim(), " ");
        return Regex.Replace(withSpaces, @"\s+", " ").Trim();
    }

    private static string RemoveLeadingCatalogNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return LeadingCatalogNumber.Replace(value.Trim(), "").Trim();
    }

    private static IEnumerable<string> EnumerateTrailingParenPeels(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var current = value.Trim();
        var guard = 0;
        while (!string.IsNullOrEmpty(current) && guard++ < 16)
        {
            yield return current;
            var next = TrailingParenTag.Replace(current, "").TrimEnd();
            if (next.Length == 0 || next.Length >= current.Length)
            {
                yield break;
            }

            current = next;
        }
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

    private sealed record ThumbnailFileRef(
        string FullUrl,
        string NormalizedCompareName,
        string[] Tokens,
        HashSet<string> TokenSet);

    private sealed record FuzzyQuery(
        string NormalizedName,
        string[] Tokens,
        HashSet<string> TokenSet);

    private sealed class ThumbnailDirectoryIndex
    {
        public static ThumbnailDirectoryIndex Empty { get; } = new(Array.Empty<ThumbnailFileRef>());

        public ThumbnailDirectoryIndex(IReadOnlyList<ThumbnailFileRef> files) => Files = files;

        public IReadOnlyList<ThumbnailFileRef> Files { get; }
    }

    private static string DirectoryIndexCacheKey(string playlistName, string thumbnailType) => $"{playlistName}\n{thumbnailType}";

    private static Task<ThumbnailDirectoryIndex> GetThumbnailDirectoryIndexAsync(
        string playlistName,
        string thumbnailType,
        CancellationToken cancellationToken)
    {
        var key = DirectoryIndexCacheKey(playlistName, thumbnailType);
        var lazy = DirectoryIndexLazy.GetOrAdd(
            key,
            static k =>
            {
                var parts = k.Split('\n', 2);
                var playlist = parts.Length > 0 ? parts[0] : string.Empty;
                var thumbType = parts.Length > 1 ? parts[1] : string.Empty;
                return new Lazy<Task<ThumbnailDirectoryIndex>>(
                    () => DownloadThumbnailDirectoryIndexAsync(playlist, thumbType));
            });

        return lazy.Value.WaitAsync(cancellationToken);
    }

    private static async Task<ThumbnailDirectoryIndex> DownloadThumbnailDirectoryIndexAsync(
        string playlistName,
        string thumbnailType)
    {
        await LookupThrottle.WaitAsync(CancellationToken.None);
        try
        {
            var directoryUrl =
                $"{ThumbnailServerBaseUrl}/{Uri.EscapeDataString(playlistName)}/{thumbnailType}/";
            using var response = await Client.GetAsync(directoryUrl, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                return ThumbnailDirectoryIndex.Empty;
            }

            var html = await response.Content.ReadAsStringAsync(CancellationToken.None);
            return ParseThumbnailDirectoryHtml(directoryUrl, html);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Libretro thumbnail directory listing failed ({playlistName}/{thumbnailType}): {ex.Message}");
            return ThumbnailDirectoryIndex.Empty;
        }
        finally
        {
            LookupThrottle.Release();
        }
    }

    private static ThumbnailDirectoryIndex ParseThumbnailDirectoryHtml(string directoryUrl, string html)
    {
        var refs = new List<ThumbnailFileRef>();
        foreach (Match match in DirectoryListingPngHref.Matches(html))
        {
            var href = match.Groups[1].Value;
            if (href.StartsWith("?", StringComparison.Ordinal) ||
                href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!href.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fullUrl = directoryUrl + href;
            var decoded = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(href));
            var normalized = NormalizeForFuzzyCompare(decoded);
            refs.Add(new ThumbnailFileRef(
                fullUrl,
                normalized,
                TokenizeForIndexFilter(normalized).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                new HashSet<string>(TokenizeForIndexFilter(normalized), StringComparer.OrdinalIgnoreCase)));
        }

        return new ThumbnailDirectoryIndex(refs);
    }

    private static async Task<string?> TryResolveClosestThumbnailUrlAsync(
        string playlistName,
        string thumbnailType,
        GameEntry game,
        CancellationToken cancellationToken)
    {
        var index = await GetThumbnailDirectoryIndexAsync(playlistName, thumbnailType, cancellationToken);
        if (index.Files.Count == 0)
        {
            return null;
        }

        var queries = BuildCandidateNames(game)
            .Select(NormalizeForFuzzyCompare)
            .Where(q => q.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(q => q.Length)
            .Select(q => new FuzzyQuery(
                q,
                TokenizeForIndexFilter(q).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                new HashSet<string>(TokenizeForIndexFilter(q), StringComparer.OrdinalIgnoreCase)))
            .ToList();

        if (queries.Count == 0)
        {
            return null;
        }

        foreach (var query in queries)
        {
            var exact = index.Files.FirstOrDefault(file =>
                string.Equals(file.NormalizedCompareName, query.NormalizedName, StringComparison.Ordinal));
            if (exact != null)
            {
                return exact.FullUrl;
            }
        }

        var significantTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in queries)
        {
            foreach (var token in q.Tokens)
            {
                significantTokens.Add(token);
            }
        }

        IEnumerable<ThumbnailFileRef> fileRefs = index.Files;
        if (significantTokens.Count > 0)
        {
            var narrowed = index.Files
                .Where(f => SignificantTokenOverlap(f.NormalizedCompareName, significantTokens))
                .ToList();
            if (narrowed.Count > 0)
            {
                fileRefs = narrowed;
            }
        }

        ThumbnailFileRef? best = null;
        var bestScore = 0.0;
        foreach (var file in fileRefs)
        {
            var fileBest = 0.0;
            foreach (var q in queries)
            {
                var score = NameSimilarity(q, file);
                if (score > fileBest)
                {
                    fileBest = score;
                }
            }

            if (IsBetterFuzzyMatch(fileBest, file, bestScore, best))
            {
                bestScore = fileBest;
                best = file;
            }
        }

        if (best == null || bestScore < MinFuzzyNameSimilarity)
        {
            return null;
        }

        return best.FullUrl;
    }

    private static IEnumerable<string> TokenizeForIndexFilter(string normalizedQuery)
    {
        foreach (var token in FuzzyTokenSplit.Split(normalizedQuery))
        {
            if (token.Length < 2 && !token.All(char.IsDigit))
            {
                continue;
            }

            yield return token;
        }
    }

    private static bool SignificantTokenOverlap(string normalizedFileName, HashSet<string> significantTokens)
    {
        foreach (var token in significantTokens)
        {
            if (normalizedFileName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeForFuzzyCompare(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var v = NormalizeTypography(rawValue.Trim()).ToLowerInvariant();
        v = v.Replace('_', ' ');
        v = FuzzyTokenSplit.Replace(v, " ");
        v = Regex.Replace(v, @"\s+", " ");
        return v.Trim();
    }

    private static double NameSimilarity(FuzzyQuery query, ThumbnailFileRef file)
    {
        var a = query.NormalizedName;
        var b = file.NormalizedCompareName;
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return 0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var tokenScore = TokenSimilarity(query, file);
        if (tokenScore < 0.45)
        {
            return tokenScore;
        }

        var longLen = Math.Max(a.Length, b.Length);
        var charScore = 0.0;
        if (longLen <= 96)
        {
            var dist = LevenshteinDistance(a, b);
            charScore = 1.0 - dist / (double)longLen;
        }

        var substringScore = 0.0;
        if (a.Length >= 8 && b.Contains(a, StringComparison.Ordinal))
        {
            substringScore = Math.Min(0.94, 0.82 + (0.12 * a.Length / Math.Max(b.Length, 1)));
        }

        return Math.Max(Math.Max(tokenScore, charScore), substringScore);
    }

    private static double TokenSimilarity(FuzzyQuery query, ThumbnailFileRef file)
    {
        if (query.Tokens.Length == 0 || file.Tokens.Length == 0)
        {
            return 0;
        }

        var queryMatches = query.Tokens.Count(file.TokenSet.Contains);
        if (queryMatches == 0)
        {
            return 0;
        }

        var fileScoreTokens = file.Tokens
            .Where(token => query.TokenSet.Contains(token) || !WeakFuzzyTokens.Contains(token))
            .ToArray();
        if (fileScoreTokens.Length == 0)
        {
            fileScoreTokens = file.Tokens;
        }

        var fileMatches = fileScoreTokens.Count(query.TokenSet.Contains);
        var queryCoverage = queryMatches / (double)query.Tokens.Length;
        var fileCoverage = fileMatches / (double)fileScoreTokens.Length;
        return (queryCoverage * 0.78) + (fileCoverage * 0.22);
    }

    private static bool IsBetterFuzzyMatch(
        double candidateScore,
        ThumbnailFileRef candidate,
        double bestScore,
        ThumbnailFileRef? best)
    {
        if (candidateScore > bestScore + 0.0001)
        {
            return true;
        }

        if (best == null || Math.Abs(candidateScore - bestScore) >= 0.0001)
        {
            return false;
        }

        var candidateRegionPreference = RegionPreference(candidate);
        var bestRegionPreference = RegionPreference(best);
        if (candidateRegionPreference != bestRegionPreference)
        {
            return candidateRegionPreference < bestRegionPreference;
        }

        return candidate.NormalizedCompareName.Length < best.NormalizedCompareName.Length;
    }

    private static int RegionPreference(ThumbnailFileRef file)
    {
        if (file.TokenSet.Contains("usa"))
        {
            return 0;
        }

        if (file.TokenSet.Contains("europe"))
        {
            return 1;
        }

        if (file.TokenSet.Contains("canada") ||
            (file.TokenSet.Contains("united") && file.TokenSet.Contains("kingdom")) ||
            file.TokenSet.Contains("australia"))
        {
            return 2;
        }

        if (file.TokenSet.Contains("japan"))
        {
            return 4;
        }

        return 3;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        if (n == 0)
        {
            return m;
        }

        if (m == 0)
        {
            return n;
        }

        var previous = new int[m + 1];
        var current = new int[m + 1];
        for (var j = 0; j <= m; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= n; i++)
        {
            current[0] = i;
            var ai = a[i - 1];
            for (var j = 1; j <= m; j++)
            {
                var cost = ai == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[m];
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
