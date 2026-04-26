using Godot;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PGEmu.Services;

public static class CoverArtImageCache
{
	private static readonly System.Net.Http.HttpClient Client = new();
	private static readonly ConcurrentDictionary<string, byte[]> DataCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, Task<byte[]?>> FetchTasks = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, Texture2D> TextureCache = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, Task<Texture2D?>> TextureTasks = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, DateTimeOffset> RecentFailures = new(StringComparer.OrdinalIgnoreCase);
	private static readonly SemaphoreSlim DownloadThrottle = new(6, 6);
	private static readonly SemaphoreSlim TextureDecodeThrottle = new(2, 2);
	private static readonly Lazy<string> DiskCacheDirectory = new(
		() => ProjectSettings.GlobalizePath("user://cover-art-cache/"));
	private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(5);

	public static Task<byte[]?> GetBytesAsync(string coverArtUrl)
	{
		if (string.IsNullOrWhiteSpace(coverArtUrl))
			return Task.FromResult<byte[]?>(null);

		if (RecentFailures.TryGetValue(coverArtUrl, out var failedAt) &&
			DateTimeOffset.UtcNow - failedAt < FailureCooldown)
		{
			return Task.FromResult<byte[]?>(null);
		}

		return FetchTasks.GetOrAdd(coverArtUrl, FetchBytesAsync);
	}

	public static bool HasTexture(string coverArtUrl)
	{
		return !string.IsNullOrWhiteSpace(coverArtUrl) && TextureCache.ContainsKey(coverArtUrl);
	}

	public static bool TryGetTexture(string coverArtUrl, out Texture2D texture)
	{
		if (!string.IsNullOrWhiteSpace(coverArtUrl) &&
			TextureCache.TryGetValue(coverArtUrl, out var cachedTexture))
		{
			texture = cachedTexture;
			return true;
		}

		texture = null!;
		return false;
	}

	public static Task<Texture2D?> GetTextureAsync(string coverArtUrl)
	{
		if (string.IsNullOrWhiteSpace(coverArtUrl))
			return Task.FromResult<Texture2D?>(null);

		if (TextureCache.TryGetValue(coverArtUrl, out var cachedTexture))
			return Task.FromResult<Texture2D?>(cachedTexture);

		return TextureTasks.GetOrAdd(coverArtUrl, CreateTextureAsync);
	}

	public static async Task<Texture2D?> GetCachedTextureAsync(string coverArtUrl)
	{
		if (string.IsNullOrWhiteSpace(coverArtUrl))
			return null;

		if (TextureCache.TryGetValue(coverArtUrl, out var cachedTexture))
			return cachedTexture;

		var imageData = await GetCachedBytesAsync(coverArtUrl);
		if (imageData == null || imageData.Length == 0)
			return null;

		return await CreateTextureFromBytesAsync(coverArtUrl, imageData);
	}

	public static async Task<byte[]?> GetCachedBytesAsync(string coverArtUrl)
	{
		if (string.IsNullOrWhiteSpace(coverArtUrl))
			return null;

		if (DataCache.TryGetValue(coverArtUrl, out var cachedBytes))
			return cachedBytes;

		return await TryReadFromDiskAsync(coverArtUrl);
	}

	public static void ForgetMemoryBytes(string coverArtUrl)
	{
		if (!string.IsNullOrWhiteSpace(coverArtUrl))
			DataCache.TryRemove(coverArtUrl, out _);
	}

	public static void Reject(string coverArtUrl)
	{
		if (string.IsNullOrWhiteSpace(coverArtUrl))
			return;

		DataCache.TryRemove(coverArtUrl, out _);
		RecentFailures[coverArtUrl] = DateTimeOffset.UtcNow;
		TextureCache.TryRemove(coverArtUrl, out _);
		TryDeleteDiskCache(coverArtUrl);
	}

	private static async Task<Texture2D?> CreateTextureAsync(string coverArtUrl)
	{
		try
		{
			var imageData = await GetBytesAsync(coverArtUrl);
			if (imageData == null || imageData.Length == 0)
				return null;

			return await CreateTextureFromBytesAsync(coverArtUrl, imageData);
		}
		finally
		{
			TextureTasks.TryRemove(coverArtUrl, out _);
		}
	}

