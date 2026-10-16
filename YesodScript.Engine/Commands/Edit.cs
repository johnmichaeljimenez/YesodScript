using YesodScript.Engine.Core;
using YesodScript.Engine.Tools;

namespace YesodScript.Engine.Commands;

public abstract class EditPropertyCommand<T> : Command
{
	[CommandParameter]
	protected int id;

	[CommandParameter]
	protected string path;

	[CommandParameter]
	protected string value = "";

	protected T instance { get; private set; }

	public override void OnEnter()
	{
		instance = GetInstance(id);
		dataMembers[path].SetParsed(instance, value);
	}

	protected virtual T GetInstance(int id) => throw new NotImplementedException();
	protected virtual IReadOnlyDictionary<string, PathMember> dataMembers => throw new NotImplementedException();
}