using System;
using System.Collections.Generic;
using YesodScript.Engine.Commands;

namespace YesodScript.Engine.Core
{
	public class TriggerDefinition
	{
		public string Key { get; set; }
		public Dictionary<string, string> Parameters { get; set; } = new();
		public int StartIndex { get; set; }
	}

	public class Instruction
	{
		public string ID { get; internal set; }

		public string TriggerKey { get; internal set; }
		public readonly Dictionary<string, string> TriggerParameters = new();
		public readonly List<TriggerDefinition> Triggers = new();

		public int CurrentIndex { get; private set; }
		public bool IsPaused { get; set; }
		public bool IsRunning { get; internal set; }

		public readonly List<Command> Commands = new();
		public readonly Dictionary<string, int> Labels = new();
		public readonly Dictionary<Command, int> LineReference = new();

		public readonly List<Var> Vars = new();

		public void SetIndex(int index)
		{
			CurrentIndex = index;

			if (index >= Commands.Count)
				return;

			try
			{
				var cmd = Commands[CurrentIndex];
				cmd.ResolveDynamicParameters();
				cmd.OnEnter();
			}
			catch (Exception ex)
			{
				throw LineError(this, Commands[CurrentIndex], ex.Message);
			}
		}

		public void Jump(string name)
		{
			if (!Labels.TryGetValue(name, out var index))
			{
				throw new InvalidOperationException($"Label '{name}' not found.");
			}

			SetIndex(index);
		}

		internal void ClearLocalVars()
		{
			Vars.Clear();
		}

		public static Exception LineError(Instruction ins, Command cmd, string msg)
		{
			throw new InvalidOperationException($"Runtime error at '{ins.ID}' line {ins.LineReference[cmd]}: {msg}");
		}
	}
}