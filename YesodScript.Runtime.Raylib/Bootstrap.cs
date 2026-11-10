namespace YesodScript.Runtime;

public class Bootstrap
{
    public void Run(string[] args)
    {
        RaylibHost? host = null;
        try
        {
            GameContent.Initialize(args);
            var vm = GameVM.Create();
            host = new RaylibHost(vm);
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