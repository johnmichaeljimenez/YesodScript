using Raylib_cs;
using Tween;

namespace YesodScript.Examples;

public class RaylibHost
{
	public static RaylibHost Instance { get; private set; }
	const float hz = 1.0f / 60.0f;
	private float accumulator = 0.0f;
	public float HzAlpha => accumulator / hz;
	const int screenWidth = 1280;
	const int screenHeight = 720;
	private GameVM vm;
	private Renderer renderer;
	private Assets assets;

	private readonly HashSet<KeyboardKey> holdKeys = new();
	private readonly HashSet<KeyboardKey> pressedKeys = new();
	private static readonly KeyboardKey[] allKeys = Enum.GetValues<KeyboardKey>();

	public RaylibHost(GameVM vm)
	{
		Instance = this;

		Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
		Raylib.InitWindow(screenWidth, screenHeight, "Pong");
		Raylib.MaximizeWindow();
		Raylib.InitAudioDevice();
		Raylib.SetExitKey(0);

		renderer = new();
		assets = new();

		this.vm = vm;
		vm.Initialize();
	}

	public void Tick()
	{
		accumulator = 0;
		var latchedClick = false;

		while (!Raylib.WindowShouldClose())
		{
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
			while (accumulator >= hz)
			{
				renderer.SnapshotPosition();
				vm.UpdateInputVars((int)mousePos.X, (int)mousePos.Y, latchedClick);
				vm.Tick(hz);
				accumulator -= hz;
				vmTickedThisFrame = true;

				if (!vm.IsRunning) break;
			}

			if (vmTickedThisFrame)
			{
				latchedClick = false;
				holdKeys.Clear();
				pressedKeys.Clear();
			}

			if (!vm.IsRunning) break;

			TweenManager.Update(frameTime, frameTime);
			renderer.Draw();
		}
	}

	public void Cleanup()
	{
		vm.Dispose();
		assets.Dispose();
		renderer.Dispose();
		Raylib.CloseAudioDevice();
		Raylib.CloseWindow();
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
}