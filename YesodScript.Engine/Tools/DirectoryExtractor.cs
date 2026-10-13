namespace YesodScript.Engine.Tools;

public static class DirectoryExtractor
{
	public static Dictionary<string, string> GetPaths(string basePath, string ext = "ysl")
	{
		var fileMap = new Dictionary<string, string>();
		var searchPattern = $"*.{ext}";

		foreach (var fullPath in Directory.GetFiles(basePath, searchPattern, SearchOption.AllDirectories))
		{
			var relativePath = Path.GetRelativePath(basePath, fullPath).Replace('\\', '/');

			var dir = Path.GetDirectoryName(relativePath);
			var fileNameWithoutExt = Path.GetFileNameWithoutExtension(relativePath);

			var key = string.IsNullOrEmpty(dir) || dir == "."
				? fileNameWithoutExt
				: $"{dir.Replace('\\', '/')}/{fileNameWithoutExt}";

			fileMap[key] = Path.Combine(basePath, relativePath).Replace('\\', '/');
		}

		return fileMap;
	}
}