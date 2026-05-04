using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PGEmu.Services;

public static class ProfileVisualStyleCatalog
{
	public const string DefaultAccentId = "sky";
	public const string DefaultAvatarFrameId = "rounded";
	public const string DefaultBackgroundId = "original";

	public sealed class AccentPreset
	{
		public AccentPreset(string id, string label, Color primary)
		{
			Id = id;
			Label = label;
			Primary = primary;
		}

		public string Id { get; }
		public string Label { get; }
		public Color Primary { get; }
	}

	public sealed class AvatarFramePreset
	{
		public AvatarFramePreset(
			string id,
			string label,
			float width,
			float radius,
			float strokeWidth,
			int panelRadius,
			float avatarSize,
			int frameMargin,
			int innerMargin)
		{
			Id = id;
			Label = label;
			Width = width;
			Radius = radius;
			StrokeWidth = strokeWidth;
			PanelRadius = panelRadius;
			AvatarSize = avatarSize;
			FrameMargin = frameMargin;
			InnerMargin = innerMargin;
		}

		public string Id { get; }
		public string Label { get; }
		public float Width { get; }
		public float Radius { get; }
		public float StrokeWidth { get; }
		public int PanelRadius { get; }
		public float AvatarSize { get; }
		public int FrameMargin { get; }
		public int InnerMargin { get; }
	}

	public sealed class BackgroundPreset
	{
		public BackgroundPreset(
			string id,
			string label,
			Color overlay,
			Color cardSurface,
			Color cardSurfaceAlt,
			Color cardSurfaceInset,
			Color chipSurface,
			Color separator,
			Color sceneTint,
			Color sceneHighlight,
			string globalGradientKey,
			Color accent,
			Color showcaseAccent,
			Color recentAccent,
			Color friendsAccent,
			Color[] tileAccents)
		{
			Id = id;
			Label = label;
			Overlay = overlay;
			CardSurface = cardSurface;
			CardSurfaceAlt = cardSurfaceAlt;
			CardSurfaceInset = cardSurfaceInset;
			ChipSurface = chipSurface;
			Separator = separator;
			SceneTint = sceneTint;
			SceneHighlight = sceneHighlight;
			GlobalGradientKey = globalGradientKey;
			Accent = accent;
			ShowcaseAccent = showcaseAccent;
			RecentAccent = recentAccent;
			FriendsAccent = friendsAccent;
			TileAccents = tileAccents;
		}

		public string Id { get; }
		public string Label { get; }
		public Color Overlay { get; }
		public Color CardSurface { get; }
		public Color CardSurfaceAlt { get; }
		public Color CardSurfaceInset { get; }
		public Color ChipSurface { get; }
		public Color Separator { get; }
		public Color SceneTint { get; }
		public Color SceneHighlight { get; }
		public string GlobalGradientKey { get; }
		public Color Accent { get; }
		public Color ShowcaseAccent { get; }
		public Color RecentAccent { get; }
		public Color FriendsAccent { get; }
		public Color[] TileAccents { get; }
	}

	public sealed class ResolvedStyle
	{
		public ResolvedStyle(AccentPreset accent, AvatarFramePreset avatarFrame, BackgroundPreset background)
		{
			Accent = accent;
			AvatarFrame = avatarFrame;
			Background = background;
		}

		public AccentPreset Accent { get; }
		public AvatarFramePreset AvatarFrame { get; }
		public BackgroundPreset Background { get; }
	}

	public static readonly IReadOnlyList<AccentPreset> Accents = new[]
	{
		new AccentPreset("sky", "Sky", new Color(0.47f, 0.84f, 1f, 1f)),
		new AccentPreset("mint", "Mint", new Color(0.57f, 0.97f, 0.79f, 1f)),
		new AccentPreset("rose", "Rose", new Color(1f, 0.73f, 0.86f, 1f)),
		new AccentPreset("gold", "Gold", new Color(1f, 0.83f, 0.51f, 1f)),
		new AccentPreset("violet", "Violet", new Color(0.72f, 0.64f, 0.92f, 1f))
	};

