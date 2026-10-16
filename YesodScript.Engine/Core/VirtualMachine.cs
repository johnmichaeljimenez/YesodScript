using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using YesodScript.Engine.Commands;
using YesodScript.Engine.Tools;

namespace YesodScript.Engine.Core;

public class CommandType
{
	public string Name;
	public Type Type;
	public List<CommandParameter> Parameters;
}

public class CommandParameter
{
	public FieldInfo Field;
	public Type Type;
	public object DefaultValue;
}

[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class CommandParameterAttribute : Attribute { }

public class VirtualMachine : IDisposable
{
	public long CurrentTick { get; private set; }
	public readonly List<Var> Vars = new();

	private Dictionary<string, CommandType> commandTypes;

	private readonly List<Instruction> allCommands = new();
	private readonly List<Instruction> runningCommands = new();
	private readonly List<Instruction> _toRemove = new();
	private int _cloneCounter;

	public Random RNG { get; private set; }
	private int _randomSeed = -1;
	public int RandomSeed
	{
		get
		{
			return _randomSeed;
		}

		set
		{
			if (_randomSeed == value)
				return;

			_randomSeed = value;
			RNG = new Random(_randomSeed);
		}
	}

	public bool IsRunning => runningCommands.Count > 0;

	public Action<string> OnOutput;

	public VirtualMachine(int randomSeed = 0)
	{
		RandomSeed = randomSeed;
		InitRegistry();
	}

	public void DumpCommands(params string[] selectedCommands)
	{
		if (commandTypes == null || commandTypes.Count == 0)
			throw new InvalidOperationException("No commands found.");

		var commandsToDump = commandTypes.Values.ToList();
		if (selectedCommands != null && selectedCommands.Length > 0)
		{
			commandsToDump.RemoveAll(p => !selectedCommands.Contains(p.Name, StringComparer.InvariantCultureIgnoreCase));
		}

		var str = "";

		foreach (var i in commandsToDump.OrderBy(p => p.Name))
		{
			str += $"===[ {i.Name} ]===\n";

			if (i.Parameters.Count == 0)
			{
				str += $"	- <NO PARAMETERS>\n";
			}
			else
			{
				foreach (var j in i.Parameters)
				{
					var defValue = "*";
					if (j.DefaultValue != null && j.DefaultValue.ToString().Length > 0)
						defValue = j.DefaultValue.ToString();

					str += $"	- {j.Field.Name} ({j.Type.Name}) = {defValue}\n";
				}
			}

			str += "\n";
		}

		OnOutput?.Invoke(str);
	}

	public void Initialize()
	{
		OnInitialize();
		FireTrigger("_INIT");
		Tick(0);
	}

	public void Dispose()
	{
		OnDispose();
		_toRemove.Clear();
		runningCommands.Clear();
		allCommands.Clear();
	}

	public List<Instruction> FireTrigger(string triggerName)
	{
		var fired = new List<Instruction>();

		if (string.IsNullOrEmpty(triggerName))
			return fired;

		var parts = new List<string>();
		if (triggerName.Contains(' '))
		{
			parts = Utils.SplitArguments(triggerName);
			triggerName = parts[0];
			parts.RemoveAt(0);
		}

		foreach (var i in allCommands.ToArray())
		{
			foreach (var trigger in i.Triggers)
			{
				if (string.Equals(trigger.Key, triggerName, StringComparison.InvariantCultureIgnoreCase))
				{
					var parameters = trigger.Parameters.Keys.ToList();
					for (int j = 0; j < parts.Count; j++)
					{
						if (j < parameters.Count)
							trigger.Parameters[parameters[j]] = parts[j];
					}

					i.TriggerKey = trigger.Key;
					i.TriggerParameters.Clear();
					foreach (var kvp in trigger.Parameters)
					{
						i.TriggerParameters[kvp.Key] = kvp.Value;
					}

					Run(i, trigger.StartIndex);
					fired.Add(i);
					break;
				}
			}
		}

		return fired;
	}

	public void Run(string id)
	{
		var instruction = allCommands.FirstOrDefault(p => string.Equals(id, p.ID, StringComparison.InvariantCultureIgnoreCase));
		if (instruction == null)
			throw new InvalidOperationException($"Instruction '{id}' not found.");

		Run(instruction);
	}

	public void Run(Instruction instruction, int startIndex = 0)
	{
		if (instruction.IsRunning)
			return;

		if (instruction.Commands.Count == 0)
			return;

		instruction.IsRunning = true;
		instruction.IsPaused = false;
		instruction.ClearLocalVars();
		instruction.SetIndex(startIndex);

		if (!runningCommands.Contains(instruction))
			runningCommands.Add(instruction);
	}

	public void Stop(string id)
	{
		Stop(allCommands.FirstOrDefault(i => string.Equals(i.ID, id, StringComparison.OrdinalIgnoreCase)));
	}

	public void Stop(Instruction instruction)
	{
		if (instruction != null)
		{
			instruction.IsRunning = false;  //deferred
		}
	}

	public void RunInline(Instruction i, float deltaTime)
	{
		while (i.IsRunning && !i.IsPaused && i.CurrentIndex < i.Commands.Count)
		{
			var line = i.Commands[i.CurrentIndex];
			if (line is _Trigger) { i.IsRunning = false; break; }

			var currentIndex = i.CurrentIndex;
			bool updateDone;
			try { updateDone = line.OnTick(deltaTime); }
			catch (Exception ex) { throw Instruction.LineError(i, line, ex.Message); }

			if (!updateDone) break; //real wait

			try { line.OnExit(); }
			catch (Exception ex) { throw Instruction.LineError(i, line, ex.Message); }

			OnPostExecuteCommand(line);

			if (!i.IsRunning) break;
			if (currentIndex != i.CurrentIndex) continue; // jumped
			i.SetIndex(i.CurrentIndex + 1);
		}

		if (!i.IsRunning || i.CurrentIndex >= i.Commands.Count)
		{
			i.IsRunning = false;
			runningCommands.Remove(i);
		}
	}

	public void Tick(float deltaTime)
	{
		CurrentTick++;
		if (runningCommands.Count == 0)
			return;

		foreach (var i in runningCommands.ToArray())
		{
			if (i.IsPaused || i.CurrentIndex >= i.Commands.Count)
				continue;

			var line = i.Commands[i.CurrentIndex];

			if (line is _Trigger)
			{
				i.IsRunning = false;
				_toRemove.Add(i);
				continue;
			}

			while (i.IsRunning && !i.IsPaused && i.CurrentIndex < i.Commands.Count)
			{
				line = i.Commands[i.CurrentIndex];
				if (line is _Trigger)
				{
					i.IsRunning = false;
					break;
				}


				var currentIndex = i.CurrentIndex;
				var updateDone = false;

				try
				{
					updateDone = line.OnTick(deltaTime);
				}
				catch (Exception ex)
				{
					throw Instruction.LineError(i, line, ex.Message);
				}

				if (!updateDone)
					break; //waiting

				try
				{
					line.OnExit();
				}
				catch (Exception ex)
				{
					throw Instruction.LineError(i, line, ex.Message);
				}

				OnPostExecuteCommand(line);

				if (!i.IsRunning)
					break;

				if (currentIndex != i.CurrentIndex)
					continue; //jumped

				i.SetIndex(i.CurrentIndex + 1);
			}

			if (!i.IsRunning || i.CurrentIndex >= i.Commands.Count)
				_toRemove.Add(i);
		}

		for (int i = _toRemove.Count - 1; i >= 0; i--)
		{
			var ins = _toRemove[i];
			ins.IsRunning = false;
			runningCommands.Remove(ins);
			_toRemove.RemoveAt(i);
		}
	}

	private void InitRegistry()
	{
		var validTypes = new List<Type>();
		foreach (var t in AppDomain.CurrentDomain.GetAssemblies()
			.SelectMany(GetLoadableTypes)
			.Where(t => t.IsClass && !t.IsAbstract && typeof(Command).IsAssignableFrom(t) && t != typeof(Command)))
		{
			validTypes.Add(t);
		}

		commandTypes = new();
		foreach (var i in validTypes)
		{
			var instance = Activator.CreateInstance(i) as Command;
			if (instance == null)
				continue;

			var parameters = new List<CommandParameter>();
			var typeChain = new List<Type>();
			for (var t = i; t != null && t != typeof(object); t = t.BaseType)
				typeChain.Add(t);
			typeChain.Reverse();

			foreach (var declaringType in typeChain)
			{
				var fields = declaringType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				foreach (var field in fields)
				{
					if (field.GetCustomAttribute<CommandParameterAttribute>() == null)
						continue;

					parameters.Add(new CommandParameter
					{
						Field = field,
						Type = field.FieldType,
						DefaultValue = field.GetValue(instance)
					});
				}
			}

			var name = i.Name.ToUpper();
			commandTypes[name] = new CommandType
			{
				Name = name,
				Type = i,
				Parameters = parameters
			};
		}
	}

	public Instruction FindInstruction(string id)
	{
		return allCommands.FirstOrDefault(p => p.ID.Equals(id, StringComparison.InvariantCultureIgnoreCase));
	}

	
	public Instruction CloneInstruction(string sourceId, bool runInit = true)
	{
		var source = FindInstruction(sourceId);
		if (source == null)
			throw new InvalidOperationException($"Instruction '{sourceId}' not found.");

		string newId;
		do
		{
			newId = $"{source.ID}#{++_cloneCounter}";
		} while (FindInstruction(newId) != null);

		var clone = new Instruction { ID = newId };

		foreach (var t in source.Triggers)
		{
			clone.Triggers.Add(new TriggerDefinition
			{
				Key = t.Key,
				Parameters = new Dictionary<string, string>(t.Parameters),
				StartIndex = t.StartIndex
			});
		}

		foreach (var kv in source.Labels)
			clone.Labels[kv.Key] = kv.Value;

		for (int i = 0; i < source.Commands.Count; i++)
		{
			var orig = source.Commands[i];
			var cmdClone = orig.CreateClone(this, clone);
			clone.Commands.Add(cmdClone);
			if (source.LineReference.TryGetValue(orig, out var line))
				clone.LineReference[cmdClone] = line;
		}

		int trigIdx = 0;
		foreach (var cmd in clone.Commands)
		{
			if (cmd is _Trigger trig && trigIdx < clone.Triggers.Count)
			{
				var def = clone.Triggers[trigIdx++];
				trig.TriggerKey = def.Key;
				trig.Parameters = def.Parameters;
			}
		}

		allCommands.Add(clone);

		if (runInit)
		{
			var initTrig = clone.Triggers.FirstOrDefault(t =>
				string.Equals(t.Key, "_INIT", StringComparison.OrdinalIgnoreCase));
			if (initTrig != null)
			{
				clone.TriggerKey = initTrig.Key;
				clone.TriggerParameters.Clear();
				foreach (var kv in initTrig.Parameters)
					clone.TriggerParameters[kv.Key] = kv.Value;
				Run(clone, initTrig.StartIndex);
			}
		}

		return clone;
	}

public IEnumerable<Instruction> FindInstructions(Func<Instruction, string> selector, string query)
	{
		if (selector == null)
			throw new ArgumentNullException(nameof(selector));

		return allCommands.Where(i => Utils.IsMatch(selector(i), query));
	}

	private IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException e)
		{
			return e.Types.Where(t => t != null);
		}
	}

	public void CompileDirectory(string path, string ext = "ysl")
	{
		foreach (var file in DirectoryExtractor.GetPaths(path, ext))
		{
			CompileFile(file.Value, file.Key);
		}
	}

	public Instruction CompileFile(string path, string id = null)
	{
		if (string.IsNullOrEmpty(id))
		{
			id = Path.GetFileNameWithoutExtension(path);
		}

		var content = File.ReadAllText(path);
		return Compile(id, content);
	}

	public Instruction Compile(string id, string code)
	{
		if (string.IsNullOrEmpty(code))
			return null;

		var ins = new Instruction() { ID = id };
		var n = 0;

		var lineNumber = 0;

		foreach (var i in code.Trim().Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
		{
			lineNumber++;
			try
			{
				if (string.IsNullOrEmpty(i)) continue;

				var line = i.Trim();
				if (line.Length == 0) continue;
				if (line.StartsWith("#")) continue;

				if (line.StartsWith("@"))
				{
					var triggerLine = line.Substring(1).Trim();
					var lineParts = triggerLine.SplitArguments();
					var triggerKey = lineParts[0];

					var triggerParams = new Dictionary<string, string>();
					for (int j = 1; j < lineParts.Count; j++)
					{
						var varName = lineParts[j].Replace("<", "").Replace(">", "");
						triggerParams.Add(varName, "");
					}

					var cmd = new _Trigger
					{
						TriggerKey = triggerKey,
						Parameters = triggerParams
					};
					cmd.Initialize(this, ins, commandTypes[nameof(_Trigger).ToUpper()], Array.Empty<ArgumentToken>());
					cmd.OnInit();

					ins.Triggers.Add(new TriggerDefinition
					{
						Key = triggerKey,
						Parameters = triggerParams,
						StartIndex = n + 1
					});

					ins.Labels[triggerKey] = n + 1;
					if (ins.Triggers.Count == 1)
					{
						ins.TriggerKey = triggerKey;
						ins.TriggerParameters.Clear();
						foreach (var kvp in triggerParams)
							ins.TriggerParameters[kvp.Key] = kvp.Value;
					}

					ins.Commands.Add(cmd);
					ins.LineReference.Add(cmd, lineNumber);
					n++;
					continue;
				}

				if (line.StartsWith(">"))
				{
					var labelName = line.Substring(1).Trim();
					if (ins.Labels.ContainsKey(labelName))
						throw new InvalidOperationException($"Label '{labelName}' already exists");

					ins.Labels.Add(labelName, n);
					continue;
				}

				var parts = line.SplitArgumentTokens();
				var typeName = parts[0].Value.ToUpper();

				if (!commandTypes.TryGetValue(typeName, out var type))
					throw new Exception($"Unknown command: '{typeName}'");

				var command = Activator.CreateInstance(type.Type) as Command;
				command.Initialize(this, ins, type, parts.Skip(1).ToArray());
				command.OnInit();

				ins.Commands.Add(command);
				ins.LineReference.Add(command, lineNumber);
				n++;
			}
			catch (Exception ex)
			{
				throw new InvalidOperationException($"Compiler error at '{id}' line {lineNumber}: {ex.Message}");
			}
		}

		allCommands.Add(ins);
		return ins;
	}

	public void Unload(Instruction instruction)
	{
		allCommands.Remove(instruction);
		runningCommands.Remove(instruction);
	}

	protected virtual void OnInitialize()
	{

	}

	protected virtual void OnDispose()
	{

	}

	protected virtual void OnPostExecuteCommand(Command command)
	{

	}

	internal string ResolveReferences(string rawValue, Instruction ins)
	{
		if (string.IsNullOrEmpty(rawValue))
			return rawValue;

		var current = Regex.Replace(rawValue, @"\[\[(.*?)\]\]", match =>
		{
			var refName = match.Groups[1].Value.Trim();
			var referenced = GetVar(refName, ins);

			if (referenced == null)
				throw new InvalidOperationException($"Referenced Var '{refName}' was not found.");

			if (referenced.Value == null)
				throw new InvalidOperationException($"Referenced Var '{refName}' has no value.");

			return Convert.ToString(referenced.Value, CultureInfo.InvariantCulture);
		});

		current = Regex.Replace(current, @"<(.*?)>", match =>
		   {
			   var refName = match.Groups[1].Value.Trim();
			   var localValue = ins.TriggerParameters[refName];

			   if (localValue == null)
				   throw new InvalidOperationException($"Local parameter '{refName}' was not found.");

			   return Convert.ToString(localValue, CultureInfo.InvariantCulture);
		   });

		return current;
	}

	public Var GetVar(string name, Instruction scope)
	{
		if (string.IsNullOrEmpty(name))
			return null;

		if (scope != null && name[0] == '_')
			return scope.Vars.FirstOrDefault(v =>
				string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

		return Vars.FirstOrDefault(v =>
			string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));
	}

	public Var AddGlobalVar(string name, Type type, object value)
	{
		var newVar = new Var()
		{
			Name = name,
			Type = type,
			Value = value
		};

		Vars.Add(newVar);
		return newVar;
	}

	public Var AddGlobalList(string name, Type type)
	{
		var newVar = new Var()
		{
			Name = name,
			Type = typeof(OpList),
			Value = new OpList(type)
		};

		Vars.Add(newVar);
		return newVar;
	}
}
