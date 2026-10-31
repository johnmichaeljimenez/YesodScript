using System.Globalization;
using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands
{
	public abstract class VarMath : Command
	{
		[CommandParameter]
		private string key = "";

		[CommandParameter]
		private string value = "0";

		protected abstract float Apply(float a, float b);

		public override void OnEnter()
		{
			base.OnEnter();

			var var = VM.GetVar(key, Instruction);

			if (var == null)
				throw new InvalidOperationException($"Var '{key}' was not found.");

			if (var.Type != typeof(int) && var.Type != typeof(float))
				throw new NotSupportedException($"Var '{key}' is of type '{var.Type.Name}'; math operations require int or float.");

			var operand = ResolveOperand(value);

			if (!TryGetNumber(operand, out var scalarB))
				throw new FormatException($"Could not parse '{value}' as float for math on Var '{key}'.");

			var a = Convert.ToSingle(var.Value, CultureInfo.InvariantCulture);
			var result = Apply(a, scalarB);

			var.Value = var.Type == typeof(int)
				? (object)(int)(float.Truncate(result))
				: (float)result;
		}

		private object ResolveOperand(string operand)
		{
			var referenced = VM.GetVar(operand, Instruction);
			if (referenced != null)
				return referenced.Value;

			return operand;
		}

		private static bool TryGetNumber(object value, out float number)
		{
			switch (value)
			{
				case int i:
					number = i;
					return true;
				case float f:
					number = f;
					return true;
				default:
					return float.TryParse(
						Convert.ToString(value, CultureInfo.InvariantCulture),
						NumberStyles.Float,
						CultureInfo.InvariantCulture,
						out number);
			}
		}
	}

	public class Add : VarMath
	{
		protected override float Apply(float a, float b) => a + b;
	}

	public class Sub : VarMath
	{
		protected override float Apply(float a, float b) => a - b;
	}

	public class Mul : VarMath
	{
		protected override float Apply(float a, float b) => a * b;
	}

	public class Div : VarMath
	{
		protected override float Apply(float a, float b) => a / b;
	}

	public class Sqrt : Command
	{
		[CommandParameter] private string key = "";

		public override void OnEnter()
		{
			var v = VM.GetVar(key, Instruction)
				?? throw new InvalidOperationException($"Var '{key}' was not found.");

			if (v.Type != typeof(int) && v.Type != typeof(float))
				throw new NotSupportedException($"Var '{key}' is of type '{v.Type.Name}'; SQRT requires int or float.");

			var result = MathF.Sqrt(Convert.ToSingle(v.Value, CultureInfo.InvariantCulture));
			v.Value = v.Type == typeof(int)
				? (object)(int)float.Truncate(result)
				: result;
		}
	}
}