	public static readonly IReadOnlyList<AvatarFramePreset> AvatarFrames = new[]
	{
		new AvatarFramePreset("rounded", "Rounded", 0.985f, 0.12f, 0.026f, 18, 132f, 5, 1),
		new AvatarFramePreset("sharp", "Sharp", 0.990f, 0.035f, 0.022f, 8, 132f, 5, 1),
		new AvatarFramePreset("circle", "Circle", 0.960f, 0.50f, 0.026f, 999, 132f, 5, 1)
	};

	public static readonly IReadOnlyList<BackgroundPreset> Backgrounds = new[]
	{
		new BackgroundPreset(
			"original",
			"Original",
			new Color(0.066f, 0.047f, 0.120f, 0.78f),
			new Color(0.060f, 0.052f, 0.102f, 0.94f),
			new Color(0.082f, 0.064f, 0.132f, 0.95f),
			new Color(0.112f, 0.092f, 0.170f, 0.96f),
			new Color(0.132f, 0.104f, 0.190f, 0.94f),
			new Color(0.70f, 0.50f, 0.90f, 0.26f),
			new Color(0.066f, 0.047f, 0.120f, 0.80f),
			new Color(0.70f, 0.50f, 0.90f, 0.34f),
			"ProfileOriginal",
			new Color(0.72f, 0.56f, 0.96f, 1f),
			new Color(0.47f, 0.84f, 1.00f, 1f),
			new Color(0.57f, 0.97f, 0.79f, 1f),
			new Color(1.00f, 0.73f, 0.86f, 1f),
			new[]
			{
				new Color(0.32f, 0.64f, 0.94f, 1f),
				new Color(0.74f, 0.56f, 0.86f, 1f),
				new Color(0.86f, 0.44f, 0.72f, 1f),
				new Color(0.52f, 0.78f, 0.58f, 1f)
			}),
		new BackgroundPreset(
			"starry-night",
			"Starry Night",
			new Color(0.018f, 0.040f, 0.105f, 0.78f),
			new Color(0.034f, 0.058f, 0.128f, 0.95f),
			new Color(0.046f, 0.078f, 0.160f, 0.96f),
			new Color(0.070f, 0.104f, 0.186f, 0.97f),
			new Color(0.080f, 0.116f, 0.204f, 0.94f),
			new Color(0.92f, 0.72f, 0.26f, 0.24f),
			new Color(0.024f, 0.052f, 0.132f, 0.84f),
			new Color(0.98f, 0.78f, 0.28f, 0.32f),
			"ProfileStarryNight",
			new Color(0.96f, 0.74f, 0.24f, 1f),
			new Color(0.88f, 0.67f, 0.24f, 1f),
			new Color(0.30f, 0.62f, 0.90f, 1f),
			new Color(0.50f, 0.56f, 0.86f, 1f),
			new[]
			{
				new Color(0.86f, 0.64f, 0.22f, 1f),
				new Color(0.26f, 0.56f, 0.86f, 1f),
				new Color(0.14f, 0.28f, 0.62f, 1f),
				new Color(0.74f, 0.48f, 0.16f, 1f)
			}),
		new BackgroundPreset(
			"earthbound",
			"Earthbound",
			new Color(0.030f, 0.050f, 0.116f, 0.78f),
			new Color(0.044f, 0.062f, 0.138f, 0.95f),
			new Color(0.062f, 0.078f, 0.164f, 0.96f),
			new Color(0.082f, 0.090f, 0.190f, 0.97f),
			new Color(0.086f, 0.104f, 0.202f, 0.94f),
			new Color(0.50f, 0.54f, 1.00f, 0.24f),
			new Color(0.034f, 0.056f, 0.128f, 0.84f),
			new Color(0.60f, 0.32f, 0.96f, 0.36f),
			"ProfileEarthbound",
			new Color(0.58f, 0.56f, 1.00f, 1f),
			new Color(0.44f, 0.80f, 0.98f, 1f),
			new Color(0.66f, 0.44f, 1.00f, 1f),
			new Color(0.48f, 0.88f, 0.78f, 1f),
			new[]
			{
				new Color(0.28f, 0.58f, 0.96f, 1f),
				new Color(0.52f, 0.36f, 0.96f, 1f),
				new Color(0.30f, 0.78f, 0.82f, 1f),
				new Color(0.70f, 0.30f, 0.92f, 1f)
			}),
		new BackgroundPreset(
			"rusty",
			"Rusty",
			new Color(0.060f, 0.028f, 0.062f, 0.82f),
			new Color(0.070f, 0.052f, 0.092f, 0.96f),
			new Color(0.092f, 0.060f, 0.112f, 0.96f),
			new Color(0.116f, 0.068f, 0.126f, 0.97f),
			new Color(0.118f, 0.078f, 0.140f, 0.94f),
			new Color(0.93f, 0.12f, 0.38f, 0.26f),
			new Color(0.040f, 0.032f, 0.075f, 0.84f),
			new Color(0.94f, 0.08f, 0.34f, 0.42f),
			"ProfileRusty",
			new Color(0.90f, 0.08f, 0.32f, 1f),
			new Color(0.94f, 0.12f, 0.38f, 1f),
			new Color(0.30f, 0.42f, 0.78f, 1f),
			new Color(0.10f, 0.14f, 0.30f, 1f),
			new[]
			{
				new Color(0.93f, 0.10f, 0.36f, 1f),
				new Color(0.32f, 0.42f, 0.82f, 1f),
				new Color(0.12f, 0.15f, 0.32f, 1f),
				new Color(0.68f, 0.08f, 0.28f, 1f)
			})
	};

