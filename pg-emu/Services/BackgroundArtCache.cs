using Godot;
using PGEmu.app;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PGEmu.Services;

public partial class BackgroundArtCache : Node
{
	public static BackgroundArtCache? Instance { get; private set; }

	private const int StartupWarmupPerPlatformLimit = 48;
	private readonly HashSet<string> _warmedPlatformIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, CachedPlatformLibrary> _libraryCache = new(StringComparer.OrdinalIgnoreCase);
	private CancellationTokenSource? _warmupCts;
	private AppConfig? _config;
	private string? _configPath;
	private string? _priorityPlatformId;

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		_ = BeginFromDiscoveredConfigAsync();
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;

		_warmupCts?.Cancel();
		_warmupCts?.Dispose();
		_warmupCts = null;
		base._ExitTree();
	}

	public void Begin(AppConfig? config, string? configPath = null)
	{
		if (config == null)
			return;

		_config = config;
		_configPath = configPath ?? _configPath;
		RestartWarmup();
	}

	public void PrioritizePlatform(PlatformConfig? platform)
	{
		if (platform == null || string.IsNullOrWhiteSpace(platform.Id))
			return;

		if (string.Equals(_priorityPlatformId, platform.Id, StringComparison.OrdinalIgnoreCase) &&
			_warmupCts is { IsCancellationRequested: false })
		{
			return;
		}

		_priorityPlatformId = platform.Id;
		if (_config != null)
			RestartWarmup();
	}

	public bool TryGetCachedLibrary(PlatformConfig? platform, out List<GameEntry> games, out string scanDir)
	{
		games = new List<GameEntry>();
		scanDir = string.Empty;

		if (platform == null || string.IsNullOrWhiteSpace(platform.Id))
			return false;

		if (!_libraryCache.TryGetValue(platform.Id, out var cachedLibrary))
			return false;

		scanDir = cachedLibrary.ScanDir;
		games = CloneGames(cachedLibrary.Games);
		return true;
	}

	private async Task BeginFromDiscoveredConfigAsync()
	{
		await ToSignal(GetTree().CreateTimer(0.1), Godot.Timer.SignalName.Timeout);

		try
		{
			var configPath = ConfigFinder.FindConfigPath();
			if (string.IsNullOrWhiteSpace(configPath))
				return;

			var config = AppConfig.Load(configPath);
			config.LibraryRoot = ExpandHomePath(config.LibraryRoot);
			_config = config;
			_configPath = configPath;
			RestartWarmup();
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Cover art startup cache failed to load config: {ex.Message}");
		}
	}

	private void RestartWarmup()
	{
		if (_config == null || string.IsNullOrWhiteSpace(_config.LibraryRoot))
			return;

		_warmupCts?.Cancel();
		_warmupCts?.Dispose();
		_warmupCts = new CancellationTokenSource();
		_ = WarmAllPlatformsAsync(_warmupCts.Token);
	}

	private async Task WarmAllPlatformsAsync(CancellationToken cancellationToken)
	{
		try
		{
			foreach (var platform in GetWarmupOrder())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (string.IsNullOrWhiteSpace(platform.Id) || _warmedPlatformIds.Contains(platform.Id))
					continue;

				await WarmPlatformAsync(platform, cancellationToken);
				_warmedPlatformIds.Add(platform.Id);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Cover art startup warm-up failed: {ex.Message}");
		}
	}

	private IEnumerable<PlatformConfig> GetWarmupOrder()
	{
		if (_config?.Platforms == null)
			yield break;

		if (!string.IsNullOrWhiteSpace(_priorityPlatformId))
		{
			var priority = _config.Platforms.FirstOrDefault(platform =>
				string.Equals(platform.Id, _priorityPlatformId, StringComparison.OrdinalIgnoreCase));
			if (priority != null)
				yield return priority;
		}

		foreach (var platform in _config.Platforms)
		{
			if (!string.IsNullOrWhiteSpace(_priorityPlatformId) &&
				string.Equals(platform.Id, _priorityPlatformId, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			yield return platform;
		}
	}

	private async Task WarmPlatformAsync(PlatformConfig platform, CancellationToken cancellationToken)
	{
		var scanResult = await Task.Run(() =>
		{
			var games = LibraryScanner.Scan(platform, _config!.LibraryRoot, out var resolvedDir)
				.Where(game => !string.IsNullOrWhiteSpace(game.Path))
				.OrderBy(game => game.Title, StringComparer.OrdinalIgnoreCase)
				.ToList();

			return (Games: games, ScanDir: resolvedDir);
		}, cancellationToken);

		var scannedGames = scanResult.Games;
		var scanDir = scanResult.ScanDir;
		_libraryCache[platform.Id] = new CachedPlatformLibrary(scanDir, CloneGames(scannedGames));

		var gamesToWarm = scannedGames
			.Take(StartupWarmupPerPlatformLimit)
			.ToList();
		if (gamesToWarm.Count == 0)
			return;

		cancellationToken.ThrowIfCancellationRequested();
		LibretroThumbnailService.ApplyCachedCoverArt(platform, gamesToWarm);
		await PrefetchCoverArtTexturesAsync(gamesToWarm, cancellationToken);

		await LibretroThumbnailService.PopulateCoverArtAsync(platform, gamesToWarm, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		await PrefetchCoverArtTexturesAsync(gamesToWarm, cancellationToken);
		_libraryCache[platform.Id] = new CachedPlatformLibrary(scanDir, CloneGames(scannedGames));
	}

	private static async Task PrefetchCoverArtTexturesAsync(IEnumerable<GameEntry> games, CancellationToken cancellationToken)
	{
		var coverArtUrls = games
			.Select(game => game.CoverArtUrl)
			.Where(static url => !string.IsNullOrWhiteSpace(url))
			.Cast<string>()
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		var tasks = coverArtUrls.Select(async coverArtUrl =>
		{
			cancellationToken.ThrowIfCancellationRequested();
			await CoverArtImageCache.GetTextureAsync(coverArtUrl);
		});

		await Task.WhenAll(tasks);
	}

	private static List<GameEntry> CloneGames(IEnumerable<GameEntry> games)
	{
		return games
			.Select(game => new GameEntry
			{
				Name = game.Name,
				Path = game.Path,
				CoverArtUrl = game.CoverArtUrl,
				AchievementNum = game.AchievementNum,
				retroAchievementsGameId = game.retroAchievementsGameId,
				platform = game.platform,
				TimePlayed = game.TimePlayed,
			})
			.ToList();
	}

	private static string ExpandHomePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path) || path[0] != '~')
			return path;

		var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
		if (path.Length == 1)
			return home;

		var rest = path[1..].TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
		return System.IO.Path.Combine(home, rest);
	}

	private sealed record CachedPlatformLibrary(string ScanDir, List<GameEntry> Games);
}
