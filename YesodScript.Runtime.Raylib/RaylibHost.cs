using System.Numerics;
using Raylib_cs;
using TweenLib;

namespace YesodScript.Runtime;

public class RaylibHost
{
	public static RaylibHost Instance { get; private set; }
	public const float hz = 1.0f / 60.0f;
	private float accumulator = 0.0f;
	public float HzAlpha => accumulator / hz;
	private GameVM vm;
	private Renderer renderer;
	private Assets assets;
	private string? fatalErrorText;
	private bool windowReady;
	private bool ownsWindow;
	private bool audioReady;
	private bool latchedClick;
	private bool pointerWasDown;
	private bool cleanedUp;

	private readonly HashSet<KeyboardKey> holdKeys = new();
	private readonly HashSet<KeyboardKey> pressedKeys = new();
	private static readonly KeyboardKey[] allKeys = Enum.GetValues<KeyboardKey>();
	public string? FatalErrorText => fatalErrorText;

	public RaylibHost(GameVM vm) : this(vm, null)
	{

	}

	public RaylibHost(GameVM vm, Action<Assets>? configureAssets) : this(vm, configureAssets, true, true)
	{

	}

	public RaylibHost(GameVM vm, Action<Assets>? configureAssets, bool createWindow, bool initializeAudioDevice)
	{
		Instance = this;

		ownsWindow = createWindow;
		if (createWindow)
		{
			var flags = ConfigFlags.VSyncHint;
			if (GameContent.Resizable)
			{
				flags |= ConfigFlags.ResizableWindow;
			}

			if (GameContent.Transparent)
			{
				flags |= ConfigFlags.TransparentWindow;
				flags |= ConfigFlags.UndecoratedWindow;
			}

			Raylib.SetConfigFlags(flags);
			Raylib.InitWindow(GameContent.WindowWidth, GameContent.WindowHeight, GameContent.Title);
			if (GameContent.Maximized && GameContent.Resizable)
				Raylib.MaximizeWindow();
		}

		windowReady = true;
		if (initializeAudioDevice)
		{
			Raylib.InitAudioDevice();
			audioReady = true;
		}
		Raylib.SetExitKey(0);

		renderer = new();
		assets = new();

		this.vm = vm;
		try
		{
			configureAssets?.Invoke(assets);
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
		latchedClick = false;

		while (!Raylib.WindowShouldClose() && UpdateFrame())
		{

		}
	}

	public bool UpdateFrame()
	{
		var pointerDown = Raylib.IsMouseButtonDown(MouseButton.Left) || Raylib.GetTouchPointCount() > 0;
		var pointerPressed = Raylib.IsMouseButtonPressed(MouseButton.Left) || (pointerDown && !pointerWasDown);
		pointerWasDown = pointerDown;

		return UpdateFrame(Raylib.GetFrameTime(), renderer.GetVirtualPointerPosition(), pointerPressed, pointerDown);
	}

	public bool UpdateFrame(float frameTime, Vector2 mousePosition, bool mousePressed)
	{
		return UpdateFrame(frameTime, mousePosition, mousePressed, false);
	}

	public bool UpdateFrame(float frameTime, Vector2 mousePosition, bool mousePressed, bool mouseHeld)
	{
		if (cleanedUp)
			return false;

		if (fatalErrorText != null)
		{
			DrawFatalError();
			return true;
		}

		frameTime = Math.Clamp(frameTime, 0f, 0.25f);
		if (mousePressed)
			latchedClick = true;

		foreach (var key in allKeys)
		{
			if (Raylib.IsKeyPressed(key)) pressedKeys.Add(key);
			if (Raylib.IsKeyDown(key)) holdKeys.Add(key);
		}

		accumulator += frameTime;
		var vmTickedThisFrame = false;
		try
		{
			while (accumulator >= hz)
			{
				renderer.SnapshotPosition();
				vm.UpdateInputVars((int)mousePosition.X, (int)mousePosition.Y, latchedClick, mouseHeld);
				vm.Tick(hz);
				latchedClick = false;
				accumulator -= hz;
				vmTickedThisFrame = true;

				if (!vm.IsRunning) break;
			}
		}
		catch (Exception ex)
		{
			SetFatal(ex, "Runtime");
			DrawFatalError();
			return true;
		}

		if (vmTickedThisFrame)
		{
			holdKeys.Clear();
			pressedKeys.Clear();
		}

		if (!vm.IsRunning)
			return false;

		try
		{
			TweenManager.Update(frameTime, frameTime);
			renderer.Draw();
		}
		catch (Exception ex)
		{
			SetFatal(ex, "Render");
			DrawFatalError();
		}

		return true;
	}

	public void Cleanup()
	{
		if (cleanedUp)
			return;

		cleanedUp = true;
		try { vm.Dispose(); } catch { }
		try { assets.Dispose(); } catch { }
		try { renderer.Dispose(); } catch { }

		if (audioReady)
		{
			Raylib.CloseAudioDevice();
			audioReady = false;
		}

		if (windowReady && ownsWindow)
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