	public static ResolvedStyle Resolve(string? accentId, string? avatarFrameId, string? backgroundId)
	{
		return new ResolvedStyle(
			FindAccent(accentId),
			FindAvatarFrame(avatarFrameId),
			FindBackground(backgroundId));
	}

	public static AccentPreset FindAccent(string? id)
	{
		return FindById(Accents, id, DefaultAccentId);
	}

	public static AvatarFramePreset FindAvatarFrame(string? id)
	{
		return FindById(AvatarFrames, id, DefaultAvatarFrameId);
	}

	public static BackgroundPreset FindBackground(string? id)
	{
		return FindById(Backgrounds, id, DefaultBackgroundId);
	}

	public static int FindAccentIndex(string? id)
	{
		return FindIndex(Accents, FindAccent(id).Id);
	}

	public static int FindAvatarFrameIndex(string? id)
	{
		return FindIndex(AvatarFrames, FindAvatarFrame(id).Id);
	}

	public static int FindBackgroundIndex(string? id)
	{
		return FindIndex(Backgrounds, FindBackground(id).Id);
	}

	private static T FindById<T>(IReadOnlyList<T> presets, string? id, string defaultId)
		where T : class
	{
		var normalized = string.IsNullOrWhiteSpace(id) ? defaultId : id.Trim();
		var preset = presets.FirstOrDefault(preset => string.Equals(GetId(preset), normalized, StringComparison.OrdinalIgnoreCase));
		return preset ?? presets.First(preset => string.Equals(GetId(preset), defaultId, StringComparison.OrdinalIgnoreCase));
	}

	private static int FindIndex<T>(IReadOnlyList<T> presets, string id)
		where T : class
	{
		for (int index = 0; index < presets.Count; index++)
		{
			if (string.Equals(GetId(presets[index]), id, StringComparison.OrdinalIgnoreCase))
				return index;
		}

		return 0;
	}

	private static string GetId<T>(T preset)
	{
		return preset switch
		{
			AccentPreset accent => accent.Id,
			AvatarFramePreset frame => frame.Id,
			BackgroundPreset background => background.Id,
			_ => string.Empty
		};
	}
}
