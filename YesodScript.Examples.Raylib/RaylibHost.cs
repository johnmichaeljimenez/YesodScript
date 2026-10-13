using Raylib_cs;
using YesodScript.Engine.Core;

namespace YesodScript.Examples;

public class RaylibHost
{
	public static RaylibHost Instance { get; private set; }
	const float hz = 1.0f / 60.0f;
	const int screenWidth = 1280;
	const int screenHeight = 720;
	private GameVM vm;
	private Renderer renderer;

	public RaylibHost(GameVM vm)
	{
		Instance = this;

		Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
		Raylib.InitWindow(screenWidth, screenHeight, "YesodScript Raylib-cs example");

		renderer = new();

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

			if (Raylib.IsMouseButtonPressed(0))
			{
				latchedClick = true;
			}

			var mousePos = renderer.GetVirtualMousePosition();

			accumulator += frameTime;
			while (accumulator >= hz)
			{
				vm.UpdateInputVars((int)mousePos.X, (int)mousePos.Y, latchedClick);
				vm.Tick(hz);
				accumulator -= hz;

				if (!vm.IsRunning)
					break;
			}

			latchedClick = false;
			
			if (!vm.IsRunning)
				break;

			renderer.Draw();
		}
	}

	public void Cleanup()
	{
		vm.Dispose();
		renderer.Dispose();
		Raylib.CloseWindow();
	}
}