using Spectre.Console;
using YesodScript.Engine.Core;

var vm = new VirtualMachine();
vm.OnOutput += (msg) =>
{
	AnsiConsole.Clear();
	AnsiConsole.MarkupLine(msg);
};

vm.CompileFile("scripts/main.ysl");
vm.Initialize();

while (vm.IsRunning)
{
	vm.Tick(0.033f);

	if (vm.IsRunning)
	{
		Thread.Sleep(16);
	}
}

AnsiConsole.Console.Input.ReadKey(true);