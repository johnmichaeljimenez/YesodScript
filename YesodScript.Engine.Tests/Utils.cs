using YesodScript.Engine.Core;

namespace YesodScript.Engine.Tests;

public static class Utils
{
	internal static VirtualMachine Test(out string output, params string[] scriptFileNames)
	{
		var vmOutput = "";
		var vm = new VirtualMachine();

		if (Directory.Exists("core"))
			vm.CompileDirectory("core");

		foreach (var i in scriptFileNames)
		{
			var path = i.Replace("\\", "/"); //for linux
			vm.CompileFile($"scripts/{path}");
		}

		vm.OnOutput += (str) => { vmOutput += $"{str}\n"; };
		vm.Initialize();

		while (vm.IsRunning)
		{
			vm.Tick(0.033333f); //~30fps delta time
		}

		output = vmOutput.Trim();
		return vm;
	}
}