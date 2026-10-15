using System.Globalization;
using YesodScript.Engine.Core;

namespace YesodScript.Engine.Commands
{
	public class Jump : Command
	{
		[CommandParameter]
		private string labelName = "";

		public override void OnEnter()
		{
			Instruction.Jump(labelName);
		}
	}

	public class JumpBetween : Command
	{
		[CommandParameter]
		private string x1 = "";
		[CommandParameter]
		private string y1 = "";
		[CommandParameter]
		private string x2 = "";
		[CommandParameter]
		private string y2 = "";
		[CommandParameter]
		private string labelName = "";

		public override void OnEnter()
		{
			var valX1 = ResolveOperand(x1);
			var valY1 = ResolveOperand(y1);
			var valX2 = ResolveOperand(x2);
			var valY2 = ResolveOperand(y2);

			if (TryGetNumber(valX1, out var fx1) &&
				TryGetNumber(valY1, out var fy1) &&
				TryGetNumber(valX2, out var fx2) &&
				TryGetNumber(valY2, out var fy2))
			{
				//normalize range
				float min1 = Math.Min(fx1, fy1);
				float max1 = Math.Max(fx1, fy1);
				float min2 = Math.Min(fx2, fy2);
				float max2 = Math.Max(fx2, fy2);

				if (min1 <= max2 && min2 <= max1)
				{
					Instruction.Jump(labelName);
				}
			}
		}

		private object ResolveOperand(string operand)
		{
			var var = VM.GetVar(operand, Instruction);
			return var != null ? var.Value : operand;
		}

		private static bool TryGetNumber(object value, out float number)
		{
			if (value is int i)
			{
				number = i;
				return true;
			}

			if (value is float f)
			{
				number = f;
				return true;
			}

			return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
				NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}
	}

	public class JumpIf : Command
	{
		[CommandParameter]
		private string key = "";
		[CommandParameter]
		private string labelName = "";

		public override void OnEnter()
		{
			var baseKey = key;
			var invert = Utils.HasPrefix("!", ref baseKey);
			var var = VM.GetVar(baseKey, Instruction);
			Utils.TryParseBool(var.Value.ToString(), out var boolValue);

			if (invert)
				boolValue = !boolValue;

			if (boolValue)
				Instruction.Jump(labelName);
		}
	}

	public abstract class JumpCompare : Command
	{
		[CommandParameter]
		private string left = "";

		[CommandParameter]
		private string right = "";

		[CommandParameter]
		private string labelName = "";

		protected abstract bool Compare(int comparisonResult);

		public override void OnEnter()
		{
			var a = ResolveOperand(left);
			var b = ResolveOperand(right);

			if (Compare(CompareValues(a, b)))
				Instruction.Jump(labelName);
		}

		private object ResolveOperand(string operand)
		{
			var var = VM.GetVar(operand, Instruction);
			return var != null ? var.Value : operand;
		}

		private static int CompareValues(object a, object b)
		{
			if (TryGetNumber(a, out var x) && TryGetNumber(b, out var y))
				return x.CompareTo(y);

			return string.Compare(
				Convert.ToString(a, CultureInfo.InvariantCulture),
				Convert.ToString(b, CultureInfo.InvariantCulture),
				StringComparison.OrdinalIgnoreCase);
		}

		private static bool TryGetNumber(object value, out float number)
		{
			if (value is int i)
			{
				number = i;
				return true;
			}

			if (value is float f)
			{
				number = f;
				return true;
			}

			return float.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
				NumberStyles.Float, CultureInfo.InvariantCulture, out number);
		}
	}

	public class JumpEQ : JumpCompare
	{
		protected override bool Compare(int comparisonResult) => comparisonResult == 0;
	}

	public class JumpNEQ : JumpCompare
	{
		protected override bool Compare(int comparisonResult) => comparisonResult != 0;
	}

	public class JumpGT : JumpCompare
	{
		protected override bool Compare(int comparisonResult) => comparisonResult > 0;
	}

	public class JumpLT : JumpCompare
	{
		protected override bool Compare(int comparisonResult) => comparisonResult < 0;
	}

	public class JumpGE : JumpCompare
	{
		protected override bool Compare(int comparisonResult) => comparisonResult >= 0;
	}

	public class JumpLE : JumpCompare
	{
		protected override bool Compare(int comparisonResult) => comparisonResult <= 0;
	}
}