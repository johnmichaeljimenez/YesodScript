namespace YesodScript.Runtime;

public class Bootstrap
{
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