using System.Globalization;
using System.Text;
using System.Text.Json;
using YesodScript.Engine.Core;

namespace YesodScript.Runtime;

public class SaveManager
{
	private readonly Dictionary<string, List<Var>> saveData = new(StringComparer.OrdinalIgnoreCase);
	private readonly ISaveStorage storage;

	public SaveManager(GameVM gameVM) : this(gameVM, null)
	{
	}

	public SaveManager(GameVM gameVM, ISaveStorage? storage)
	{
		this.storage = storage ?? new FileSaveStorage();
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
		var json = storage.Read(path);
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
		using var stream = new MemoryStream();
		using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
		{
			writer.WriteStartObject();
			foreach (var variable in vars)
			{
				writer.WritePropertyName(variable.Name);
				WriteValue(writer, variable);
			}
			writer.WriteEndObject();
		}

		var json = Encoding.UTF8.GetString(stream.ToArray());
		storage.Write(path, json);
	}

	public string GetSavePath(string filename)
	{
		if (!Directory.Exists("saves"))
			Directory.CreateDirectory("saves");

		return Path.Combine("saves", $"{filename}.json");
	}

	private static void WriteValue(Utf8JsonWriter writer, Var variable)
	{
		if (variable.Type == typeof(OpList))
		{
			if (variable.Value is not OpList list)
			{
				writer.WriteStartArray();
				writer.WriteEndArray();
				return;
			}

			writer.WriteStartArray();
			foreach (var item in list.Items)
				WriteScalar(writer, item, list.ElementType);
			writer.WriteEndArray();
			return;
		}

		WriteScalar(writer, variable.Value, variable.Type);
	}

	private static void WriteScalar(Utf8JsonWriter writer, object? value, Type type)
	{
		if (value is null)
		{
			writer.WriteNullValue();
			return;
		}

		if (type == typeof(bool) || value is bool)
		{
			writer.WriteBooleanValue(Convert.ToBoolean(value, CultureInfo.InvariantCulture));
			return;
		}

		if (type == typeof(int) || value is int)
		{
			writer.WriteNumberValue(Convert.ToInt32(value, CultureInfo.InvariantCulture));
			return;
		}

		if (type == typeof(float) || value is float)
		{
			writer.WriteNumberValue(Convert.ToSingle(value, CultureInfo.InvariantCulture));
			return;
		}

		if (type == typeof(string) || value is string)
		{
			writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
			return;
		}

		throw new NotSupportedException($"Unsupported save type '{type.Name}'.");
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
			if (type == typeof(bool))
				return false;
			if (type == typeof(int))
				return 0;
			if (type == typeof(float))
				return 0f;
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

		throw new NotSupportedException($"Unsupported save type '{type.Name}' for '{varName}'.");
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
