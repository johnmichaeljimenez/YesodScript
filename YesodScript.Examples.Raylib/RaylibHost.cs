using Raylib_cs;
using YesodScript.Engine.Core;

namespace YesodScript.Examples;

public class RaylibHost
{
	const int screenWidth = 800;
	const int screenHeight = 450;
	private VirtualMachine vm;

	public RaylibHost(VirtualMachine vm)
	{
		Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint | ConfigFlags.MaximizedWindow);
		Raylib.InitWindow(screenWidth, screenHeight, "YesodScript Raylib-cs example");

		this.vm = vm;
		vm.Initialize();
	}

	public void Tick()
	{
		while (!Raylib.WindowShouldClose())
		{
			var dt = Raylib.GetFrameTime();
			vm.Tick(dt);    //TODO: use fixed 60hz

			Raylib.BeginDrawing();
			Raylib.ClearBackground(Color.Black);

			Raylib.EndDrawing();
		}
	}

	public void Cleanup()
	{
		vm.Dispose();
		Raylib.CloseWindow();
	}
}