using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace YesodScript.Examples;

public static class ErrorReport
{
	public const string LogFileName = "error.log";

	public static string LogPath => Path.Combine(AppContext.BaseDirectory, LogFileName);

	public static string Write(Exception ex, string stage)
	{
		var text = Format(ex, stage);
		try
		{
			File.WriteAllText(LogPath, text, Encoding.UTF8);
		}
		catch
		{
            
		}

		return text;
	}

	public static string Format(Exception ex, string stage)
	{
		var sb = new StringBuilder();
		sb.AppendLine("YesodScript error");
		sb.AppendLine($"Time:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine($"Stage: {stage}");
		sb.AppendLine();

		var summary = GetSummary(ex);
		if (!string.IsNullOrEmpty(summary.Script))
			sb.AppendLine($"Script: {summary.Script}");
		if (summary.Line is int line)
			sb.AppendLine($"Line:   {line}");
		sb.AppendLine($"Message: {summary.Message}");
		sb.AppendLine();
		sb.AppendLine("Full exception:");
		sb.AppendLine(ex.ToString());
		sb.AppendLine();
		sb.AppendLine($"Log file: {LogPath}");
		return sb.ToString();
	}

	public static string FormatOnScreen(Exception ex, string stage)
	{
		var summary = GetSummary(ex);
		var sb = new StringBuilder();
		sb.AppendLine("SCRIPT / DATA ERROR");
		sb.AppendLine();
		sb.AppendLine($"Stage: {stage}");
		if (!string.IsNullOrEmpty(summary.Script))
			sb.AppendLine($"Script: {summary.Script}");
		if (summary.Line is int line)
			sb.AppendLine($"Line: {line}");
		sb.AppendLine();
		sb.AppendLine(Wrap(summary.Message, 56));
		sb.AppendLine();
		sb.AppendLine($"Details written to {LogFileName}");
		sb.AppendLine("next to the executable.");
		sb.AppendLine();
		sb.AppendLine("Close the window to exit.");
		return sb.ToString();
	}

	public static string FormatMessageBox(Exception ex, string stage)
	{
		var summary = GetSummary(ex);
		var sb = new StringBuilder();
		sb.AppendLine($"Stage: {stage}");
		if (!string.IsNullOrEmpty(summary.Script))
			sb.AppendLine($"Script: {summary.Script}");
		if (summary.Line is int line)
			sb.AppendLine($"Line: {line}");
		sb.AppendLine();
		sb.AppendLine(summary.Message);
		sb.AppendLine();
		sb.AppendLine($"Full details were written to:");
		sb.AppendLine(LogPath);
		return sb.ToString();
	}

	public static void ShowMessageBox(string message, string title = "YesodScript — Script Error")
	{
		try
		{
			MessageBoxW(IntPtr.Zero, message, title, 0x00000010u ); //MB_ICONERROR
		}
		catch
		{
            
		}
	}

	public static ErrorSummary GetSummary(Exception ex)
	{
		var message = ex.Message ?? ex.GetType().Name;
		string? script = null;
		int? line = null;

		//Engine format: "Compiler error at 'main' line 12: ..."
		//               "Runtime error at 'ball' line 40: ..."
		var match = Regex.Match(
			message,
			@"^(?:Compiler|Runtime) error at '(?<script>[^']+)' line (?<line>\d+):\s*(?<msg>.*)$",
			RegexOptions.IgnoreCase | RegexOptions.Singleline);

		if (match.Success)
		{
			script = match.Groups["script"].Value;
			if (int.TryParse(match.Groups["line"].Value, out var parsedLine))
				line = parsedLine;
			message = match.Groups["msg"].Value.Trim();
		}

		return new ErrorSummary(script, line, message);
	}

	private static string Wrap(string text, int width)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= width)
			return text;

		var sb = new StringBuilder();
		var remaining = text;
		while (remaining.Length > width)
		{
			var split = remaining.LastIndexOf(' ', width);
			if (split <= 0) split = width;
			sb.AppendLine(remaining[..split].TrimEnd());
			remaining = remaining[split..].TrimStart();
		}

		sb.Append(remaining);
		return sb.ToString();
	}

	[DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
	private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}

public readonly record struct ErrorSummary(string? Script, int? Line, string Message);
