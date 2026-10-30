using System.Globalization;
using System.Numerics;
using System.Text.Json;
using YesodScript.Engine.Core;

namespace YesodScript.Examples;

public class SaveManager
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = true
	};

	private readonly Dictionary<string, List<Var>> saveData = new(StringComparer.OrdinalIgnoreCase);

	public SaveManager(GameVM gameVM)
	{
	}

	public void RegisterVar(string group, Var variable)
	{
		if (variable.Name.StartsWith("_", StringComparison.InvariantCultureIgnoreCase)
			|| variable.Name.StartsWith("SYS:", StringComparison.InvariantCultureIgnoreCase))
		{
			throw new Exception($"Cannot register save variable '{variable.Name}' (local or system)");
		}

		if (!saveData.TryGetValue(group, out var list))
		{
			list = new List<Var>();
			saveData[group] = list;
		}

		if (list.Any(v => string.Equals(v.Name, variable.Name, StringComparison.OrdinalIgnoreCase)))
			return;

		list.Add(variable);
	}

	public void Load(string group, string filename)
	{
		if (!saveData.TryGetValue(group, out var vars))
			throw new Exception($"No save data registered for group '{group}'");

		var path = GetSavePath(filename);
		if (!File.Exists(path))
			return;

		var json = File.ReadAllText(path);
		if (string.IsNullOrWhiteSpace(json))
			return;

		using var doc = JsonDocument.Parse(json);
		if (doc.RootElement.ValueKind != JsonValueKind.Object)
			throw new InvalidDataException($"Save file '{path}' root must be a JSON object.");

		foreach (var variable in vars)
		{
			if (!TryGetPropertyIgnoreCase(doc.RootElement, variable.Name, out var element))
				continue;

			variable.Value = DeserializeValue(element, variable);
		}
	}

	public void Save(string group, string filename)
	{
		if (!saveData.TryGetValue(group, out var vars))
			throw new Exception($"No save data registered for group '{group}'");

		var path = GetSavePath(filename);
		var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

		foreach (var variable in vars)
			data[variable.Name] = SerializeValue(variable);

		var json = JsonSerializer.Serialize(data, JsonOptions);
		File.WriteAllText(path, json);
	}

	public string GetSavePath(string filename)
	{
		if (!Directory.Exists("saves"))
			Directory.CreateDirectory("saves");

		return Path.Combine("saves", $"{filename}.json");
	}

	private static object? SerializeValue(Var variable)
	{
		if (variable.Type == typeof(OpList))
		{
			if (variable.Value is not OpList list)
				return Array.Empty<object>();

			return list.Items.Select(item => SerializeScalar(item, list.ElementType)).ToArray();
		}

		return SerializeScalar(variable.Value, variable.Type);
	}

	private static object? SerializeScalar(object? value, Type type)
	{
		if (value is null)
			return null;

		if (type == typeof(Vector2) || value is Vector2)
			return Utils.FormatVector2((Vector2)value);

		if (type == typeof(bool) || value is bool)
			return Convert.ToBoolean(value, CultureInfo.InvariantCulture);

		if (type == typeof(int) || value is int)
			return Convert.ToInt32(value, CultureInfo.InvariantCulture);

		if (type == typeof(float) || value is float)
			return Convert.ToSingle(value, CultureInfo.InvariantCulture);

		if (type == typeof(string) || value is string)
			return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;

		return value;
	}

	private static object DeserializeValue(JsonElement element, Var variable)
	{
		if (variable.Type == typeof(OpList))
		{
			var list = variable.Value as OpList
				?? throw new InvalidOperationException($"Var '{variable.Name}' is typed as OpList but has no list instance.");

			list.Items.Clear();

			if (element.ValueKind == JsonValueKind.Null)
				return list;

			if (element.ValueKind != JsonValueKind.Array)
				throw new InvalidDataException($"Save value for list '{variable.Name}' must be a JSON array.");

			foreach (var item in element.EnumerateArray())
				list.Items.Add(DeserializeScalar(item, list.ElementType, variable.Name));

			return list;
		}

		return DeserializeScalar(element, variable.Type, variable.Name);
	}

	private static object DeserializeScalar(JsonElement element, Type type, string varName)
	{
		if (element.ValueKind == JsonValueKind.Null)
		{
			if (type == typeof(string))
				return string.Empty;
			if (type.IsValueType)
				return Activator.CreateInstance(type)!;
			throw new InvalidDataException($"Null is not valid for '{varName}' ({type.Name}).");
		}

		if (type == typeof(bool))
		{
			if (element.ValueKind == JsonValueKind.True) return true;
			if (element.ValueKind == JsonValueKind.False) return false;
			if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n))
				return n != 0;
			if (element.ValueKind == JsonValueKind.String
				&& Utils.TryParseBool(element.GetString() ?? string.Empty, out var b))
				return b;

			throw new InvalidDataException($"Cannot parse bool for '{varName}' from {element.ValueKind}.");
		}

		if (type == typeof(int))
		{
			if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var n))
				return n;
			if (element.ValueKind == JsonValueKind.String
				&& int.TryParse(element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
				return n;

			if (element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var d)
				&& Math.Abs(d - Math.Round(d)) < 1e-9)
				return (int)Math.Round(d);

			throw new InvalidDataException($"Cannot parse int for '{varName}' from {element.ValueKind}.");
		}

		if (type == typeof(float))
		{
			if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var f))
				return f;
			if (element.ValueKind == JsonValueKind.String
				&& float.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
				return f;

			throw new InvalidDataException($"Cannot parse float for '{varName}' from {element.ValueKind}.");
		}

		if (type == typeof(string))
		{
			return element.ValueKind switch
			{
				JsonValueKind.String => element.GetString() ?? string.Empty,
				JsonValueKind.Number => element.GetRawText(),
				JsonValueKind.True => "true",
				JsonValueKind.False => "false",
				_ => element.GetRawText()
			};
		}

		if (type == typeof(Vector2))
		{
			if (element.ValueKind == JsonValueKind.String)
			{
				if (Utils.TryParseVector2(element.GetString() ?? string.Empty, out var v))
					return v;
			}
			else if (element.ValueKind == JsonValueKind.Object)
			{
				float x = 0, y = 0;
				if (TryGetPropertyIgnoreCase(element, "X", out var xEl)
					|| TryGetPropertyIgnoreCase(element, "x", out xEl))
					x = ReadFloat(xEl, varName + ".X");
				if (TryGetPropertyIgnoreCase(element, "Y", out var yEl)
					|| TryGetPropertyIgnoreCase(element, "y", out yEl))
					y = ReadFloat(yEl, varName + ".Y");
				return new Vector2(x, y);
			}
			else if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() == 2)
			{
				var arr = element.EnumerateArray().ToArray();
				return new Vector2(ReadFloat(arr[0], varName + "[0]"), ReadFloat(arr[1], varName + "[1]"));
			}

			throw new InvalidDataException($"Cannot parse Vector2 for '{varName}'.");
		}

		throw new NotSupportedException($"Unsupported save type '{type.Name}' for '{varName}'.");
	}

	private static float ReadFloat(JsonElement element, string field)
	{
		if (element.ValueKind == JsonValueKind.Number && element.TryGetSingle(out var f))
			return f;
		if (element.ValueKind == JsonValueKind.String
			&& float.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out f))
			return f;

		throw new InvalidDataException($"Cannot parse float for '{field}'.");
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
	{
		foreach (var prop in obj.EnumerateObject())
		{
			if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
			{
				value = prop.Value;
				return true;
			}
		}

		value = default;
		return false;
	}
}
