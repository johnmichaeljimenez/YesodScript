using YesodScript.Engine.Core;

namespace YesodScript.Examples;

public class SaveManager
{
    private readonly Dictionary<string, List<Var>> saveData = new();

    public SaveManager(GameVM gameVM)
    {
        
    }

    public void RegisterVar(string group, Var variable)
    {
        if (variable.Name.StartsWith("_", StringComparison.InvariantCultureIgnoreCase) || variable.Name.StartsWith("SYS:", StringComparison.InvariantCultureIgnoreCase))
        {
            throw new Exception($"Cannot register save variable '{variable.Name}' (local or system)");
        }

        if (!saveData.ContainsKey(group))
        {
            saveData[group] = new();
        }

        saveData[group].Add(variable);
    }

    public void Load(string group)
    {
        if (!saveData.ContainsKey(group))
        {
            throw new Exception($"No save data registered for group '{group}'");
        }

        var path = GetSavePath(group);
        var json = System.IO.File.ReadAllText(path);
        var data = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json);

        foreach (var i in saveData[group])
        {
            i.Value = data[i.Name];
        }
    }

    public void Save(string group)
    {
        if (!saveData.ContainsKey(group))
        {
            throw new Exception($"No save data registered for group '{group}'");
        }

        var path = GetSavePath(group);
        var data = new Dictionary<string, object>();

        foreach (var i in saveData[group])
        {
            data[i.Name] = i.Value;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(data);
        System.IO.File.WriteAllText(path, json);
    }

    public string GetSavePath(string group)
    {
        return $"saves/{group}.json";
    }
}