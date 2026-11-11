using System.Diagnostics;
using Raylib_cs;
using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Runtime;

public class Assets : IDisposable
{
	public static Assets Instance { get; private set; }
	private readonly Dictionary<string, Texture2D> sprites = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Sound> sounds = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Font> fonts = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Model> models = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, ModelAnimation[]> modelAnimations = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, IntPtr> modelAnimationPtrs = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, int> modelAnimationCounts = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly Dictionary<string, Shader> shaders = new(StringComparer.InvariantCultureIgnoreCase);
	private readonly List<ICustomAssetLoader> customAssetLoaders = [];
	private readonly Dictionary<Type, ICustomAssetLoader> customAssetLoadersByType = [];
	private readonly Dictionary<string, ICustomAssetLoader> customAssetLoadersByExtension = new(StringComparer.InvariantCultureIgnoreCase);

	public Assets()
	{
		Instance = this;

		fonts[""] = Raylib.GetFontDefault();
	}

	public void RegisterAssetLoader<T>(Func<string, T> load, params string[] extensions) where T : class
	{
		RegisterAssetLoader(load, null, extensions);
	}

	public void RegisterAssetLoader<T>(Func<string, T> load, Action<T>? unload, params string[] extensions) where T : class
	{
		ArgumentNullException.ThrowIfNull(load);
		ArgumentNullException.ThrowIfNull(extensions);

		if (extensions.Length == 0)
			throw new ArgumentException("At least one file extension must be registered.", nameof(extensions));

		if (customAssetLoadersByType.ContainsKey(typeof(T)))
			throw new InvalidOperationException($"An asset loader for '{typeof(T).FullName}' is already registered.");

		var normalizedExtensions = new List<string>();
		foreach (var extension in extensions)
		{
			var normalized = extension?.Trim().TrimStart('.');
			if (string.IsNullOrWhiteSpace(normalized) || Path.GetFileName(normalized) != normalized)
				throw new ArgumentException($"Invalid asset file extension: '{extension}'.", nameof(extensions));

			if (normalizedExtensions.Contains(normalized, StringComparer.InvariantCultureIgnoreCase))
				continue;

			if (customAssetLoadersByExtension.ContainsKey(normalized))
				throw new InvalidOperationException($"An asset loader for the '.{normalized}' extension is already registered.");

			normalizedExtensions.Add(normalized);
		}

		if (normalizedExtensions.Count == 0)
			throw new ArgumentException("At least one valid file extension must be registered.", nameof(extensions));

		var assetLoader = new CustomAssetLoader<T>(normalizedExtensions, load, unload);
		customAssetLoaders.Add(assetLoader);
		customAssetLoadersByType.Add(typeof(T), assetLoader);
		foreach (var extension in normalizedExtensions)
			customAssetLoadersByExtension.Add(extension, assetLoader);
	}

	public T GetAsset<T>(string id) where T : class
	{
		if (TryGetAsset<T>(id, out var asset))
			return asset!;

		throw new InvalidOperationException($"Asset '{id}' of type '{typeof(T).FullName}' not found. Load it first or check the asset id.");
	}

	public bool TryGetAsset<T>(string id, out T? asset) where T : class
	{
		if (customAssetLoadersByType.TryGetValue(typeof(T), out var loader) &&
			loader.TryGet(id, out var value))
		{
			asset = (T)value!;
			return true;
		}

		asset = default;
		return false;
	}

