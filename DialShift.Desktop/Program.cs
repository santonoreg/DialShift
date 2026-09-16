using Avalonia;

namespace DialShift.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
        catch (Exception ex) { App.Log(ex); throw; }
    }
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
