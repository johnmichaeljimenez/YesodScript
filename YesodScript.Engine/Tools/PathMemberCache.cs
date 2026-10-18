using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace YesodScript.Engine.Tools;

public sealed class PathMember
{
	private static readonly HashSet<Type> AllowedLeafTypes = new()
	{
		typeof(string),
		typeof(int),
		typeof(float),  //no double
		typeof(bool),
		typeof(byte),
		typeof(System.Numerics.Vector2),
	};

	private readonly MemberInfo[] _chain;
	private readonly string[] _segmentNames;

	internal PathMember(string path, IReadOnlyList<MemberInfo> chain)
	{
		if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is required.", nameof(path));
		if (chain is null || chain.Count == 0) throw new ArgumentException("Chain must contain at least one member.", nameof(chain));

		Path = path;
		_chain = chain.ToArray();
		_segmentNames = _chain.Select(m => m.Name).ToArray();
		ValueType = GetMemberType(_chain[^1]);
	}

	public string Path { get; }
	public Type ValueType { get; }
	public bool IsField => _chain[^1] is FieldInfo;
	public bool IsProperty => _chain[^1] is PropertyInfo;
	public int Depth => _chain.Length;

	public static bool IsAllowedLeafType(Type type) =>
		AllowedLeafTypes.Contains(Nullable.GetUnderlyingType(type) ?? type);

	public object? Get(object root)
	{
		ArgumentNullException.ThrowIfNull(root);

		object? current = root;
		for (int i = 0; i < _chain.Length; i++)
		{
			if (current is null)
				throw new InvalidOperationException(
					$"Null intermediate at '{SegmentPath(i)}' while getting '{Path}'.");

			current = Read(_chain[i], current);
		}
		return current;
	}

	public void Set(object root, object? value, bool createMissing = true)
	{
		ArgumentNullException.ThrowIfNull(root);
		SetValue(root, ConvertTo(value, ValueType), createMissing);
	}

	private void SetValue(object root, object? converted, bool createMissing)
	{
		if (_chain.Length == 1)
		{
			Write(_chain[0], root, converted, Path);
			return;
		}

		var boxes = new object?[_chain.Length];
		boxes[0] = root;

		for (int i = 0; i < _chain.Length - 1; i++)
		{
			object? next = Read(_chain[i], boxes[i]!);

			if (next is null)
			{
				if (!createMissing)
					throw new InvalidOperationException(
						$"Null intermediate at '{SegmentPath(i + 1)}' while setting '{Path}'.");

				next = CreateInstance(GetMemberType(_chain[i]), SegmentPath(i + 1));
				Write(_chain[i], boxes[i]!, next, SegmentPath(i + 1));
			}

			boxes[i + 1] = next;
		}

		Write(_chain[^1], boxes[^1]!, converted, Path);

		for (int i = _chain.Length - 2; i >= 0; i--)
		{
			if (GetMemberType(_chain[i]).IsValueType)
				Write(_chain[i], boxes[i]!, boxes[i + 1], SegmentPath(i + 1));
		}
	}

	public void SetParsed(object root, string? value, bool createMissing = true)
	{
		ArgumentNullException.ThrowIfNull(root);
		object? converted = ParseTo(value, ValueType);
		SetValue(root, converted, createMissing);
	}

	private static object? ParseTo(string? value, Type targetType)
	{
		if (string.IsNullOrWhiteSpace(value))
			return ConvertTo(null, targetType);

		Type destination = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (!AllowedLeafTypes.Contains(destination))
			throw new InvalidOperationException(
				$"Type '{destination.Name}' is not an allowed leaf type (allowed: string, int, float, bool, byte, Vector2).");

		if (destination == typeof(string)) return value;
		if (destination == typeof(bool))
		{
			if (bool.TryParse(value, out bool b)) return b;
			if (value == "1") return true;
			if (value == "0") return false;
			throw new FormatException($"Cannot parse '{value}' as bool (expected true/false or 1/0).");
		}
		if (destination == typeof(int))
			return int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
		if (destination == typeof(float))
			return float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
		if (destination == typeof(byte))
			return ParseByte(value);
		if (destination == typeof(System.Numerics.Vector2))
		{
			if (Core.Utils.TryParseVector2(value, out var vec))
				return vec;
			throw new FormatException($"Cannot parse '{value}' as vector2.");
		}

