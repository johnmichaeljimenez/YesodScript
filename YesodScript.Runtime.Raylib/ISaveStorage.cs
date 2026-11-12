namespace YesodScript.Runtime;

public interface ISaveStorage
{
	string? Read(string path);
	void Write(string path, string contents);
}

public class FileSaveStorage : ISaveStorage
{
	public string? Read(string path) =>
		File.Exists(path) ? File.ReadAllText(path) : null;

	public void Write(string path, string contents) =>
		File.WriteAllText(path, contents);
}
