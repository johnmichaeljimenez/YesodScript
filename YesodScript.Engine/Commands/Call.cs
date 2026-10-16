using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands
{
	public class Call : Command
	{
		[CommandParameter]
		private string id;

		[CommandParameter]
		private string labelWhenEmpty;

		[CommandParameter]
		private bool fireAndForget = false;

		private List<Instruction> _fired;

		public override void OnEnter()
		{
			_fired = VM.FireTrigger(id);
			if (_fired.Count == 0 && !string.IsNullOrEmpty(labelWhenEmpty))
				Instruction.Jump(labelWhenEmpty);
		}

		public override bool OnTick(float deltaTime)
		{
			if (fireAndForget || _fired == null || _fired.Count == 0)
				return true;

			foreach (var ins in _fired)
			{
				if (!ins.IsRunning) continue;

				VM.RunInline(ins, deltaTime); //run immediately
				if (ins.IsRunning)
					return false;
			}

			return true;
		}
	}
}
