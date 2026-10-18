using System.Numerics;
using System.Text.Json;

namespace YesodScript.Examples;

public static class GameContent
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
	};

	public static string GameId { get; private set; } = "pong";
	public static string Title { get; private set; } = "YesodScript";
	public static string Root { get; private set; } = AppContext.BaseDirectory;
	public static Vector2 VirtualResolution { get; private set; } = new(1280, 720);

	public static bool PixelArt { get; private set; }

	public static string CorePath { get; private set; } = Path.Combine(AppContext.BaseDirectory, "core");
	public static string ScriptsPath => Path.Combine(Root, "scripts");
	public static string AssetsPath => Path.Combine(Root, "assets");
	public static string? ItchTarget { get; private set; }

	public static void Initialize(string[]? args = null)
	{
		var baseDir = AppContext.BaseDirectory;
		var gameId = ResolveGameId(args, baseDir);
		GameId = gameId;

		Root = ResolveRoot(baseDir, gameId);
		CorePath = ResolveCorePath(baseDir, Root);
		Directory.SetCurrentDirectory(Root);

		ApplyManifest(Path.Combine(Root, "game.json"), gameId);
	}

	private static string ResolveGameId(string[]? args, string baseDir)
	{
		if (args != null)
		{
			for (var i = 0; i < args.Length; i++)
			{
				var arg = args[i];
				if (arg.Equals("--game", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
					return NormalizeId(args[i + 1]);

				if (arg.StartsWith("--game=", StringComparison.OrdinalIgnoreCase))
					return NormalizeId(arg["--game=".Length..]);
			}
		}

		var gamesRoot = Path.Combine(baseDir, "game");
		if (Directory.Exists(gamesRoot))
		{
			var dirs = Directory.GetDirectories(gamesRoot)
				.Select(Path.GetFileName)
				.Where(name => !string.IsNullOrEmpty(name))
				.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
				.ToList();

			if (dirs.Count == 1)
				return dirs[0]!;
		}

		return "pong";
	}

	private static string ResolveRoot(string baseDir, string gameId)
	{
		var nested = Path.Combine(baseDir, "game", gameId);
		if (IsContentRoot(nested))
			return Path.GetFullPath(nested);

		if (IsContentRoot(baseDir))
			return Path.GetFullPath(baseDir);

		var projectGame = FindProjectGameRoot(gameId);
		if (projectGame != null)
			return projectGame;

		throw new DirectoryNotFoundException(
			$"Game content not found for '{gameId}'. Expected '{nested}' or flat assets/scripts under '{baseDir}'.");
	}

	private static string? FindProjectGameRoot(string gameId)
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
		{
			var candidate = Path.Combine(dir.FullName, "game", gameId);
			if (IsContentRoot(candidate))
				return Path.GetFullPath(candidate);
		}

		return null;
	}

	private static string ResolveCorePath(string baseDir, string contentRoot)
	{
		var besideBase = Path.Combine(baseDir, "core");
		if (Directory.Exists(besideBase))
			return Path.GetFullPath(besideBase);

		var besideContent = Path.Combine(contentRoot, "core");
		if (Directory.Exists(besideContent) && IsSamePath(contentRoot, baseDir))
			return Path.GetFullPath(besideContent);

		var projectCore = FindProjectCore(baseDir) ?? FindProjectCore(contentRoot);
		if (projectCore != null)
			return projectCore;

		throw new DirectoryNotFoundException(
			$"Shared core/ stdlib not found. Expected '{besideBase}' next to the app, or a project core/ folder.");
	}

	private static string? FindProjectCore(string startPath)
	{
		var dir = new DirectoryInfo(startPath);
		for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
		{
			var core = Path.Combine(dir.FullName, "core");
			var games = Path.Combine(dir.FullName, "game");
			if (Directory.Exists(core) && Directory.Exists(games))
				return Path.GetFullPath(core);
		}

		return null;
	}

	private static bool IsSamePath(string a, string b)
	{
		return string.Equals(
			Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsContentRoot(string path)
	{
		return Directory.Exists(Path.Combine(path, "scripts"))
			|| Directory.Exists(Path.Combine(path, "assets"));
	}

	private static void ApplyManifest(string manifestPath, string gameId)
	{
		Title = ToTitleCaseId(gameId);
		ItchTarget = null;
		PixelArt = false;

		if (!File.Exists(manifestPath))
			return;

		try
		{
			var json = File.ReadAllText(manifestPath);
			var manifest = JsonSerializer.Deserialize<GameManifest>(json, JsonOptions);
			if (manifest == null)
				return;

			if (!string.IsNullOrWhiteSpace(manifest.Title))
				Title = manifest.Title.Trim();
			if (!string.IsNullOrWhiteSpace(manifest.Id))
				GameId = NormalizeId(manifest.Id);
			if (!string.IsNullOrWhiteSpace(manifest.Itch))
				ItchTarget = manifest.Itch.Trim();

			if (manifest.PixelArt != null)
				PixelArt = manifest.PixelArt.Value;

			if (manifest.VirtualWidth != null && manifest.VirtualHeight != null)
			{
				if (manifest.VirtualWidth > 0 &&
					manifest.VirtualHeight > 0 &&
					manifest.VirtualWidth <= 1920 &&
					manifest.VirtualHeight <= 1080)
				{
					VirtualResolution = new(manifest.VirtualWidth.Value, manifest.VirtualHeight.Value);
				}
			}
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to read game manifest '{manifestPath}'.", ex);
		}
	}

	private static string NormalizeId(string id)
	{
		id = id.Trim().Replace('\\', '/').Trim('/');
		if (id.Contains('/') || id.Contains(".."))
			throw new ArgumentException($"Invalid game id '{id}'.");
		return id.ToLowerInvariant();
	}

	private static string ToTitleCaseId(string id)
	{
		if (string.IsNullOrEmpty(id))
			return "YesodScript";
		return char.ToUpperInvariant(id[0]) + id[1..];
	}

	private sealed class GameManifest
	{
		public string? Id { get; set; }
		public string? Title { get; set; }
		public string? Icon { get; set; }
		public string? Itch { get; set; }
		public float? VirtualWidth { get; set; } = 1280;
		public float? VirtualHeight { get; set; } = 720;
		
		public bool? PixelArt { get; set; }
	}
}
