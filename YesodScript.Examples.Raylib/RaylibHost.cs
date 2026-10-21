using Raylib_cs;
using TweenLib;

namespace YesodScript.Examples;

public class RaylibHost
{
	public static RaylibHost Instance { get; private set; }
	public const float hz = 1.0f / 60.0f;
	private float accumulator = 0.0f;
	public float HzAlpha => accumulator / hz;
	const int screenWidth = 1280;
	const int screenHeight = 720;
	private GameVM vm;
	private Renderer renderer;
	private Assets assets;
	private string? fatalErrorText;
	private bool windowReady;
	private bool audioReady;

	private readonly HashSet<KeyboardKey> holdKeys = new();
	private readonly HashSet<KeyboardKey> pressedKeys = new();
	private static readonly KeyboardKey[] allKeys = Enum.GetValues<KeyboardKey>();

	public RaylibHost(GameVM vm)
	{
		Instance = this;

		Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
		Raylib.InitWindow(screenWidth, screenHeight, GameContent.Title);
		windowReady = true;
		Raylib.MaximizeWindow();
		Raylib.InitAudioDevice();
		audioReady = true;
		Raylib.SetExitKey(0);

		renderer = new();
		assets = new();

		this.vm = vm;
		try
		{
			vm.Initialize();
		}
		catch (Exception ex)
		{
			SetFatal(ex, "Initialization");
		}
	}

	public void Tick()
	{
		accumulator = 0;
		var latchedClick = false;

		while (!Raylib.WindowShouldClose())
		{
			if (fatalErrorText != null)
			{
				DrawFatalError();
				continue;
			}

			var frameTime = Raylib.GetFrameTime();
			if (frameTime > 0.25f) frameTime = 0.25f;

			if (Raylib.IsMouseButtonPressed(0)) latchedClick = true;

			foreach (var key in allKeys)
			{
				if (Raylib.IsKeyPressed(key)) pressedKeys.Add(key);
				if (Raylib.IsKeyDown(key)) holdKeys.Add(key);
			}

			var mousePos = renderer.GetVirtualMousePosition();
			accumulator += frameTime;

			bool vmTickedThisFrame = false;
			try
			{
				while (accumulator >= hz)
				{
					renderer.SnapshotPosition();
					vm.UpdateInputVars((int)mousePos.X, (int)mousePos.Y, latchedClick);
					vm.Tick(hz);
					accumulator -= hz;
					vmTickedThisFrame = true;

					if (!vm.IsRunning) break;
				}
			}
			catch (Exception ex)
			{
				SetFatal(ex, "Runtime");
				continue;
			}

			if (vmTickedThisFrame)
			{
				latchedClick = false;
				holdKeys.Clear();
				pressedKeys.Clear();
			}

			if (!vm.IsRunning) break;

			try
			{
				TweenManager.Update(frameTime, frameTime);
				renderer.Draw();
			}
			catch (Exception ex)
			{
				SetFatal(ex, "Render");
			}
		}
	}

	public void Cleanup()
	{
		try { vm.Dispose(); } catch {  }
		try { assets.Dispose(); } catch { }
		try { renderer.Dispose(); } catch {  }

		if (audioReady)
		{
			Raylib.CloseAudioDevice();
			audioReady = false;
		}

		if (windowReady)
		{
			Raylib.CloseWindow();
			windowReady = false;
		}
	}

	public bool IsKeyHeld(KeyboardKey key)
	{
		return holdKeys.Contains(key);
	}

	public bool IsKeyPressed(KeyboardKey key)
	{
		var pressed = pressedKeys.Contains(key);

		if (pressed)
		{
			pressedKeys.Remove(key);    //consume immediately if true
		}

		return pressed;
	}

	private void SetFatal(Exception ex, string stage)
	{
		if (fatalErrorText != null)
			return;

		ErrorReport.Write(ex, stage);
		fatalErrorText = ErrorReport.FormatOnScreen(ex, stage);
		if (windowReady)
			Raylib.SetWindowTitle("YesodScript — Error");
	}

	private void DrawFatalError()
	{
		Raylib.BeginDrawing();
		Raylib.ClearBackground(new Color(20, 12, 12, 255));

		var fontSize = 20;
		var lineHeight = 26;
		var marginX = 40;
		var y = 40;

		Raylib.DrawText("YesodScript", marginX, y, 28, new Color(255, 120, 120, 255));
		y += 40;

		foreach (var line in (fatalErrorText ?? "Unknown error.").Replace("\r\n", "\n").Split('\n'))
		{
			Raylib.DrawText(line, marginX, y, fontSize, Color.RayWhite);
			y += lineHeight;
			if (y > Raylib.GetScreenHeight() - 40)
				break;
		}

		Raylib.EndDrawing();
	}
}