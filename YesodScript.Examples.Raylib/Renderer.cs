using System.Numerics;
using Raylib_cs;

namespace YesodScript.Examples;

public class Renderer : IDisposable
{
	public static Renderer Instance { get; private set; }

	private int renderID;
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

	public RenderData CreateRenderData(Vector2 position, Vector2 size, bool disabled)
	{
		var r = new RenderData();
		r.ID = renderID + 1;
		r.Position = position;
		r.Size = size;
		r.Disabled = disabled;

		renderList[r.ID] = r;
		return r;
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

				Raylib.DrawRectangleV(i.Value.Position, i.Value.Size, Color.Red);
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

public struct RenderData
{
	public int ID;
	public bool Disabled;
	public Vector2 Position;
	public Vector2 Size;
	public float Rotation; //degrees
}