using Raylib_cs;
using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Examples;

public class Assets : IDisposable
{
	public static Assets Instance { get; private set; }
	private readonly Dictionary<string, Texture2D> sprites = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Sound> sounds = new(StringComparer.InvariantCultureIgnoreCase);

	public Assets()
	{
		Instance = this;
	}

	public void LoadSprite(string id, string path)
	{
		sprites[id] = Raylib.LoadTexture(path);
	}

	public Texture2D GetSprite(string id)
	{
		return sprites[id];
	}

	public void LoadSound(string id, string path)
	{
		sounds[id] = Raylib.LoadSound(path);
	}

	public Sound GetSound(string id)
	{
		return sounds[id];
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
	}
}

public class LoadSprites : GameCommand
{
	[CommandParameter]
	private string relativePath;

	private Dictionary<string, string> paths;
	private List<string> keys;

	public override void OnEnter()
	{
		paths = DirectoryExtractor.GetPaths(relativePath, "png");
		keys = paths.Keys.ToList();
	}

	public override bool OnTick(float deltaTime)
	{
		if (keys.Count > 0)
		{
			var key = keys[0];
			Assets.Instance.LoadSprite(key, paths[key]);
			keys.RemoveAt(0);

			return false;
		}

		return true;
	}
}

public class LoadSounds : GameCommand
{
	[CommandParameter]
	private string relativePath;

	private Dictionary<string, string> paths;
	private List<string> keys;

	public override void OnEnter()
	{
		paths = DirectoryExtractor.GetPaths(relativePath, "wav");
		keys = paths.Keys.ToList();
	}

	public override bool OnTick(float deltaTime)
	{
		if (keys.Count > 0)
		{
			var key = keys[0];
			Assets.Instance.LoadSound(key, paths[key]);
			keys.RemoveAt(0);

			return false;
		}

		return true;
	}
}