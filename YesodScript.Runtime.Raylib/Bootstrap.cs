using Raylib_cs;

namespace YesodScript.Runtime;

public class Bootstrap
{
    public static RaylibHost CreateBrowserHost(string[]? args = null, Action<Assets>? configureAssets = null)
    {
        return CreateBrowserHost(args, configureAssets, null);
    }

    public static RaylibHost CreateBrowserHostWithStorage(ISaveStorage saveStorage, string[]? args = null, Action<Assets>? configureAssets = null)
    {
        return CreateBrowserHost(args, configureAssets, saveStorage);
    }

    private static RaylibHost CreateBrowserHost(string[]? args, Action<Assets>? configureAssets, ISaveStorage? saveStorage)
    {
        GameContent.Initialize(args);
        var host = new RaylibHost(GameVM.Create(saveStorage), configureAssets);
        Raylib.SetTargetFPS(30);
        return host;
    }

    public void Run(string[] args)
    {
        Run(args, null);
    }

    public void Run(string[] args, Action<Assets>? configureAssets)
    {
        RaylibHost? host = null;
        try
        {
            GameContent.Initialize(args);
            var vm = GameVM.Create();
            host = new RaylibHost(vm, configureAssets);
            host.Tick();
        }
        catch (Exception ex)
        {
            ErrorReport.Write(ex, "Startup");
            ErrorReport.ShowMessageBox(ErrorReport.FormatMessageBox(ex, "Startup"));
        }
        finally
        {
            try
            {
                host?.Cleanup();
            }
            catch
            {
            }
        }
    }
}