		return ConvertTo(value, targetType);
	}

	private static byte ParseByte(string value)
	{
		string s = value.Trim();
		if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
			s.StartsWith("&h", StringComparison.OrdinalIgnoreCase))
		{
			s = s[2..];
			return byte.Parse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
		}

		if (byte.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte dec))
			return dec;

		if (s.Length is 1 or 2 &&
			byte.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte hex))
			return hex;

		throw new FormatException($"Cannot parse '{value}' as byte (expected 0-255 or hex).");
	}

	private string SegmentPath(int memberCount) =>
		string.Join(".", _segmentNames, 0, memberCount);

	private static object CreateInstance(Type type, string path)
	{
		Type actual = Nullable.GetUnderlyingType(type) ?? type;

		if (actual.IsValueType)
			return Activator.CreateInstance(actual)
				?? throw new InvalidOperationException($"Failed to create '{path}' of type {actual.Name}.");

		ConstructorInfo ctor = actual.GetConstructor(Type.EmptyTypes)
			?? throw new InvalidOperationException(
				$"Cannot create missing '{path}' of type {actual.FullName} (no public parameterless constructor).");

		return ctor.Invoke(null);
	}

	private static object? Read(MemberInfo member, object target)
	{
		try
		{
			return member switch
			{
				FieldInfo field => field.GetValue(target),
				PropertyInfo property => property.GetValue(target),
				_ => throw new InvalidOperationException(
					$"Unsupported member kind '{member.MemberType}' for '{member.Name}'."),
			};
		}
		catch (TargetInvocationException ex)
		{
			throw new InvalidOperationException(
				$"Getter of '{member.Name}' threw an exception.", ex.InnerException ?? ex);
		}
	}

	private static void Write(MemberInfo member, object target, object? value, string path)
	{
		try
		{
			switch (member)
			{
				case FieldInfo field:
					field.SetValue(target, value);
					return;
				case PropertyInfo { CanWrite: false }:
					throw new InvalidOperationException(
						$"Property '{member.Name}' has no setter (path '{path}').");
				case PropertyInfo property:
					property.SetValue(target, value);
					return;
				default:
					throw new InvalidOperationException(
						$"Unsupported member kind '{member.MemberType}' for '{member.Name}'.");
			}
		}
		catch (TargetInvocationException ex)
		{
			throw new InvalidOperationException(
				$"Setter of '{member.Name}' threw an exception (path '{path}').", ex.InnerException ?? ex);
		}
	}

	internal static Type GetMemberType(MemberInfo member) =>
		member switch
		{
			FieldInfo field => field.FieldType,
			PropertyInfo property => property.PropertyType,
			_ => throw new ArgumentException($"Not a field or property: {member}", nameof(member)),
		};

	internal static object? ConvertTo(object? value, Type targetType)
	{
		if (value is null or DBNull)
		{
			if (!targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null)
				return null;
			throw new InvalidOperationException($"Cannot assign null to {targetType.Name}.");
		}

		Type destination = Nullable.GetUnderlyingType(targetType) ?? targetType;

		if (!AllowedLeafTypes.Contains(destination))
			throw new InvalidOperationException(
				$"Type '{destination.Name}' is not an allowed leaf type (allowed: string, int, float, bool, byte, Vector2).");

		Type source = value.GetType();
		if (destination.IsAssignableFrom(source))
			return value;

		if (value is string text)
		{
			if (destination == typeof(string)) return text;
			if (destination == typeof(bool))
			{
				if (bool.TryParse(text, out bool b)) return b;
				if (text == "1") return true;
				if (text == "0") return false;
				throw new FormatException($"Cannot parse '{text}' as bool (expected true/false or 1/0).");
			}
			if (destination == typeof(int))
				return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
			if (destination == typeof(float))
				return float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
			if (destination == typeof(byte))
				return ParseByte(text);
			if (destination == typeof(System.Numerics.Vector2))
			{
				if (Core.Utils.TryParseVector2(text, out var vec))
					return vec;
				throw new FormatException($"Cannot parse '{text}' as vector2.");
			}

			throw new InvalidOperationException(
				$"Cannot convert string to allowed leaf type '{destination.Name}'.");
		}

		return Convert.ChangeType(value, destination, CultureInfo.InvariantCulture);
	}
}

