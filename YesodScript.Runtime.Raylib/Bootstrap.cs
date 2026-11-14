using Raylib_cs;

namespace YesodScript.Runtime;

public class Bootstrap
{
    public static RaylibHost CreateBrowserHost(string[]? args = null, Action<Assets>? configureAssets = null)
    {
        return CreateBrowserHost(args, configureAssets, null, null);
    }

    public static RaylibHost CreateBrowserHostWithStorage(ISaveStorage saveStorage, string[]? args = null, Action<Assets>? configureAssets = null)
    {
        return CreateBrowserHost(args, configureAssets, saveStorage, null);
    }

    public static RaylibHost CreateBrowserHostWithVMFactory(
        Func<ISaveStorage?, GameVM> vmFactory,
        string[]? args = null,
        Action<Assets>? configureAssets = null)
    {
        ArgumentNullException.ThrowIfNull(vmFactory);
        return CreateBrowserHost(args, configureAssets, null, vmFactory);
    }

    public static RaylibHost CreateBrowserHostWithStorageAndVMFactory(
        ISaveStorage saveStorage,
        Func<ISaveStorage?, GameVM> vmFactory,
        string[]? args = null,
        Action<Assets>? configureAssets = null)
    {
        ArgumentNullException.ThrowIfNull(vmFactory);
        return CreateBrowserHost(args, configureAssets, saveStorage, vmFactory);
    }

    private static RaylibHost CreateBrowserHost(
        string[]? args,
        Action<Assets>? configureAssets,
        ISaveStorage? saveStorage,
        Func<ISaveStorage?, GameVM>? vmFactory)
    {
        GameContent.Initialize(args);
        var vm = vmFactory == null
            ? GameVM.Create(saveStorage)
            : GameVM.CreateWithFactory(vmFactory, saveStorage);
        var host = new RaylibHost(vm, configureAssets);
        Raylib.SetTargetFPS(30);
        return host;
    }

    public void Run(string[] args)
    {
        Run(args, null);
    }

    public void Run(string[] args, Action<Assets>? configureAssets)
    {
        RunCore(args, configureAssets, null);
    }

    public void RunWithVMFactory(string[] args, Func<ISaveStorage?, GameVM> vmFactory, Action<Assets>? configureAssets = null)
    {
        ArgumentNullException.ThrowIfNull(vmFactory);
        RunCore(args, configureAssets, vmFactory);
    }

    private static void RunCore(string[] args, Action<Assets>? configureAssets, Func<ISaveStorage?, GameVM>? vmFactory)
    {
        RaylibHost? host = null;
        try
        {
            GameContent.Initialize(args);
            var vm = vmFactory == null ? GameVM.Create() : GameVM.CreateWithFactory(vmFactory);
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