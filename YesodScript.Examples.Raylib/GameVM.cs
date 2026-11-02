using System.Numerics;
using Raylib_cs;
using TweenLib;
using YesodScript.Engine.Commands;
using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Examples;

public class GameVM : VirtualMachine
{
	public bool mouseClicked;
	private Var screenWidth, screenHeight, screenWidthHalf, screenHeightHalf;
	private Var mouseX, mouseY;
	private Var hoveredId, clickedId;
	private Var logString;

	public readonly SaveManager SaveManager;

	public GameVM(int randomSeed = 0) : base(randomSeed)
	{
		SaveManager = new SaveManager(this);
	}

	public static GameVM Instance { get; private set; }

	public static GameVM Create()
	{
		var vm = new GameVM(Environment.TickCount);
		vm.OnOutput += (msg) =>
		{
			var logs = vm.logString.Value.ToString();
			logs += msg + "\n";

			var lines = logs.Split(['\n'], StringSplitOptions.None);
			if (lines.Length > 11)
			{
				logs = string.Join("\n", lines.TakeLast(11));
			}

			vm.logString.Value = logs;

			var all = vm.FireTrigger("SYS:LOG_RECEIVED");
			foreach (var ins in all)
			{
				if (!ins.IsRunning) continue;

				vm.RunInline(ins, RaylibHost.hz);
			}

			Raylib.TraceLog(TraceLogLevel.Info, $"[{vm.CurrentTick}] {msg}");
		};

		foreach (var corePath in GameContent.CorePaths)
			vm.CompileDirectory(corePath);
		vm.CompileDirectory(GameContent.ScriptsPath);

		Instance = vm;
		return vm;
	}

	protected override void OnInitialize()
	{
		logString = AddGlobalVar("SYS:LOG", typeof(string), "");
		mouseX = AddGlobalVar("SYS:MOUSE_X", typeof(int), 0);
		mouseY = AddGlobalVar("SYS:MOUSE_Y", typeof(int), 0);
		AddGlobalVar("SYS:REQ_EXIT", typeof(bool), 0);

		screenWidth = AddGlobalVar("SYS:SCREEN_WIDTH", typeof(int), GameContent.VirtualResolution.X);
		screenHeight = AddGlobalVar("SYS:SCREEN_HEIGHT", typeof(int), GameContent.VirtualResolution.Y);

		screenWidthHalf = AddGlobalVar("SYS:SCREEN_WIDTH_HALF", typeof(int), GameContent.VirtualResolution.X / 2);
		screenHeightHalf = AddGlobalVar("SYS:SCREEN_HEIGHT_HALF", typeof(int), GameContent.VirtualResolution.Y / 2);

		hoveredId = AddGlobalVar("SYS:HOVERED", typeof(int), -1);
		clickedId = AddGlobalVar("SYS:CLICKED", typeof(int), -1);

		AddGlobalList("SYS:DRAWLIST", typeof(int));
	}

	public void UpdateInputVars(int mx, int my, bool mouse)
	{
		mouseClicked = mouse;
		mouseX.Value = mx;
		mouseY.Value = my;

		var mousePos = new Vector2(mx, my);
		var hovered = Renderer.Instance.GetHoveredRenderId(mousePos);
		hoveredId.Value = hovered;
		clickedId.Value = (mouse && hovered != -1) ? hovered : -1;
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
		if (RaylibHost.Instance.IsKeyHeld(key))
			Instruction.Jump(labelName);
	}
}

public class JumpKeyPress : GameCommand
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
	[CommandParameter]
	private string group = "";

	public override void OnEnter()
	{
		var data = Renderer.Instance.CreateRenderData(x, y);
		data.Depth = depth;
		data.Group = group;
		if (string.IsNullOrEmpty(storeID))
			return;

		var storeVar = GameVM.GetVar(storeID, Instruction);
		storeVar.Value = data.ID;
	}
}

public class RemoveRender : GameCommand
{
	[CommandParameter]
	private int id;

