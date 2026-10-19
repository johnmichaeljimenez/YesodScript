namespace YesodScript.Engine.Tools;

public static class DirectoryExtractor
{
	public static Dictionary<string, string> GetPaths(string basePath, params string[] exts)
	{
		if (exts == null || exts.Length == 0)
			exts = ["ysl"];

		var fileMap = new Dictionary<string, string>();

		foreach (var ext in exts)
		{
			if (string.IsNullOrWhiteSpace(ext))
				continue;

			var normalizedExt = ext.Trim().TrimStart('.');
			var searchPattern = $"*.{normalizedExt}";

			foreach (var fullPath in Directory.GetFiles(basePath, searchPattern, SearchOption.AllDirectories))
			{
				var relativePath = Path.GetRelativePath(basePath, fullPath).Replace('\\', '/');

				var dir = Path.GetDirectoryName(relativePath);
				var fileNameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);

				var key = string.IsNullOrEmpty(dir) || dir == "."
					? fileNameWithoutExt
					: $"{dir.Replace('\\', '/')}/{fileNameWithoutExt}";

				// First extension / first file wins on key collision
				if (fileMap.ContainsKey(key))
					continue;

				fileMap[key] = Path.Combine(basePath, relativePath).Replace('\\', '/');
			}
		}

		return fileMap;
	}
}