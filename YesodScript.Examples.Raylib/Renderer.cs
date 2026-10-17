using System.Numerics;
using Raylib_cs;
using Tween;
using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Examples;

public class Renderer : IDisposable
{
	public static readonly IReadOnlyDictionary<string, PathMember> RenderDataMembers = PathMemberCache.Build<RenderData>();
	public static readonly IReadOnlyDictionary<string, PathMember> CameraMembers = PathMemberCache.Build<Camera2D>();
	public static Renderer Instance { get; private set; }

	private int renderID = 1;   //0 means stale default data
	private readonly Dictionary<int, RenderData> renderList = new();
	private readonly List<RenderData> sortedRenderList = new();
	private bool sortDirty;

	const int scale = 2;
	const int VirtualWidth = 1280 / scale;
	const int VirtualHeight = 720 / scale;

	private RenderTexture2D rt;

	public Camera2D Camera = new()
	{
		Offset = new Vector2(VirtualWidth * 0.5f, VirtualHeight * 0.5f),
		Zoom = 1f
	};

	public Renderer()
	{
		Instance = this;

		rt = Raylib.LoadRenderTexture(VirtualWidth, VirtualHeight);
		renderID = 0;
	}

	public void Dispose()
	{
		Raylib.UnloadRenderTexture(rt);
	}

	public void ClearRenderData(string filter = "*")
	{
		if (filter.Equals("*"))
		{
			renderList.Clear();
			sortedRenderList.Clear();
			sortDirty = false;
			return;
		}

		foreach (var i in renderList.Values.ToArray())
		{
			if (Utils.IsMatchWildcard(i.Group, filter))
			{
				renderList.Remove(i.ID);
				sortedRenderList.Remove(i);
			}
		}
	}

	public void SetRenderDataState(bool state, string filter = "*")
	{
		foreach (var i in renderList)
		{
			if (Utils.IsMatchWildcard(i.Value.Group, filter))
				i.Value.Disabled = !state;
		}
	}

	public void RemoveRenderData(int id)
	{
		if (!renderList.Remove(id, out var data))
			return;

		sortedRenderList.Remove(data);
	}

	public void SortRenderers()
	{
		sortDirty = true;
	}

	private void EnsureSorted()
	{
		if (!sortDirty)
			return;

		sortedRenderList.Sort((a, b) => a.Depth.CompareTo(b.Depth));
		sortDirty = false;
	}

	public RenderData CreateRenderData(int x, int y)
	{
		var newID = renderID++;

		if (renderList.ContainsKey(newID))
			throw new InvalidOperationException($"RenderData with ID '{newID}' already exists.");

		var r = new RenderData();
		r.ID = newID;
		r.X = x;
		r.Y = y;
		r.prevX = x;
		r.prevY = y;

		renderList[r.ID] = r;
		sortedRenderList.Add(r);
		SortRenderers();
		return r;
	}

	public void SnapshotPosition()
	{
		foreach (var i in sortedRenderList)
		{
			i.prevX = i.X;
			i.prevY = i.Y;
		}
	}

	public RenderData GetRenderData(int id)
	{
		if (!renderList.TryGetValue(id, out var data))
			throw new InvalidOperationException($"RenderData id '{id}' not found.");

		return data;
	}

	public Vector2 GetVirtualMousePosition()
	{
		var windowWidth = Raylib.GetScreenWidth();
		var windowHeight = Raylib.GetScreenHeight();

		var currentScale = MathF.Min((float)windowWidth / VirtualWidth, (float)windowHeight / VirtualHeight);
		var offsetX = (windowWidth - (VirtualWidth * currentScale)) * 0.5f;
		var offsetY = (windowHeight - (VirtualHeight * currentScale)) * 0.5f;

		var mouseScreenPos = Raylib.GetMousePosition();

		var virtualMouseX = (mouseScreenPos.X - offsetX) / currentScale;
		var virtualMouseY = (mouseScreenPos.Y - offsetY) / currentScale;

		return new Vector2(virtualMouseX, virtualMouseY);
	}

