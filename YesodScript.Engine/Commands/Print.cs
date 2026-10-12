using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands
{
	public class Print : Command
	{
		[CommandParameter]
		private string msg = "<empty>";

		public override void OnEnter()
		{
			VM.OnOutput?.Invoke($"{msg}");
		}
	}
}