using Avalonia;
using GuildSync.Companion.Platform;
using GuildSync.Core;

namespace GuildSync.Companion;

public static class Program
{
    public static string[] Args { get; private set; } = [];
    public static Mutex? Instance { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Args = args;
        // A crash should at least leave a trace journald can pick up.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");
        AppConstants.ServerBase = AppConstants.ResolveServerBase();

        if (args.Contains("--version") || args.Contains("-v"))
        {
            Console.WriteLine($"{AppConstants.AppName} {AppConstants.Version}");
            return;
        }

        var preview = PreviewPage();
        if (preview is null)
        {
            var name = Environment.GetEnvironmentVariable("GSC_IPC_NAME");
            if (string.IsNullOrWhiteSpace(name))
                name = AppConstants.IpcName;
            Instance = SingleInstance.Claim(name, App.RequestShow);
            if (Instance is null)
                return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    public static string? PreviewPage()
    {
        var index = Array.IndexOf(Args, "--preview");
        if (index < 0 || index + 1 >= Args.Length)
            return null;
        var page = Args[index + 1];
        return page == "dashboard" ? "home" : page;
    }

    public static bool ForceShow => Args.Contains("--show") || PreviewPage() is not null;
}