public static class PathMemberCache
{
	public static IReadOnlyDictionary<string, PathMember> Build<T>(
		bool includeFields = true,
		bool includeProperties = true,
		bool includeNonPublic = true,
		int maxDepth = 5) =>
		Build(typeof(T), includeFields, includeProperties, includeNonPublic, maxDepth);

	public static IReadOnlyDictionary<string, PathMember> Build(
		Type rootType,
		bool includeFields = true,
		bool includeProperties = true,
		bool includeNonPublic = false,
		int maxDepth = 5)
	{
		ArgumentNullException.ThrowIfNull(rootType);
		if (maxDepth < 1) throw new ArgumentOutOfRangeException(nameof(maxDepth));

		var result = new Dictionary<string, PathMember>(StringComparer.Ordinal);
		var stack = new List<MemberInfo>();
		var visiting = new HashSet<Type>();

		Walk(rootType, prefix: "", stack, visiting, result,
			includeFields, includeProperties, includeNonPublic, maxDepth);

		return new ReadOnlyDictionary<string, PathMember>(result);
	}

	public static void Apply(
		object instance,
		IReadOnlyDictionary<string, PathMember> cache,
		IEnumerable<KeyValuePair<string, object?>> values,
		bool createMissing = true)
	{
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(cache);
		ArgumentNullException.ThrowIfNull(values);

		foreach (var (path, value) in values)
		{
			if (!cache.TryGetValue(path, out PathMember? member))
				throw new KeyNotFoundException($"No cached member for path '{path}'.");

			member.Set(instance, value, createMissing);
		}
	}

	private static void Walk(
		Type type,
		string prefix,
		List<MemberInfo> stack,
		HashSet<Type> visiting,
		Dictionary<string, PathMember> result,
		bool includeFields,
		bool includeProperties,
		bool includeNonPublic,
		int depthLeft)
	{
		if (depthLeft <= 0 || !visiting.Add(type))
			return;

		try
		{
			BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
			if (includeNonPublic) flags |= BindingFlags.NonPublic;

			if (includeProperties)
			{
				foreach (PropertyInfo property in type.GetProperties(flags))
				{
					if (property.GetIndexParameters().Length != 0) continue;
					if (property.DeclaringType == typeof(object)) continue;

					AddMember(property, property.PropertyType, property.CanRead,
						prefix, stack, visiting, result,
						includeFields, includeProperties, includeNonPublic, depthLeft);
				}
			}

			if (includeFields)
			{
				foreach (FieldInfo field in type.GetFields(flags))
				{
					if (field.IsStatic) continue;
					if (field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)) continue;

					AddMember(field, field.FieldType, canRead: true,
						prefix, stack, visiting, result,
						includeFields, includeProperties, includeNonPublic, depthLeft);
				}
			}
		}
		finally
		{
			visiting.Remove(type);
		}
	}

	private static void AddMember(
		MemberInfo member,
		Type memberType,
		bool canRead,
		string prefix,
		List<MemberInfo> stack,
		HashSet<Type> visiting,
		Dictionary<string, PathMember> result,
		bool includeFields,
		bool includeProperties,
		bool includeNonPublic,
		int depthLeft)
	{
		Type next = Nullable.GetUnderlyingType(memberType) ?? memberType;
		string path = prefix.Length == 0 ? member.Name : prefix + "." + member.Name;

		if (IsForbiddenType(next))
			return;

		if (PathMember.IsAllowedLeafType(next))
		{
			stack.Add(member);
			try
			{
				result[path] = new PathMember(path, stack);
			}
			finally
			{
				stack.RemoveAt(stack.Count - 1);
			}
			return;
		}

		if (canRead && depthLeft > 1)
		{
			stack.Add(member);
			try
			{
				Walk(next, path, stack, visiting, result,
					includeFields, includeProperties, includeNonPublic, depthLeft - 1);
			}
			finally
			{
				stack.RemoveAt(stack.Count - 1);
			}
		}
	}

	private static bool IsForbiddenType(Type type)
	{
		if (type == typeof(string)) return false;
		if (typeof(Delegate).IsAssignableFrom(type)) return true;
		if (typeof(System.Collections.IEnumerable).IsAssignableFrom(type)) return true;
		if (type.IsArray) return true;
		if (type.IsByRefLike) return true;
		return false;
	}
}