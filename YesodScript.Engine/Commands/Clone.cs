using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands
{
	public class Clone : Command
	{
		[CommandParameter]
		private string id;

		[CommandParameter]
		private string saveTo;

		public override void OnEnter()
		{
			var clone = VM.CloneInstruction(id, runInit: true);

			if (string.IsNullOrEmpty(saveTo))
				return;

			var v = VM.GetVar(saveTo, Instruction);
			if (v == null)
			{
				var isLocal = saveTo.StartsWith("_", StringComparison.Ordinal);
				var vars = isLocal ? Instruction.Vars : VM.Vars;
				v = new Var
				{
					Name = saveTo,
					Type = typeof(string),
					Value = clone.ID
				};
				vars.Add(v);
			}
			else
			{
				if (v.Type != typeof(string))
					throw new InvalidOperationException($"Var '{saveTo}' must be of type string to store a clone ID.");
				v.Value = clone.ID;
			}
		}
	}
}
