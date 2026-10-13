using Raylib_cs;
using YesodScript.Engine.Commands;
using YesodScript.Engine.Core;

namespace YesodScript.Examples;

public class GameVM : VirtualMachine
{
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
}

public class GameCommands : Command
{
	protected GameVM GameVM => (GameVM)VM;
}