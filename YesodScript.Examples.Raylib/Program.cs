namespace YesodScript.Examples;

public static class Program
{
	[STAThread]
	public static void Main()
	{
		var host = new RaylibHost(GameVM.Create());
		host.Tick();
		host.Cleanup();
	}
}