using System.Numerics;
using Raylib_cs;
using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Examples;

public class Renderer : IDisposable
{
	public static readonly IReadOnlyDictionary<string, PathMember> RenderDataMembers = PathMemberCache.Build<RenderData>();
	public static Renderer Instance { get; private set; }

	private int renderID = 1;   //0 means stale default data
	private readonly Dictionary<int, RenderData> renderList = new();

	const int scale = 2;
	const int VirtualWidth = 1280 / scale;
	const int VirtualHeight = 720 / scale;

	private RenderTexture2D rt;

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
			return;
		}

		foreach (var i in renderList.Values.ToArray())
		{
			if (Utils.IsMatchWildcard(i.Group, filter))
				renderList.Remove(i.ID);
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
		renderList.Remove(id);
	}

	public RenderData CreateRenderData(int x, int y)
	{
		var r = new RenderData();
		r.ID = renderID++;
		r.X = x;
		r.Y = y;

		renderList[r.ID] = r;
		return r;
	}

	public RenderData GetRenderData(int id)
	{
		return renderList[id];
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

			foreach (var i in renderList)
			{
				if (i.Value.Disabled)
					continue;

				if (i.Value.Sprite.Id != 0)
				{
					var size = i.Value.Sprite.Dimensions;
					var to = new Vector2(i.Value.X, i.Value.Y);
					i.Value.visPos = Raymath.Vector2Lerp(i.Value.visPos, to, Raylib.GetFrameTime() * 30);

					Raylib.DrawTextureV(i.Value.Sprite, i.Value.visPos - (size * 0.5f), Color.White);
				}

				if (string.IsNullOrWhiteSpace(i.Value.Text))
					continue;

				Raylib.DrawText(i.Value.Text, (int)i.Value.X, (int)i.Value.Y, 14, Color.Red);
			}
		}
		Raylib.EndTextureMode();

		Raylib.BeginDrawing();
		{
			Raylib.ClearBackground(Color.Black);
			Raylib.DrawTexturePro(rt.Texture, sourceRect, destRect, Vector2.Zero, 0.0f, Color.White);

			Raylib.DrawFPS(4, 4);
		}
		Raylib.EndDrawing();
	}
}

public class RenderData
{
	public int ID;
	public bool Disabled;
	public float X, Y;
	public string Text;
	public string Group = "";

	public Vector2 visPos;

	public string SpriteID;
	public Texture2D Sprite { get; private set; }

	public void Sync()
	{
		if (string.IsNullOrEmpty(SpriteID))
		{
			Sprite = default;
			return;
		}

		Sprite = Assets.Instance.GetSprite(SpriteID);
	}
}