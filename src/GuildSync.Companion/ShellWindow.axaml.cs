using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GuildSync.Companion.Services;
using GuildSync.Companion.ViewModels;

namespace GuildSync.Companion;

public partial class ShellWindow : Window, IUserDialogs
{
    private readonly ShellViewModel _model;
    private bool _allowClose;
    private bool _toldTray;

    public ShellWindow() : this(new ShellViewModel("home"))
    {
    }

    public ShellWindow(ShellViewModel model)
    {
        _model = model;
        DataContext = model;
        InitializeComponent();
    }

    public void BringUp()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowWindow() => BringUp();

    public async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select your WoW install",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
            return null;
        return folders[0].TryGetLocalPath();
    }

    public void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public void ShowToast(string message)
    {
        if (IsVisible)
        {
            _model.Banner(message);
            return;
        }

        var toast = new ToastWindow(message);
        toast.Show();
    }

    public void Shutdown()
    {
        _allowClose = true;
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
    }

    private void DragWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void Minimize(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void HideToTray(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => HideToTray();

    private void HideToTray()
    {
        Hide();
        if (_toldTray)
            return;
        _toldTray = true;
        Dispatcher.UIThread.Post(() =>
            ShowToast("Still running in the tray. Quit from the icon when you want it to stop."),
            DispatcherPriority.Background);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }
        e.Cancel = true;
        HideToTray();
    }
}