	public void Draw()
	{
		var windowWidth = Raylib.GetScreenWidth();
		var windowHeight = Raylib.GetScreenHeight();

		var scale = MathF.Min((float)windowWidth / VirtualWidth, (float)windowHeight / VirtualHeight);

		var destRect = new Rectangle(
			(windowWidth - (VirtualWidth * scale)) * 0.5f,
			(windowHeight - (VirtualHeight * scale)) * 0.5f,
			VirtualWidth * scale,
			VirtualHeight * scale
		);

		var sourceRect = new Rectangle(0, 0, VirtualWidth, -VirtualHeight);
		Raylib.BeginTextureMode(rt);
		{
			Raylib.ClearBackground(Color.RayWhite);

			var alpha = Math.Clamp(RaylibHost.Instance.HzAlpha, 0f, 1f);
			EnsureSorted();

			Raylib.BeginMode2D(Camera);
			foreach (var item in sortedRenderList)
			{
				if (item.Disabled || !item.World)
					continue;
					
				DrawItem(item, alpha);
			}
			Raylib.EndMode2D();

			foreach (var item in sortedRenderList)
			{
				if (item.Disabled || item.World)
					continue;

				DrawItem(item, alpha);
			}
		}
		Raylib.EndTextureMode();

		Raylib.BeginDrawing();
		{
			Raylib.ClearBackground(Color.Black);
			Raylib.DrawTexturePro(rt.Texture, sourceRect, destRect, Vector2.Zero, 0.0f, Color.White);
		}
		Raylib.EndDrawing();
	}

	private void DrawItem(RenderData i, float alpha)
	{
		var maxDist = 100f;
		var drawX = Math.Abs(i.X - i.prevX) > maxDist ? i.X : i.prevX + (i.X - i.prevX) * alpha;
		var drawY = Math.Abs(i.Y - i.prevY) > maxDist ? i.Y : i.prevY + (i.Y - i.prevY) * alpha;

		if (i.Sprite.Id != 0)
		{
			var size = i.Sprite.Dimensions;
			var pos = new Vector2(drawX, drawY);

			var c1 = new Color(i.Tint.R, i.Tint.G, i.Tint.B, (byte)Math.Clamp(i.Tint.A * i.Opacity, 0f, 255f));
			Raylib.DrawTextureV(i.Sprite, pos - (size * 0.5f), c1);
		}

		if (string.IsNullOrWhiteSpace(i.Text))
			return;

		var fontSize = i.FontSize;
		var c2 = new Color(i.Tint.R, i.Tint.G, i.Tint.B, (byte)Math.Clamp(i.Tint.A * i.Opacity, 0f, 255f));
		var textWidth = Raylib.MeasureText(i.Text, fontSize);
		var textX = (int)drawX - (textWidth / 2);
		var textY = (int)drawY - (fontSize / 2);
		Raylib.DrawText(i.Text, textX, textY, fontSize, c2);
	}
}

public class RenderData : IDisposable
{
	public int ID;
	public bool Disabled;

	public float X;
	public float Y;
	public float prevX, prevY;
	public string Text;
	public string Group = "";
	public float Opacity = 1;
	public Color Tint = Color.White;
	public bool World = false;

	public string SpriteID;
	public Texture2D Sprite { get; private set; }
	public int FontSize = 14;

	private int _depth;
	public int Depth
	{
		get => _depth;
		set
		{
			if (_depth == value)
				return;

			_depth = value;
			Renderer.Instance?.SortRenderers();
		}
	}

	public List<TweenBase> activeTweens = new();

	public void Sync()
	{
		if (string.IsNullOrEmpty(SpriteID))
		{
			Sprite = default;
			return;
		}

		Sprite = Assets.Instance.GetSprite(SpriteID);
	}

	public void Dispose()
	{
		foreach (var i in activeTweens)
		{
			TweenManager.Remove(i);
		}
	}
}