	public override void OnEnter()
	{
		Renderer.Instance.RemoveRenderData(id);
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
	[CommandParameter] private bool angleWrap = false;

	public override void OnEnter()
	{
		var data = Renderer.Instance.GetRenderData(id);
		if (!Renderer.RenderDataMembers.TryGetValue(path, out var member))
			throw new Exception($"Unknown path '{path}'");

		Func<float> getter = () => (float)member.Get(data);
		Action<float> setter = (v) => member.Set(data, v);

		var _from = relative ? getter.Invoke() + from : from;
		var _to = relative ? getter() + to : to;
		if (angleWrap)
			_to = _from + TweenUtil.ShortestAngleDelta(_from, _to);

		var tween = TweenLib.Tween.To(getter, setter, _to, duration).
					SetFrom(_from).
					SetEasing(TweenUtil.ResolveEasing(easing)).
					SetId(string.IsNullOrEmpty(tweenId) ? null : tweenId);

		data.activeTweens.Add(tween);
		TweenManager.Add(tween);
	}
}

public class Render3D : GameCommand
{
	[CommandParameter] private float x;
	[CommandParameter] private float y;
	[CommandParameter] private float z = 0f;
	[CommandParameter] private string modelID;
	[CommandParameter] private string storeID;
	[CommandParameter] private string group = "";

	public override void OnEnter()
	{
		var data = Renderer.Instance.CreateRenderData3D(x, y, z);
		data.Group = group;
		data.ModelID = modelID;
		data.Sync();

		if (string.IsNullOrEmpty(storeID))
			return;

		var storeVar = GameVM.GetVar(storeID, Instruction);
		storeVar.Value = data.ID;
	}
}

public class RemoveRender3D : GameCommand
{
	[CommandParameter]
	private int id;

	public override void OnEnter()
	{
		Renderer.Instance.RemoveRenderData3D(id);
	}
}

public class Edit3D : EditPropertyCommand<RenderData3D>
{
	public override void OnEnter()
	{
		base.OnEnter();
		instance.Sync();
	}

	protected override RenderData3D GetInstance(int id)
	{
		return Renderer.Instance.GetRenderData3D(id);
	}

	protected override IReadOnlyDictionary<string, PathMember> dataMembers => Renderer.RenderData3DMembers;
}

public class Tween3D : GameCommand
{
	[CommandParameter] private int id;
	[CommandParameter] private string path;
	[CommandParameter] private float from;
	[CommandParameter] private float to;
	[CommandParameter] private bool relative;
	[CommandParameter] private float duration = 0.5f;
	[CommandParameter] private string easing = "Linear";
	[CommandParameter] private string tweenId = "";
	[CommandParameter] private bool angleWrap = false;

	public override void OnEnter()
	{
		var data = Renderer.Instance.GetRenderData3D(id);
		if (!Renderer.RenderData3DMembers.TryGetValue(path, out var member))
			throw new Exception($"Unknown path '{path}'");

		Func<float> getter = () => (float)member.Get(data);
		Action<float> setter = (v) => member.Set(data, v);

		var _from = relative ? getter.Invoke() + from : from;
		var _to = relative ? getter() + to : to;
		if (angleWrap)
			_to = _from + TweenUtil.ShortestAngleDelta(_from, _to);

		var tween = TweenLib.Tween.To(getter, setter, _to, duration).
					SetFrom(_from).
					SetEasing(TweenUtil.ResolveEasing(easing)).
					SetId(string.IsNullOrEmpty(tweenId) ? null : tweenId);

		data.activeTweens.Add(tween);
		TweenManager.Add(tween);
	}
}

public class Anim3D : GameCommand
{
	[CommandParameter] private int id;
	[CommandParameter] private string animName; //empty = stop
	[CommandParameter] private bool looping = true;

	public override void OnEnter()
	{
		var data = Renderer.Instance.GetRenderData3D(id);
		data.PlayAnimation(animName, looping);
	}
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

public class Cam : GameCommand
{
	[CommandParameter]
	private float x;
	[CommandParameter]
	private float y;
	[CommandParameter]
	private float zoom;

	public override void OnEnter()
	{
		Renderer.Instance.Camera.X = x;
		Renderer.Instance.Camera.Y = y;
		Renderer.Instance.Camera.Zoom = zoom;
	}
}

public class TweenCam : GameCommand
{
	[CommandParameter] private string path;
	[CommandParameter] private float from;
	[CommandParameter] private float to;
	[CommandParameter] private bool relative;
	[CommandParameter] private float duration = 0.5f;
	[CommandParameter] private string easing = "Linear";
	[CommandParameter] private string tweenId = "";

