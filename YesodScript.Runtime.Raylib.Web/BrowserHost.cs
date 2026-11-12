using System.Runtime.InteropServices.JavaScript;
using YesodScript.Runtime;

namespace YesodScript.Runtime.Raylib.Web;

public static partial class BrowserHost
{
    private static RaylibHost? _host;

    public static void Start()
    {
        if (_host != null)
            throw new InvalidOperationException("The browser host has already been started.");

        _host = Bootstrap.CreateBrowserHostWithStorage(new BrowserSaveStorage());
    }

    [JSExport]
    public static bool UpdateFrame()
    {
        return _host?.UpdateFrame() ?? false;
    }

    [JSExport]
    public static void ResizeWindow(int width, int height)
    {
        Raylib_cs.Raylib.SetWindowSize(width, height);
    }

    [JSExport]
    public static string? GetFatalErrorText() => _host?.FatalErrorText;

    [JSExport]
    public static void Cleanup()
    {
        _host?.Cleanup();
        _host = null;
    }

    [JSImport("globalThis.localStorage.getItem")]
    private static partial string? GetLocalStorageItem(string key);

    [JSImport("globalThis.localStorage.setItem")]
    private static partial void SetLocalStorageItem(string key, string value);

    private sealed class BrowserSaveStorage : ISaveStorage
    {
        private const string KeyPrefix = "yesodscript.save.v1";

        public string? Read(string path) =>
            GetLocalStorageItem(GetStorageKey(path));

        public void Write(string path, string contents) =>
            SetLocalStorageItem(GetStorageKey(path), contents);

        private static string GetStorageKey(string path) =>
            $"{KeyPrefix}:{Uri.EscapeDataString(GameContent.GameId)}:{Uri.EscapeDataString(path)}";
    }
}
