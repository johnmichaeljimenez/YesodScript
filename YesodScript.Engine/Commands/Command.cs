using System.Globalization;
using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands
{
	public abstract class Command
	{
		protected VirtualMachine VM { get; private set; }
		protected Instruction Instruction { get; private set; }

		private CommandType _commandType;
		private ArgumentToken[] _rawArgs;

		private Dictionary<int, string> _deferredArgs;

		internal void Initialize(VirtualMachine vm, Instruction instruction, CommandType commandType, ArgumentToken[] args)
		{
			VM = vm;
			Instruction = instruction;
			_commandType = commandType;
			_rawArgs = args;
			_deferredArgs = new Dictionary<int, string>();

			for (int i = 0; i < commandType.Parameters.Count; i++)
			{
				var param = commandType.Parameters[i];
				bool hasArg = i < args.Length && !args[i].IsDefault;

				if (hasArg && !IsReference(args[i].Value))
				{
					try
					{
						object value = ConvertArgument(args[i].Value, param.Type, param.Field.Name);
						param.Field.SetValue(this, value);
					}
					catch (Exception ex) when (ex is FormatException || ex is ArgumentException)
					{
						_deferredArgs[i] = args[i].Value;
						param.Field.SetValue(this, param.DefaultValue);
					}
				}
				else
				{
					param.Field.SetValue(this, param.DefaultValue);
				}
			}
		}

		internal void ResolveDynamicParameters()
		{
			if (_commandType == null || _rawArgs == null) return;

			for (int i = 0; i < _commandType.Parameters.Count; i++)
			{
				if (i >= _rawArgs.Length) continue;
				if (_rawArgs[i].IsDefault) continue;
				string raw = _rawArgs[i].Value;

				bool isRef = IsReference(raw);
				bool isDeferred = _deferredArgs != null && _deferredArgs.ContainsKey(i);

				if (!isRef && !isDeferred) continue;

				var param = _commandType.Parameters[i];
				string resolved = VM.ResolveReferences(raw, Instruction);

				if (resolved == raw && isDeferred)
				{
					var nakedVar = VM.GetVar(raw, Instruction);
					if (nakedVar != null && nakedVar.Value != null)
					{
						resolved = Utils.FormatValue(nakedVar.Value);
					}
				}

				object value = param.Type == typeof(string)
					? resolved
					: ConvertArgument(resolved.Trim(), param.Type, param.Field.Name);

				param.Field.SetValue(this, value);
			}
		}

		private static bool IsReference(string arg)
		{
			if (string.IsNullOrEmpty(arg))
				return false;

			return arg.Contains("[[") || arg.Contains("<");
		}

		internal static object ConvertArgument(string arg, Type type, string fieldName)
		{
			if (type == typeof(string))
				return arg;
				
			if (string.IsNullOrWhiteSpace(arg))
			{
				return type.IsValueType ? Activator.CreateInstance(type) : null;
			}

			if (type == typeof(bool))
			{
				if (Utils.TryParseBool(arg, out var b))
					return b;

				throw new FormatException($"Could not parse '{arg}' as bool for parameter '{fieldName}'");
			}

			if (type == typeof(float))
			{
				if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
					return f;

				throw new FormatException($"Could not parse '{arg}' as float for parameter '{fieldName}'");
			}

			if (type == typeof(int))
			{
				if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
					return n;

				throw new FormatException($"Could not parse '{arg}' as int for parameter '{fieldName}'");
			}

			if (type == typeof(System.Numerics.Vector2))
			{
				if (Utils.TryParseVector2(arg, out var v))
					return v;

				throw new FormatException($"Could not parse '{arg}' as vector2 for parameter '{fieldName}'");
			}

			if (type.IsEnum)
			{
				return Enum.Parse(type, arg, ignoreCase: true);
			}

			throw new NotSupportedException($"Unsupported command parameter type '{type}' for parameter '{fieldName}'");
		}

		public virtual void OnInit()
		{

		}

		public virtual void OnEnter()
		{

		}

		public virtual void OnExit()
		{

		}

		public virtual bool OnTick(float deltaTime) => true;

		internal virtual Command CreateClone(VirtualMachine vm, Instruction newInstruction)
		{
			var clone = (Command)Activator.CreateInstance(GetType());
			clone.Initialize(vm, newInstruction, _commandType, _rawArgs ?? Array.Empty<ArgumentToken>());
			return clone;
		}

	}
}