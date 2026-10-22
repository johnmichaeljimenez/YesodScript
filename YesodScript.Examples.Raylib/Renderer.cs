using System.Numerics;
using Raylib_cs;
using TweenLib;
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

	private int VirtualWidth;
	private int VirtualHeight;

	private RenderTexture2D rt;

	public Camera2D Camera = new()
	{
		Offset = new Vector2(GameContent.VirtualResolution.X * 0.5f, GameContent.VirtualResolution.Y * 0.5f),
		Zoom = 1f
	};

	public Renderer()
	{
		Instance = this;
		VirtualWidth = (int)GameContent.VirtualResolution.X;
		VirtualHeight = (int)GameContent.VirtualResolution.Y;
		rt = Raylib.LoadRenderTexture(VirtualWidth, VirtualHeight);

		var filter = GameContent.PixelArt ? TextureFilter.Point : TextureFilter.Bilinear;
		Raylib.SetTextureFilter(rt.Texture, filter);

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

	public int GetHoveredRenderId(Vector2 virtualMouse)
	{
		EnsureSorted();

		var worldMouse = ScreenToWorld(virtualMouse);

		for (int i = sortedRenderList.Count - 1; i >= 0; i--)
		{
			var item = sortedRenderList[i];
			if (item.Disabled || !item.IsClickable)
				continue;

			var testPos = item.World ? worldMouse : virtualMouse;
			if (HitTest(item, testPos))
				return item.ID;
		}

		return -1;
	}

	private Vector2 ScreenToWorld(Vector2 screen)
	{
		var cam = Camera;
		var zoom = cam.Zoom == 0f ? 1f : cam.Zoom;
		return new Vector2(
			(screen.X - cam.Offset.X) / zoom + cam.Target.X,
			(screen.Y - cam.Offset.Y) / zoom + cam.Target.Y
		);
	}

	private static bool HitTest(RenderData item, Vector2 point)
	{
		var bounds = GetBounds(item);
		if (bounds.Width <= 0f || bounds.Height <= 0f)
			return false;

		return Raylib.CheckCollisionPointRec(point, bounds);
	}

	private static Rectangle GetBounds(RenderData item)
	{
		var w = 0f;
		var h = 0f;
		var originX = 0f;
		var originY = 0f;

		if (item.Sprite.Id != 0)
		{
			var size = item.Sprite.Dimensions;
			w = size.X * MathF.Abs(item.ScaleX);
			h = size.Y * MathF.Abs(item.ScaleY);
			originX = w * 0.5f;
			originY = h * 0.5f;
		}
		else if (!string.IsNullOrWhiteSpace(item.Text))
		{
			var font = Assets.Instance.GetFont(item.FontName);
			var textSize = MeasureMultilineText(font, item.Text, item.FontSize, 1f);
			w = textSize.X;
			h = textSize.Y;
			originX = item.TextHAlign switch
			{
				TextHAlign.Left => 0f,
				TextHAlign.Right => w,
				_ => w * 0.5f,
			};
			originY = item.TextVAlign switch
			{
				TextVAlign.Top => 0f,
				TextVAlign.Bottom => h,
				_ => h * 0.5f,
			};
		}
		else
		{
			originX = w * 0.5f;
			originY = h * 0.5f;
		}

		return new Rectangle(item.X - originX, item.Y - originY, w, h);
	}

	private static string[] SplitLines(string text)
	{
		return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
	}

	private static Vector2 MeasureMultilineText(Font font, string text, float fontSize, float spacing)
	{
		var lines = SplitLines(text);
		float width = 0f;
		float lineHeight = fontSize;
		float totalHeight = 0f;

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			var size = string.IsNullOrEmpty(line)
				? new Vector2(0f, fontSize)
				: Raylib.MeasureTextEx(font, line, fontSize, spacing);

			if (size.X > width)
				width = size.X;
			if (size.Y > lineHeight)
				lineHeight = size.Y;
		}

		totalHeight = lines.Length == 0 ? fontSize : lineHeight * lines.Length;
		return new Vector2(width, totalHeight);
	}

	private static void DrawMultilineText(
		Font font,
		string text,
		Vector2 origin,
		float fontSize,
		float spacing,
		Color color,
		TextHAlign hAlign,
		TextVAlign vAlign)
	{
		var lines = SplitLines(text);
		if (lines.Length == 0)
			return;

		var blockSize = MeasureMultilineText(font, text, fontSize, spacing);
		var lineHeight = lines.Length > 0 ? blockSize.Y / lines.Length : fontSize;

		var startY = vAlign switch
		{
			TextVAlign.Top => origin.Y,
			TextVAlign.Bottom => origin.Y - blockSize.Y,
			_ => origin.Y - blockSize.Y * 0.5f,
		};

		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			var lineWidth = string.IsNullOrEmpty(line)
				? 0f
				: Raylib.MeasureTextEx(font, line, fontSize, spacing).X;

			var lineX = hAlign switch
			{
				TextHAlign.Left => origin.X,
				TextHAlign.Right => origin.X - lineWidth,
				_ => origin.X - lineWidth * 0.5f,
			};
			var lineY = startY + i * lineHeight;

			if (!string.IsNullOrEmpty(line))
				Raylib.DrawTextEx(font, line, new Vector2(lineX, lineY), fontSize, spacing, color);
		}
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
			Raylib.ClearBackground(Color.Black);

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
			var scaleX = i.ScaleX;
			var scaleY = i.ScaleY;

			var source = new Rectangle(
				0,
				0,
				scaleX < 0 ? -size.X : size.X,
				scaleY < 0 ? -size.Y : size.Y
			);

			var destW = size.X * MathF.Abs(scaleX);
			var destH = size.Y * MathF.Abs(scaleY);
			var dest = new Rectangle(drawX, drawY, destW, destH);
			var origin = new Vector2(destW * i.OriginX, destH * i.OriginY);

			var c1 = new Color(i.Tint.R, i.Tint.G, i.Tint.B, (byte)Math.Clamp(i.Tint.A * i.Opacity, 0f, 255f));
			Raylib.DrawTexturePro(i.Sprite, source, dest, origin, 0f, c1);
		}

		if (string.IsNullOrWhiteSpace(i.Text))
			return;

		var font = Assets.Instance.GetFont(i.FontName);
		var fontSize = i.FontSize;
		var c2 = new Color(i.Tint.R, i.Tint.G, i.Tint.B, (byte)Math.Clamp(i.Tint.A * i.Opacity, 0f, 255f));
		DrawMultilineText(font, i.Text, new Vector2(drawX, drawY), fontSize, 1f, c2, i.TextHAlign, i.TextVAlign);
	}
}

public enum TextHAlign
{
	Left = -1,
	Center = 0,
	Right = 1,
}

public enum TextVAlign
{
	Top = -1,
	Middle = 0,
	Bottom = 1,
}

public class RenderData : IDisposable
{
	public int ID;
	public bool Disabled;
	public bool IsClickable;

	public float X;
	public float Y;
	public float prevX, prevY;
	public float ScaleX = 1;
	public float ScaleY = 1;
	public string Text;
	public TextHAlign TextHAlign = TextHAlign.Center;
	public TextVAlign TextVAlign = TextVAlign.Middle;
	public string Group = "";
	public float Opacity = 1;
	public Color Tint = Color.White;
	public bool World = false;

	public string SpriteID;
	public Texture2D Sprite { get; private set; }
	public int FontSize = 14;
	public string FontName = "";

	public float OriginX = 0.5f;
	public float OriginY = 0.5f;

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