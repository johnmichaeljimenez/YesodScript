using System.Globalization;

namespace YesodScript.Engine.Core;

public class Var
{
	public string Name;
	public Type Type;   // only holds bool, string, int, float
	public object Value;

	public void SetValue(string effective, bool relative = false)
	{
		string s = effective.Trim();

		if (Type == typeof(bool))
		{
			if (!Utils.TryParseBool(s, out var res))
				throw new FormatException($"Cannot parse bool value '{effective}' for Var '{Name}'.");

			Value = res;
		}
		else if (Type == typeof(string))
		{
			Value = effective;
		}
		else if (Type == typeof(int))
		{
			int parsed = int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
			Value = relative ? Convert.ToInt32(Value, CultureInfo.InvariantCulture) + parsed : parsed;
		}
		else if (Type == typeof(float))
		{
			float parsed = float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
			Value = relative ? Convert.ToSingle(Value, CultureInfo.InvariantCulture) + parsed : parsed;
		}
		else
		{
			throw new NotSupportedException($"Unsupported Var type '{Type}' for Var '{Name}'.");
		}
	}
}