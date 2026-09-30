using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using GuildSync.Companion.Platform;
using GuildSync.Companion.ViewModels;
using GuildSync.Core;

namespace GuildSync.Companion;

public class App : Application
{
    private static Action? _show;
    public static ShellViewModel? Model { get; private set; }
    public static ShellWindow? Shell { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var preview = Program.PreviewPage();
            ShellViewModel model;
            if (preview is not null)
            {
                model = new ShellViewModel(preview);
            }
            else
            {
                var store = new ConfigStore(ConfigStore.DefaultFile());
                var host = new CompanionHost(store, new IngestClient(), new AddonSource(), new Win32GameProcess());
                var show = Program.ForceShow || !store.Data.Configured;
                model = new ShellViewModel(host, show);
                model.StartHost();
            }

            Model = model;
            var window = new ShellWindow(model);
            model.Dialogs = window;
            Shell = window;
            _show = () => Dispatcher.UIThread.Post(window.BringUp);
            CreateTray(model);
            if (model.ShowOnLaunch)
                window.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void CreateTray(ShellViewModel model)
    {
        WindowIcon icon;
        using (var stream = AssetLoader.Open(new Uri("avares://GuildSyncCompanion/Assets/crest.png")))
            icon = new WindowIcon(stream);

        var menu = new NativeMenu();
        menu.Items.Add(new NativeMenuItem("Settings...") { Command = model.ShowSettingsCommand });
        menu.Items.Add(new NativeMenuItem("Sync Now") { Command = model.SyncNowCommand });
        menu.Items.Add(new NativeMenuItem("Check for Updates") { Command = model.CheckUpdatesCommand });
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(new NativeMenuItem("About...") { Command = model.ShowAboutCommand });
        menu.Items.Add(new NativeMenuItem("Quit") { Command = model.QuitCommand });

        var tray = new TrayIcon
        {
            Icon = icon,
            ToolTipText = "GuildSync Companion",
            Command = model.ShowCommand,
            Menu = menu,
        };
        TrayIcon.SetIcons(Current!, new TrayIcons { tray });
    }

    public static void RequestShow()
    {
        var show = _show;
        if (show is null)
            return;
        try
        {
            show();
        }
        catch (Exception)
        {
        }
    }
}