	private static async Task<Texture2D?> CreateTextureFromBytesAsync(string coverArtUrl, byte[] imageData)
	{
		await TextureDecodeThrottle.WaitAsync();
		try
		{
			if (TextureCache.TryGetValue(coverArtUrl, out var cachedTexture))
				return cachedTexture;

			Image coverArt = new Image();
			Error loadError = coverArt.LoadPngFromBuffer(imageData);
			if (loadError != Error.Ok)
				loadError = coverArt.LoadJpgFromBuffer(imageData);
			if (loadError != Error.Ok)
				loadError = coverArt.LoadWebpFromBuffer(imageData);

			if (loadError != Error.Ok)
			{
				Reject(coverArtUrl);
				return null;
			}

			var texture = ImageTexture.CreateFromImage(coverArt);
			ForgetMemoryBytes(coverArtUrl);
			return TextureCache.GetOrAdd(coverArtUrl, texture);
		}
		finally
		{
			TextureDecodeThrottle.Release();
		}
	}

	private static async Task<byte[]?> FetchBytesAsync(string coverArtUrl)
	{
		try
		{
			byte[]? cachedImageData = await GetCachedBytesAsync(coverArtUrl);
			if (cachedImageData != null && cachedImageData.Length > 0)
			{
				DataCache[coverArtUrl] = cachedImageData;
				RecentFailures.TryRemove(coverArtUrl, out _);
				return cachedImageData;
			}

			await DownloadThrottle.WaitAsync();
			try
			{
				using var timeout = new CancellationTokenSource(RequestTimeout);
				byte[] imageData = await Client.GetByteArrayAsync(coverArtUrl, timeout.Token);
				if (imageData.Length == 0)
					return null;

				await TryWriteToDiskAsync(coverArtUrl, imageData);
				RecentFailures.TryRemove(coverArtUrl, out _);
				DataCache[coverArtUrl] = imageData;
				return imageData;
			}
			finally
			{
				DownloadThrottle.Release();
			}
		}
		catch (OperationCanceledException)
		{
			RecentFailures[coverArtUrl] = DateTimeOffset.UtcNow;
			GD.PrintErr($"Timed out downloading cover art after {RequestTimeout.TotalSeconds:0} seconds: {coverArtUrl}");
			return null;
		}
		catch (Exception ex)
		{
			RecentFailures[coverArtUrl] = DateTimeOffset.UtcNow;
			GD.PrintErr($"Failed to download cover art bytes: {ex.Message}");
			return null;
		}
		finally
		{
			FetchTasks.TryRemove(coverArtUrl, out _);
		}
	}

	private static async Task<byte[]?> TryReadFromDiskAsync(string coverArtUrl)
	{
		try
		{
			var cachePath = GetDiskCachePath(coverArtUrl);
			if (!File.Exists(cachePath))
				return null;

			byte[] imageData = await File.ReadAllBytesAsync(cachePath);
			return imageData.Length > 0 ? imageData : null;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to read cached cover art: {ex.Message}");
			return null;
		}
	}

	private static async Task TryWriteToDiskAsync(string coverArtUrl, byte[] imageData)
	{
		try
		{
			var cachePath = GetDiskCachePath(coverArtUrl);
			var cacheDirectory = Path.GetDirectoryName(cachePath);
			if (!string.IsNullOrWhiteSpace(cacheDirectory))
				Directory.CreateDirectory(cacheDirectory);

			var tempPath = $"{cachePath}.{Guid.NewGuid():N}.tmp";
			await File.WriteAllBytesAsync(tempPath, imageData);
			File.Move(tempPath, cachePath, overwrite: true);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to cache cover art locally: {ex.Message}");
		}
	}

	private static string GetDiskCachePath(string coverArtUrl)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(coverArtUrl.Trim()));
		var fileName = $"{Convert.ToHexString(hash).ToLowerInvariant()}.img";
		return Path.Combine(DiskCacheDirectory.Value, fileName);
	}

	private static void TryDeleteDiskCache(string coverArtUrl)
	{
		try
		{
			var cachePath = GetDiskCachePath(coverArtUrl);
			if (File.Exists(cachePath))
				File.Delete(cachePath);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed to delete invalid cached cover art: {ex.Message}");
		}
	}
}
