using System.Globalization;
using System.Numerics;
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
		protected abstract Vector2 Apply(Vector2 a, Vector2 b);

		public override void OnEnter()
		{
			base.OnEnter();

			var var = VM.GetVar(key, Instruction);

			if (var == null)
				throw new InvalidOperationException($"Var '{key}' was not found.");

			var operand = ResolveOperand(value);

			if (var.Type == typeof(Vector2))
			{
				if (operand is Vector2 vb)
				{
					var.Value = Apply((Vector2)var.Value, vb);
					return;
				}

				if (operand is string s && Utils.TryParseVector2(s, out var parsed))
				{
					var.Value = Apply((Vector2)var.Value, parsed);
					return;
				}

				throw new FormatException($"Could not parse '{value}' as vector2 for math on Var '{key}'.");
			}

			if (var.Type != typeof(int) && var.Type != typeof(float))
				throw new NotSupportedException($"Var '{key}' is of type '{var.Type.Name}'; math operations require int, float, or vector2.");

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
		protected override Vector2 Apply(Vector2 a, Vector2 b) => a + b;
	}

	public class Sub : VarMath
	{
		protected override float Apply(float a, float b) => a - b;
		protected override Vector2 Apply(Vector2 a, Vector2 b) => a - b;
	}

	public class Mul : VarMath
	{
		protected override float Apply(float a, float b) => a * b;
		protected override Vector2 Apply(Vector2 a, Vector2 b) => a * b;
	}

	public class Div : VarMath
	{
		protected override float Apply(float a, float b) => a / b;
		protected override Vector2 Apply(Vector2 a, Vector2 b) => a / b;
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

	public abstract class VectorUnaryOutCommand : Command
	{
		[CommandParameter] private string key = "";
		[CommandParameter] private string outVar = "";

		protected abstract float Compute(Vector2 source);

		public override void OnEnter()
		{
			WriteFloat(outVar, Compute(GetVector(key)));
		}

		private Vector2 GetVector(string name)
		{
			var v = VM.GetVar(name, Instruction)
				?? throw new InvalidOperationException($"Var '{name}' was not found.");
			if (v.Type != typeof(Vector2) || v.Value is not Vector2 vec)
				throw new NotSupportedException($"Var '{name}' is of type '{v.Type.Name}'; expected vector2.");
			return vec;
		}

		private void WriteFloat(string name, float value)
		{
			var v = VM.GetVar(name, Instruction)
				?? throw new InvalidOperationException($"Var '{name}' was not found.");
			if (v.Type == typeof(float))
				v.Value = value;
			else if (v.Type == typeof(int))
				v.Value = (int)float.Truncate(value);
			else
				throw new NotSupportedException($"Var '{name}' is of type '{v.Type.Name}'; expected int or float output.");
		}
	}

	public class VLength : VectorUnaryOutCommand
	{
		protected override float Compute(Vector2 source) => source.Length();
	}

	public class VLengthSquared : VectorUnaryOutCommand
	{
		protected override float Compute(Vector2 source) => source.LengthSquared();
	}

	public class VNormalize : Command
	{
		[CommandParameter] private string key = "";

		public override void OnEnter()
		{
			var v = VM.GetVar(key, Instruction)
				?? throw new InvalidOperationException($"Var '{key}' was not found.");
			if (v.Type != typeof(Vector2) || v.Value is not Vector2 source)
				throw new NotSupportedException($"Var '{key}' is of type '{v.Type.Name}'; expected vector2.");

			v.Value = source.LengthSquared() <= float.Epsilon ? Vector2.Zero : Vector2.Normalize(source);
		}
	}

	public class VScale : Command
	{
		[CommandParameter] private string key = "";
		[CommandParameter] private float value = 1f;

		public override void OnEnter()
		{
			var v = VM.GetVar(key, Instruction)
				?? throw new InvalidOperationException($"Var '{key}' was not found.");
			if (v.Type != typeof(Vector2) || v.Value is not Vector2 source)
				throw new NotSupportedException($"Var '{key}' is of type '{v.Type.Name}'; expected vector2.");

			v.Value = source * value;
		}
	}

	public class VDot : Command
	{
		[CommandParameter] private string left = "";
		[CommandParameter] private string right = "";
		[CommandParameter] private string outVar = "";

		public override void OnEnter()
		{
			WriteFloat(outVar, Vector2.Dot(GetVector(left), GetVector(right)));
		}

		private Vector2 GetVector(string name)
		{
			var v = VM.GetVar(name, Instruction)
				?? throw new InvalidOperationException($"Var '{name}' was not found.");
			if (v.Type != typeof(Vector2) || v.Value is not Vector2 vec)
				throw new NotSupportedException($"Var '{name}' is of type '{v.Type.Name}'; expected vector2.");
			return vec;
		}

		private void WriteFloat(string name, float value)
		{
			var v = VM.GetVar(name, Instruction)
				?? throw new InvalidOperationException($"Var '{name}' was not found.");
			if (v.Type == typeof(float))
				v.Value = value;
			else if (v.Type == typeof(int))
				v.Value = (int)float.Truncate(value);
			else
				throw new NotSupportedException($"Var '{name}' is of type '{v.Type.Name}'; expected int or float output.");
		}
	}

	public class JumpDistance : Command
	{
		[CommandParameter] private string left = "";
		[CommandParameter] private string right = "";
		[CommandParameter] private float distance = 0f;
		[CommandParameter] private string labelName = "";

		public override void OnEnter()
		{
			var a = GetVector(left);
			var b = GetVector(right);

			if (Vector2.DistanceSquared(a, b) <= distance * distance)
				Instruction.Jump(labelName);
		}

		private Vector2 GetVector(string name)
		{
			var v = VM.GetVar(name, Instruction)
				?? throw new InvalidOperationException($"Var '{name}' was not found.");
			if (v.Type != typeof(Vector2) || v.Value is not Vector2 vec)
				throw new NotSupportedException($"Var '{name}' is of type '{v.Type.Name}'; expected vector2.");
			return vec;
		}
	}
}
