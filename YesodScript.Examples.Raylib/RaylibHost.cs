using Raylib_cs;

namespace YesodScript.Examples;

public class RaylibHost
{
	public static RaylibHost Instance { get; private set; }
	const float hz = 1.0f / 60.0f;
	const int screenWidth = 1280;
	const int screenHeight = 720;
	private GameVM vm;
	private Renderer renderer;
	private Assets assets;

	private readonly HashSet<KeyboardKey> pressedKeys = new();
	private static readonly KeyboardKey[] allKeys = Enum.GetValues<KeyboardKey>();

	public RaylibHost(GameVM vm)
	{
		Instance = this;

		Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
		Raylib.InitWindow(screenWidth, screenHeight, "YesodScript Raylib-cs example");

		renderer = new();
		assets = new();

		this.vm = vm;
		vm.Initialize();
	}

	public void Tick()
	{
		var accumulator = 0.0f;
		var latchedClick = false;

		while (!Raylib.WindowShouldClose())
		{
			var frameTime = Raylib.GetFrameTime();
			if (frameTime > 0.25f) frameTime = 0.25f;

			if (Raylib.IsMouseButtonPressed(0)) latchedClick = true;

			foreach (var key in allKeys)
			{
				if (Raylib.IsKeyDown(key)) pressedKeys.Add(key);
			}

			var mousePos = renderer.GetVirtualMousePosition();
			accumulator += frameTime;

			bool vmTickedThisFrame = false;
			while (accumulator >= hz)
			{
				vm.UpdateInputVars((int)mousePos.X, (int)mousePos.Y, latchedClick);
				vm.Tick(hz);
				accumulator -= hz;
				vmTickedThisFrame = true;

				if (!vm.IsRunning) break;
			}

			if (vmTickedThisFrame)
			{
				latchedClick = false;
				pressedKeys.Clear();
			}

			if (!vm.IsRunning) break;

			renderer.Draw();
		}
	}

	public void Cleanup()
	{
		vm.Dispose();
		assets.Dispose();
		renderer.Dispose();
		Raylib.CloseWindow();
	}

	public bool IsKeyPressed(KeyboardKey key)
	{
		return pressedKeys.Contains(key);
	}
}