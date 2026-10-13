using System.Numerics;
using Raylib_cs;
using YesodScript.Engine.Tools;

namespace YesodScript.Examples;

public class Renderer : IDisposable
{
	public static readonly IReadOnlyDictionary<string, PathMember> RenderDataMembers = PathMemberCache.Build<RenderData>();
	public static Renderer Instance { get; private set; }

	private int renderID = 1;	//0 means stale default data
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

	public void ClearRenderData()
	{
		renderList.Clear();
	}

	public void RemoveRenderData(int id)
	{
		renderList.Remove(id);
	}

	public RenderData CreateRenderData(int x, int y)
	{
		var r = new RenderData();
		r.ID = renderID + 1;
		r.X = x;
		r.Y = y;
		r.W = 1;
		r.H = 1;

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

				Raylib.DrawRectangle(i.Value.X, i.Value.Y, i.Value.W, i.Value.H, Color.Red);
				
				if (string.IsNullOrWhiteSpace(i.Value.Text))
					continue;
					
				Raylib.DrawText(i.Value.Text, i.Value.X, i.Value.Y, 14, Color.Red);
			}
		}
		Raylib.EndTextureMode();

		Raylib.BeginDrawing();
		{
			Raylib.ClearBackground(Color.Black);
			Raylib.DrawTexturePro(rt.Texture, sourceRect, destRect, Vector2.Zero, 0.0f, Color.White);

			// Raylib.DrawFPS(4, 4);
		}
		Raylib.EndDrawing();
	}
}

public class RenderData
{
	public int ID;
	public bool Disabled;
	public int X, Y;
	public int W, H;
	public float Rotation; //degrees
	public string Text;

	public void Sync()
	{
		
	}
}