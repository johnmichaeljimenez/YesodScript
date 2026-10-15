using System.Diagnostics;

namespace YesodScript.Engine.Commands
{
	public class Pause : Command
	{
		public override void OnEnter()
		{
			if (Debugger.IsAttached)
				Debugger.Break();
		}
	}
}