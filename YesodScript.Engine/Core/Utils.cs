using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace YesodScript.Engine.Core;

public readonly struct ArgumentToken
{
    public string Value { get; }
    public bool Quoted { get; }

    public ArgumentToken(string value, bool quoted)
    {
        Value = value;
        Quoted = quoted;
    }

    public bool IsDefault =>
        !Quoted && Value == "*";

    public override string ToString() => Value;
}

public static class Utils
{
    public static bool IsMatch(string candidate, string query)
    {
        if (candidate == null || query == null)
            return false;

        if (query.Contains("*"))
            return IsMatchWildcard(candidate, query);

        if (candidate.Contains("*"))
            return IsMatchWildcard(query, candidate);

        return string.Equals(candidate, query, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasPrefix(string prefix, ref string input)
    {
        if (string.IsNullOrEmpty(prefix) || string.IsNullOrEmpty(input) || !input.StartsWith(prefix, StringComparison.InvariantCultureIgnoreCase))
            return false;

        input = input.Substring(prefix.Length);
        return true;
    }

    public static bool TryParseBool(string input, out bool result)
    {
        if (bool.TryParse(input, out result))
        {
            return true;
        }

        if (input == "1")
        {
            result = true;
            return true;
        }
        if (input == "0")
        {
            result = false;
            return true;
        }

        result = false;
        return false;
    }

    public static bool TryParseVector2(string input, out Vector2 result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var s = input.Trim();
        if (s.Length >= 2 && s[0] == '<' && s[^1] == '>')
            s = s[1..^1].Trim();
        else if (s.Length >= 2 && s[0] == '(' && s[^1] == ')')
            s = s[1..^1].Trim();

        var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return false;

        if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
            return false;
        if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            return false;

        result = new Vector2(x, y);
        return true;
    }

    public static string FormatVector2(Vector2 value) =>
        string.Format(CultureInfo.InvariantCulture, "{0},{1}", value.X, value.Y);

    public static string FormatValue(object? value) => value switch
    {
        null => string.Empty,
        Vector2 v => FormatVector2(v),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    public static List<string> SplitArguments(this string input)
    {
        return SplitArgumentTokens(input).Select(t => t.Value).ToList();
    }

    public static List<ArgumentToken> SplitArgumentTokens(this string input)
    {
        var result = new List<ArgumentToken>();
        var currentToken = new StringBuilder();
        bool inQuotes = false;
        bool tokenQuoted = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (c == '"')   //YAGNI: no double quotes needed yet
            {
                inQuotes = !inQuotes;
                tokenQuoted = true;
                continue;
            }

            if (c == ' ' && !inQuotes)
            {
                if (currentToken.Length > 0)
                {
                    result.Add(new ArgumentToken(currentToken.ToString(), tokenQuoted));
                    currentToken.Clear();
                    tokenQuoted = false;
                }
            }
            else
            {
                currentToken.Append(c);
            }
        }

        if (currentToken.Length > 0)
        {
            result.Add(new ArgumentToken(currentToken.ToString(), tokenQuoted));
        }

        return result;
    }

    public static bool IsMatchWildcard(string input, string wildcardPattern)
    {
        string regexPattern = Regex.Escape(wildcardPattern)
                                   .Replace("\\*", ".*")
                                   .Replace("\\?", ".");

        regexPattern = "^" + regexPattern + "$";

        return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
    }
}
