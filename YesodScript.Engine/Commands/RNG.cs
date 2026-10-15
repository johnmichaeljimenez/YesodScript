using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands;

public class RNG : Command
{
	[CommandParameter]
	private float range;
	[CommandParameter]
	private string labelName;

	public override void OnEnter()
	{
		var value = VM.RNG.NextSingle();
		if (value <= range)
			Instruction.Jump(labelName);
	}
}

public class RNGRange : Command
{
	[CommandParameter]
	private float min;
	[CommandParameter]
	private float max;
	[CommandParameter]
	private string labelName;

	public override void OnEnter()
	{
		var value = VM.RNG.NextSingle();
		if (value >= min && value <= max)
			Instruction.Jump(labelName);
	}
}

public class RandVar : Command
{
    [CommandParameter] private string key;
    [CommandParameter] private float min;
    [CommandParameter] private float max;

    public override void OnEnter()
    {
        var v = VM.GetVar(key, Instruction)
            ?? throw new InvalidOperationException($"Var '{key}' not found.");

        var value = min + VM.RNG.NextSingle() * (max - min);
        v.Value = v.Type == typeof(int) ? (object)(int)MathF.Round(value) : value;
    }
}