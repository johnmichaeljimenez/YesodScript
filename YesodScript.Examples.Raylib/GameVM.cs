using System.Numerics;
using Raylib_cs;
using YesodScript.Engine.Commands;
using YesodScript.Engine.Core;

namespace YesodScript.Examples;

public class GameVM : VirtualMachine
{
	public bool mouseClicked;
	private Var mouseX, mouseY;

	public static GameVM Instance { get; private set; }
	public static GameVM Create()
	{
		var vm = new GameVM();
		vm.OnOutput += (msg) =>
		{
			Raylib.TraceLog(TraceLogLevel.Info, msg);
		};

		vm.CompileDirectory("scripts");

		Instance = vm;
		return vm;
	}

	protected override void OnInitialize()
	{
		mouseX = AddGlobalVar("SYS:MOUSE_X", typeof(int), 0);
		mouseY = AddGlobalVar("SYS:MOUSE_Y", typeof(int), 0);
		AddGlobalVar("SYS:REQ_EXIT", typeof(int), 0);

		AddGlobalList("SYS:DRAWLIST", typeof(int));
	}

	public void UpdateInputVars(int mx, int my, bool mouse)
	{
		mouseClicked = mouse;
		mouseX.Value = mx;
		mouseY.Value = my;
	}
}

public class GameCommand : Command
{
	protected GameVM GameVM => (GameVM)VM;
}

public class WaitFrame : Command
{
	private bool ticked;
	public override void OnEnter()
	{
		ticked = false;
	}

	public override bool OnTick(float deltaTime)
	{
		if (!ticked)
		{
			ticked = true;
			return false;
		}

		return true;
	}
}

public class Clear : GameCommand
{
	public override void OnEnter()
	{
		Renderer.Instance.ClearRenderData();
	}
}

public class JumpClick : GameCommand
{
	[CommandParameter]
	private string labelName;

	public override void OnEnter()
	{
		if (GameVM.mouseClicked)
			Instruction.Jump(labelName);
	}
}

public class JumpKey : GameCommand
{
	[CommandParameter]
	private KeyboardKey key;
	
	[CommandParameter]
	private string labelName;

	public override void OnEnter()
	{
		if (RaylibHost.Instance.IsKeyPressed(key))
			Instruction.Jump(labelName);
	}
}

public class Render : GameCommand
{
	[CommandParameter]
	private int x;
	[CommandParameter]
	private int y;
	[CommandParameter]
	private string storeID;

	public override void OnEnter()
	{
		var data = Renderer.Instance.CreateRenderData(x, y);
		if (string.IsNullOrEmpty(storeID))
			return;

		var storeVar = GameVM.GetVar(storeID, Instruction);
		storeVar.Value = data.ID;
	}
}

public class Edit : GameCommand
{
	[CommandParameter]
	private int id;

	[CommandParameter]
	private string path;

	[CommandParameter]
	private string value;

	public override void OnEnter()
	{
		var data = Renderer.Instance.GetRenderData(id);
		Renderer.RenderDataMembers[path].SetParsed(data, value);
		data.Sync();
	}
}