	internal Dictionary<string, (string Path, Action<string, string> Load)> GetCustomAssetPaths(string basePath)
	{
		var paths = new Dictionary<string, (string Path, Action<string, string> Load)>(StringComparer.InvariantCultureIgnoreCase);
		foreach (var loader in customAssetLoaders)
		{
			foreach (var (key, path) in DirectoryExtractor.GetPaths(basePath, loader.Extensions))
			{
				if (!paths.ContainsKey(key))
					paths.Add(key, (path, loader.Load));
			}
		}

		return paths;
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

		var f = Raylib.LoadFontEx(path, 64, null, 0);
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

	public void LoadModel(string id, string path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			throw new FileNotFoundException($"Model file not found for id '{id}': '{path}'", path);

		var model = Raylib.LoadModel(path);
		NormalizeModelMaterials(model);
		models[id] = model;
		LoadModelAnimations(id, path);
	}

	/// <summary>
	/// Sketchfab (and similar) GLBs often ship black baseColorFactor with the
	/// actual unlit colors in the emissive map. Raylib's default model shader
	/// multiplies albedo texture by albedo color and ignores emission, so those
	/// models draw as black silhouettes. Promote emission → albedo when needed.
	/// </summary>
	private static unsafe void NormalizeModelMaterials(Model model)
	{
		if (model.MaterialCount <= 0 || model.Materials == null)
			return;

		for (var i = 0; i < model.MaterialCount; i++)
		{
			var maps = model.Materials[i].Maps;
			if (maps == null)
				continue;

			ref var albedo = ref maps[(int)MaterialMapIndex.Albedo];
			ref var emission = ref maps[(int)MaterialMapIndex.Emission];

			var albedoBlack = albedo.Color.R == 0 && albedo.Color.G == 0 && albedo.Color.B == 0;
			if (!albedoBlack)
				continue;

			if (emission.Texture.Id != 0)
				albedo.Texture = emission.Texture;

			albedo.Color = Color.White;
		}
	}

	public Model GetModel(string id)
	{
		if (!models.TryGetValue(id, out var model))
			throw new InvalidOperationException($"Model '{id}' not found. Load it first or check the asset id.");

		return model;
	}

	public bool TryGetModelAnimation(string modelId, string animName, out ModelAnimation animation)
	{
		animation = default;

		if (string.IsNullOrWhiteSpace(animName))
			return false;

		if (!modelAnimations.TryGetValue(modelId, out var anims) || anims.Length == 0)
			return false;

		foreach (var anim in anims)
		{
			if (anim.NameToString().Equals(animName, StringComparison.InvariantCultureIgnoreCase))
			{
				animation = anim;
				return true;
			}
		}

		return false;
	}

	public IReadOnlyList<string> GetModelAnimationNames(string modelId)
	{
		if (!modelAnimations.TryGetValue(modelId, out var anims) || anims.Length == 0)
			return Array.Empty<string>();

		var names = new string[anims.Length];
		for (var i = 0; i < anims.Length; i++)
			names[i] = anims[i].NameToString();

		return names;
	}

	private unsafe void LoadModelAnimations(string id, string path)
	{
		var count = 0;
		var ptr = Raylib.LoadModelAnimations(path, ref count);
		if (ptr == null || count <= 0)
		{
			modelAnimations[id] = Array.Empty<ModelAnimation>();
			return;
		}

		var anims = new ModelAnimation[count];
		for (var i = 0; i < count; i++)
			anims[i] = ptr[i];

		modelAnimations[id] = anims;
		modelAnimationPtrs[id] = (IntPtr)ptr;
		modelAnimationCounts[id] = count;
	}

	private unsafe void UnloadAllModelAnimations()
	{
		foreach (var pair in modelAnimationPtrs)
		{
			if (!modelAnimationCounts.TryGetValue(pair.Key, out var count) || count <= 0)
				continue;

			var ptr = (ModelAnimation*)pair.Value.ToPointer();
			if (ptr == null)
				continue;

			Raylib.UnloadModelAnimations(ptr, count);
		}

		modelAnimationPtrs.Clear();
		modelAnimationCounts.Clear();
		modelAnimations.Clear();
	}

	public void LoadShader(string id, string vsPath, string fsPath)
	{
		if (!File.Exists(vsPath) || !File.Exists(fsPath))
			throw new FileNotFoundException($"Shader files missing for '{id}'");

		shaders[id] = Raylib.LoadShader(vsPath, fsPath);
	}

	public Shader GetShader(string id)
	{
		return shaders[id];
	}

	public void Dispose()
	{
		foreach (var i in shaders)
		{
			Raylib.UnloadShader(i.Value);
		}

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

		fonts.Remove("");   //no need to unload this as it's either default font, or the existing keyed one
		foreach (var i in fonts.Values)
		{
			Raylib.UnloadFont(i);
		}

		fonts.Clear();

		UnloadAllModelAnimations();

		foreach (var i in models.Values)
		{
			Raylib.UnloadModel(i);
		}

		models.Clear();

		foreach (var loader in customAssetLoaders)
			loader.Dispose();
	}

	private interface ICustomAssetLoader
	{
		string[] Extensions { get; }
		void Load(string id, string path);
		bool TryGet(string id, out object? asset);
		void Dispose();
	}

	private sealed class CustomAssetLoader<T> : ICustomAssetLoader where T : class
	{
		private readonly Func<string, T> load;
		private readonly Action<T> unload;
		private readonly Dictionary<string, T> assets = new(StringComparer.InvariantCultureIgnoreCase);

		public string[] Extensions { get; }

		public CustomAssetLoader(IReadOnlyList<string> extensions, Func<string, T> load, Action<T>? unload)
		{
			Extensions = extensions.ToArray();
			this.load = load;
			this.unload = unload ?? DisposeIfNeeded;
		}

		public void Load(string id, string path)
		{
			var asset = load(path);
			if (assets.TryGetValue(id, out var previous))
			{
				assets[id] = asset;
				unload(previous);
			}
			else
			{
				assets.Add(id, asset);
			}
		}

		public bool TryGet(string id, out object? asset)
		{
			if (assets.TryGetValue(id, out var value))
			{
				asset = value;
				return true;
			}

			asset = null;
			return false;
		}

		public void Dispose()
		{
			foreach (var asset in assets.Values)
				unload(asset);

			assets.Clear();
		}

		private static void DisposeIfNeeded(T asset)
		{
			if (asset is IDisposable disposable)
				disposable.Dispose();
		}
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
	private Dictionary<string, string> modelPaths;
	private List<string> modelKeys;
	private Dictionary<string, (string vs, string fs)> shaderPairs;
	private List<string> shaderKeys;
	private Dictionary<string, (string Path, Action<string, string> Load)> customAssetPaths = new(StringComparer.InvariantCultureIgnoreCase);
	private List<string> customAssetKeys = [];

	public override void OnEnter()
	{
		spritePaths = DirectoryExtractor.GetPaths(relativePath, "png");
		spriteKeys = spritePaths.Keys.ToList();

		sfxPaths = DirectoryExtractor.GetPaths(relativePath, "wav", "ogg");
		sfxKeys = sfxPaths.Keys.ToList();

		fontPaths = DirectoryExtractor.GetPaths(relativePath, "ttf", "otf");
		fontKeys = fontPaths.Keys.ToList();

		modelPaths = DirectoryExtractor.GetPaths(relativePath, "glb");
		modelKeys = modelPaths.Keys.ToList();

		var vsPaths = DirectoryExtractor.GetPaths(relativePath, "vs");
		var fsPaths = DirectoryExtractor.GetPaths(relativePath, "fs");

		shaderPairs = new Dictionary<string, (string vs, string fs)>(StringComparer.InvariantCultureIgnoreCase);
		foreach (var (key, vsPath) in vsPaths)
		{
			if (fsPaths.TryGetValue(key, out var fsPath))
				shaderPairs[key] = (vsPath, fsPath);
		}
		shaderKeys = shaderPairs.Keys.ToList();

		customAssetPaths = Assets.Instance.GetCustomAssetPaths(relativePath);
		customAssetKeys = customAssetPaths.Keys.ToList();
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

		if (modelKeys.Count > 0)
		{
			var key = modelKeys[0];
			Assets.Instance.LoadModel(key, modelPaths[key]);
			modelKeys.RemoveAt(0);

			return false;
		}
		
		if (shaderKeys.Count > 0)
		{
			var key = shaderKeys[0];
			var pair = shaderPairs[key];
			Assets.Instance.LoadShader(key, pair.vs, pair.fs);
			shaderKeys.RemoveAt(0);
			return false;
		}

		if (customAssetKeys.Count > 0)
		{
			var key = customAssetKeys[0];
			var asset = customAssetPaths[key];
			asset.Load(key, asset.Path);
			customAssetKeys.RemoveAt(0);
			return false;
		}

		return true;
	}
}