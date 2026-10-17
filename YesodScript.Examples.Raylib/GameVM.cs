using System.Numerics;
using Raylib_cs;
using Tween;
using YesodScript.Engine.Commands;
using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Examples;

public class GameVM : VirtualMachine
{
	public bool mouseClicked;
	private Var mouseX, mouseY;

	public GameVM(int randomSeed = 0) : base(randomSeed)
	{
	}

	public static GameVM Instance { get; private set; }



	public static GameVM Create()
	{
		var vm = new GameVM(Environment.TickCount);
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
	[CommandParameter]
	private string filter = "*";

	public override void OnEnter()
	{
		Renderer.Instance.ClearRenderData(filter);
	}
}

public class Show : GameCommand
{
	[CommandParameter]
	private bool enabled = false;

	[CommandParameter]
	private string filter = "*";

	public override void OnEnter()
	{
		Renderer.Instance.SetRenderDataState(enabled, filter);
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
	[CommandParameter]
	private int depth;

	public override void OnEnter()
	{
		var data = Renderer.Instance.CreateRenderData(x, y);
		data.Depth = depth;
		if (string.IsNullOrEmpty(storeID))
			return;

		var storeVar = GameVM.GetVar(storeID, Instruction);
		storeVar.Value = data.ID;
	}
}

public class Edit : EditPropertyCommand<RenderData>
{
	public override void OnEnter()
	{
		base.OnEnter();
		instance.Sync();
	}

	protected override RenderData GetInstance(int id)
	{
		return Renderer.Instance.GetRenderData(id);
	}

	protected override IReadOnlyDictionary<string, PathMember> dataMembers => Renderer.RenderDataMembers;
}

public class Tween : GameCommand
{
	[CommandParameter] private int id;
	[CommandParameter] private string path;
	[CommandParameter] private float from;
	[CommandParameter] private float to;
	[CommandParameter] private bool relative;
	[CommandParameter] private float duration = 0.5f;
	[CommandParameter] private string easing = "Linear";
	[CommandParameter] private string tweenId = "";

	public override void OnEnter()
	{
		var data = Renderer.Instance.GetRenderData(id);
		if (!Renderer.RenderDataMembers.TryGetValue(path, out var member))
			throw new Exception($"Unknown path '{path}'");

		Func<float> getter = () => (float)member.Get(data);
		Action<float> setter = (v) => member.Set(data, v);

		var _from = relative ? getter.Invoke() + from : from;
		var _to = relative ? getter() + to : to;

		var tween = new Tween<float>(getter, setter, _to, duration, _from, id: string.IsNullOrEmpty(tweenId) ? null : tweenId);
		tween.SetEasing(ResolveEasing(easing));

		data.activeTweens.Add(tween);
		TweenManager.Add(tween);
	}

	private static EasingFunction ResolveEasing(string name) => name.ToUpperInvariant() switch
	{
		"LINEAR" => Easing.Linear,
		"QUAD" => Easing.QuadInOut,
		"SHAKE" => Easing.QuadInOutLoop,
		_ => Easing.Linear
	};
}

public class PlaySound : GameCommand
{
	[CommandParameter]
	private string sfxID;

    public override void OnEnter()
    {
		var snd = Assets.Instance.GetSound(sfxID);
		Raylib.SetSoundPitch(snd, Raymath.Remap(Raylib.GetRandomValue(0, 100), 0, 100, 0.9f, 1.0f));
		Raylib.PlaySound(snd);
    }
}