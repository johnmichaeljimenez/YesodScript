using YesodScript.Runtime;

namespace YesodScript.Examples;

public static class Program
{
	[STAThread]
	public static void Main(string[] args)
	{
		new Bootstrap().Run(args);
	}
}
