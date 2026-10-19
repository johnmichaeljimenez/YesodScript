using System.Diagnostics;
using Raylib_cs;
using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Examples;

public class Assets : IDisposable
{
	public static Assets Instance { get; private set; }
	private readonly Dictionary<string, Texture2D> sprites = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Sound> sounds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Font> fonts = new(StringComparer.InvariantCultureIgnoreCase);

	public Assets()
	{
		Instance = this;

		fonts[""] = Raylib.GetFontDefault();
	}

	public void LoadSprite(string id, string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException($"Sprite file not found for id '{id}': '{path}'", path);

		var texture = Raylib.LoadTexture(path);
		var filter = GameContent.PixelArt ? TextureFilter.Point : TextureFilter.Bilinear;
		Raylib.SetTextureFilter(texture, filter);
		sprites[id] = texture;
	}

	public Texture2D GetSprite(string id)
	{
		if (!sprites.TryGetValue(id, out var sprite))
			throw new InvalidOperationException($"Sprite '{id}' not found. Load it first or check the asset id.");

		return sprite;
	}

	public void LoadSound(string id, string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException($"Sound file not found for id '{id}': '{path}'", path);

		sounds[id] = Raylib.LoadSound(path);
	}

	public Sound GetSound(string id)
	{
		if (!sounds.TryGetValue(id, out var sound))
			throw new InvalidOperationException($"Sound '{id}' not found. Load it first or check the asset id.");

		return sound;
	}

	public void LoadFont(string id, string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException($"Font file not found for id '{id}': '{path}'", path);

		var f = Raylib.LoadFontEx(path, 20, null, 0);
		if (id.Equals(GameContent.DefaultFontName, StringComparison.InvariantCultureIgnoreCase))
		{
			fonts[""] = f;
		}

		Raylib.SetTextureFilter(f.Texture, GameContent.PixelArt ? TextureFilter.Point : TextureFilter.Bilinear);
		fonts[id] = f;
	}

	public Font GetFont(string id)
	{
		if (!fonts.TryGetValue(id, out var font))
			throw new InvalidOperationException($"Font '{id}' not found. Load it first or check the asset id.");

		return font;
	}

	public void Dispose()
	{
		foreach (var i in sprites.Values)
		{
			Raylib.UnloadTexture(i);
		}

		sprites.Clear();

		foreach (var i in sounds.Values)
		{
			Raylib.UnloadSound(i);
		}

		sounds.Clear();

		fonts.Remove("");	//no need to unload this as it's either default font, or the existing keyed one
		foreach (var i in fonts.Values)
		{
			Raylib.UnloadFont(i);
		}

		fonts.Clear();
	}
}

public class LoadAssets : GameCommand
{
	[CommandParameter]
	private string relativePath;

	private Dictionary<string, string> spritePaths;
	private List<string> spriteKeys;
	private Dictionary<string, string> sfxPaths;
	private List<string> sfxKeys;
	private Dictionary<string, string> fontPaths;
	private List<string> fontKeys;

	public override void OnEnter()
	{
		spritePaths = DirectoryExtractor.GetPaths(relativePath, "png");
		spriteKeys = spritePaths.Keys.ToList();

		sfxPaths = DirectoryExtractor.GetPaths(relativePath, "wav", "ogg");
		sfxKeys = sfxPaths.Keys.ToList();

		fontPaths = DirectoryExtractor.GetPaths(relativePath, "ttf", "otf");
		fontKeys = fontPaths.Keys.ToList();
	}

	public override bool OnTick(float deltaTime)
	{
		if (spriteKeys.Count > 0)
		{
			var key = spriteKeys[0];
			Assets.Instance.LoadSprite(key, spritePaths[key]);
			spriteKeys.RemoveAt(0);

			return false;
		}

		if (sfxKeys.Count > 0)
		{
			var key = sfxKeys[0];
			Assets.Instance.LoadSound(key, sfxPaths[key]);
			sfxKeys.RemoveAt(0);

			return false;
		}

		if (fontKeys.Count > 0)
		{
			var key = fontKeys[0];
			Assets.Instance.LoadFont(key, fontPaths[key]);
			fontKeys.RemoveAt(0);

			return false;
		}

		return true;
	}
}