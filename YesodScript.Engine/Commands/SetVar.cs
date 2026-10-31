using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands;

public class SetVar : Command
{
	[CommandParameter]
	private string key;

	[CommandParameter]
	private string value = "";

	[CommandParameter]
	private bool relative;

	public override void OnEnter()
	{
		base.OnEnter();
		var v = VM.GetVar(key, Instruction)
			?? throw new InvalidOperationException($"Var '{key}' not found.");
		v.SetValue(value, relative);
	}
}

public class DefVar : Command
{
	[CommandParameter]
	private string key;

	[CommandParameter]
	private string value = "";

	[CommandParameter]
	private string typeName;

	public override void OnEnter()
	{
		base.OnEnter();

		var isLocal = key != null && key.StartsWith("_", StringComparison.Ordinal);
		var vars = isLocal ? Instruction.Vars : VM.Vars;

		if (vars.Any(v => string.Equals(v.Name, key, StringComparison.OrdinalIgnoreCase)))
			throw new Exception($"Var '{key}' already exists.");

		var newVar = new Var { Name = key, Type = GetStringType() };
		vars.Add(newVar);
		newVar.SetValue(value, false);
	}

	private Type GetStringType()
	{
		return typeName?.ToLowerInvariant() switch
		{
			"int" => typeof(int),
			"float" => typeof(float),
			"string" => typeof(string),
			"bool" => typeof(bool),
			_ => null
		};
	}
}