	public override void OnEnter()
	{
		if (!Renderer.CameraMembers.TryGetValue(path, out var member))
			throw new Exception($"Unknown path '{path}'");

		Func<float> getter = () => (float)member.Get(Renderer.Instance.Camera);
		Action<float> setter = v => member.Set(Renderer.Instance.Camera, v);

		var _from = relative ? getter() + from : from;
		var _to = relative ? getter() + to : to;

		var tween = TweenLib.Tween.To(getter, setter, _to, duration).
					SetFrom(_from).
					SetEasing(TweenUtil.ResolveEasing(easing)).
					SetId(string.IsNullOrEmpty(tweenId) ? null : tweenId);

		TweenManager.Add(tween);
	}
}

public class Cam3D : GameCommand
{
	[CommandParameter] private float fromX;
	[CommandParameter] private float fromY;
	[CommandParameter] private float fromZ;
	[CommandParameter] private float toX;
	[CommandParameter] private float toY;
	[CommandParameter] private float toZ;
	[CommandParameter] private float fovy = 45f;
	[CommandParameter] private bool showGrid = false;

	public override void OnEnter()
	{
		var cam = Renderer.Instance.Camera3D;
		cam.FromX = fromX;
		cam.FromY = fromY;
		cam.FromZ = fromZ;
		cam.ToX = toX;
		cam.ToY = toY;
		cam.ToZ = toZ;
		cam.Fovy = fovy;
		cam.ShowGrid = showGrid;
	}
}

public class TweenCam3D : GameCommand
{
	[CommandParameter] private string path;
	[CommandParameter] private float from;
	[CommandParameter] private float to;
	[CommandParameter] private bool relative;
	[CommandParameter] private float duration = 0.5f;
	[CommandParameter] private string easing = "Linear";
	[CommandParameter] private string tweenId = "";

	public override void OnEnter()
	{
		if (!Renderer.Camera3DMembers.TryGetValue(path, out var member))
			throw new Exception($"Unknown path '{path}'");

		Func<float> getter = () => (float)member.Get(Renderer.Instance.Camera3D);
		Action<float> setter = v => member.Set(Renderer.Instance.Camera3D, v);

		var _from = relative ? getter() + from : from;
		var _to = relative ? getter() + to : to;

		var tween = TweenLib.Tween.To(getter, setter, _to, duration).
					SetFrom(_from).
					SetEasing(TweenUtil.ResolveEasing(easing)).
					SetId(string.IsNullOrEmpty(tweenId) ? null : tweenId);

		TweenManager.Add(tween);
	}
}

public class RegSaveVar : GameCommand
{
	[CommandParameter]
	private string group;

	[CommandParameter]
	private string varName;

	public override void OnEnter()
	{
		var variable = GameVM.GetVar(varName, Instruction);
		GameVM.SaveManager.RegisterVar(group, variable);
	}
}

public class SaveData : GameCommand
{
	[CommandParameter]
	private string group;
	[CommandParameter]
	private string filename;

	public override void OnEnter()
	{
		GameVM.SaveManager.Save(group, filename);
	}
}

public class LoadData : GameCommand
{
	[CommandParameter]
	private string group;
	[CommandParameter]
	private string filename;

	public override void OnEnter()
	{
		GameVM.SaveManager.Load(group, filename);
	}
}

internal static class TweenUtil
{
	public static EasingFunction ResolveEasing(string name) => name.ToUpperInvariant() switch
	{
		"LINEAR" => Easing.Linear,
		"QUAD" => Easing.QuadInOut,
		"BELL" => Easing.Parabola(1),
		"SHAKE" => Easing.Shake(0.7f, 1.5f),
		_ => Easing.Linear
	};

	public static float ShortestAngleDelta(float from, float to)
	{
		float delta = (to - from) % 360f;
		if (delta > 180f)
			delta -= 360f;
		else if (delta <= -180f)
			delta += 360f;
		return delta;
	}
}