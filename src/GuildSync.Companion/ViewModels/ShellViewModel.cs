using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuildSync.Companion.Platform;
using GuildSync.Companion.Services;
using GuildSync.Core;

namespace GuildSync.Companion.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly CompanionHost? _host;
    private readonly bool _preview;
    private int _toastGen;

    public ShellViewModel(CompanionHost host, bool showOnLaunch)
    {
        _host = host;
        ShowOnLaunch = showOnLaunch;
        CompanionVersion = "v" + AppConstants.Version;
        Token = host.Config.Token;
        GameDir = host.Config.GameDir;
        AutoUpdate = host.Config.AutoUpdateAddon;
        NotifyMode = host.Config.Notify;
        StartWithWindows = host.Config.StartWithWindows;
        Page = host.Config.Configured ? "home" : "token";
        host.Changed += () => Dispatcher.UIThread.Post(Pull);
        host.Toast += (_, message, _) => Dispatcher.UIThread.Post(() => Dialogs?.ShowToast(message));
        Pull();
    }

    public ShellViewModel(string preview)
    {
        _preview = true;
        ShowOnLaunch = true;
        CompanionVersion = "v" + AppConstants.Version;
        Page = preview;
        Token = "";
        GameDir = @"C:\Program Files\World of Warcraft\_classic_beta_";
        AutoUpdate = true;
        NotifyMode = "failures";
        StartWithWindows = true;
        if (preview is "home" or "settings" or "about")
            LoadHomePreview();
        if (preview == "install")
        {
            FoundInstalls.Add(@"C:\Program Files\World of Warcraft\_classic_beta_");
            FoundInstalls.Add(@"D:\Games\World of Warcraft Forever\_classic_beta_");
            SelectedInstall = FoundInstalls[0];
            ScanMessage = "Searching complete. Found:";
            Scanning = false;
        }
        if (preview == "setup")
        {
            SetupMessage = "Completed! The addon is installed (0.3.5).\n\nThe app is now running in the background. You may now play WoW: Forever with your data synced.";
            SetupDone = true;
            Scanning = false;
        }
        PullPreviewPresence();
    }

    public IUserDialogs? Dialogs { get; set; }
    public bool ShowOnLaunch { get; }
    public string CompanionVersion { get; }

    public ObservableCollection<string> Activity { get; } = [];
    public ObservableCollection<string> FoundInstalls { get; } = [];

    [ObservableProperty] private string page = "token";
    [ObservableProperty] private string token = "";
    [ObservableProperty] private bool tokenHidden = true;
    [ObservableProperty] private string tokenMessage = "";
    [ObservableProperty] private bool busy;
    [ObservableProperty] private string scanMessage = "Searching for your World of Warcraft install.";
    [ObservableProperty] private bool scanning = true;
    [ObservableProperty] private string? selectedInstall;
    [ObservableProperty] private string installError = "";
    [ObservableProperty] private string setupMessage = "Installing the GuildSync addon...";
    [ObservableProperty] private bool setupDone;
    [ObservableProperty] private string headline = "Idle until you log in";
    [ObservableProperty] private string detail = "World of Warcraft is closed. GuildSync uploads while you play, and once more when you log out.";
    [ObservableProperty] private string gameValue = "Closed";
    [ObservableProperty] private string syncValue = "Waiting";
    [ObservableProperty] private string addonValue = "Not installed";
    [ObservableProperty] private string presence = "idle";
    [ObservableProperty] private string gameDir = "";
    [ObservableProperty] private bool autoUpdate = true;
    [ObservableProperty] private string notifyMode = "failures";
    [ObservableProperty] private bool startWithWindows = true;
    [ObservableProperty] private string settingsMessage = "";
    [ObservableProperty] private bool toastOpen;
    [ObservableProperty] private string toastText = "";

    public bool ShowNav => Page is "home" or "settings" or "about";
    public bool NotifyFailures => NotifyMode == "failures";
    public bool NotifyAlways => NotifyMode == "always";
    public bool NotifyNever => NotifyMode == "never";
    public bool PresenceLive => Presence == "live";
    public bool PresenceIdle => Presence == "idle";
    public bool PresenceBad => Presence == "bad";
    public char TokenMask => TokenHidden ? '●' : '\0';
    public string TokenToggleLabel => TokenHidden ? "Show" : "Hide";
    public bool CanUseWindowsStartup => OperatingSystem.IsWindows();

    public string Disclaimer { get; } =
        "Not affiliated with Blizzard Entertainment. GuildSync does not modify the World of Warcraft client, read its memory, inject code, or play the game. A free addon writes your saved data; this app uploads that file to your guild site.";

    partial void OnPageChanged(string value) => OnPropertyChanged(nameof(ShowNav));
    partial void OnNotifyModeChanged(string value)
    {
        OnPropertyChanged(nameof(NotifyFailures));
        OnPropertyChanged(nameof(NotifyAlways));
        OnPropertyChanged(nameof(NotifyNever));
    }
    partial void OnPresenceChanged(string value)
    {
        OnPropertyChanged(nameof(PresenceLive));
        OnPropertyChanged(nameof(PresenceIdle));
        OnPropertyChanged(nameof(PresenceBad));
    }
    partial void OnTokenHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(TokenMask));
        OnPropertyChanged(nameof(TokenToggleLabel));
    }
    partial void OnSelectedInstallChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            GameDir = value;
    }
    partial void OnBusyChanged(bool value)
    {
        NextTokenCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    public void StartHost()
    {
        if (_preview || _host is null)
            return;
        WindowsStartup.Apply(_host.Config.StartWithWindows);
        _host.Start();
        Pull();
    }

    [RelayCommand]
    private void ToggleToken() => TokenHidden = !TokenHidden;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task NextTokenAsync()
    {
        var problem = TokenRules.Problem(Token);
        if (problem is not null)
        {
            TokenMessage = problem;
            return;
        }
        if (_preview || _host is null)
        {
            Page = "install";
            return;
        }

        Busy = true;
        TokenMessage = "Checking token...";
        var check = await CheckTypedToken().ConfigureAwait(true);
        Busy = false;
        if (check.Status != "ok")
        {
            TokenMessage = $"That token was rejected. Get a fresh one at {AppConstants.UploadPageUrl}.";
            return;
        }
        _host.Config.Token = Token.Trim();
        if (!Persist())
            return;
        TokenMessage = "";
        Page = "install";
        await ScanAsync().ConfigureAwait(true);
    }

    private bool CanSubmit() => !Busy;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (_preview)
            return;
        Scanning = true;
        ScanMessage = "Searching for your World of Warcraft install.";
        InstallError = "";
        var roots = WindowsInstallRoots.Collect();
        var found = await Task.Run(() =>
        {
            var list = InstallFinder.Probe(roots.Probe);
            var seen = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            foreach (var walked in InstallFinder.FindInstalls(roots.Walk, maxDepth: 5))
            {
                if (seen.Add(walked))
                    list.Add(walked);
            }
            return list;
        }).ConfigureAwait(true);

        FoundInstalls.Clear();
        foreach (var path in found)
            FoundInstalls.Add(path);
        Scanning = false;
        if (found.Count > 0)
        {
            ScanMessage = "Searching complete. Found:";
            SelectedInstall = found[0];
            GameDir = found[0];
        }
        else
        {
            ScanMessage = "No install found automatically. Browse to the folder that contains Wow.exe.";
        }
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        if (Dialogs is null)
            return;
        var chosen = await Dialogs.PickFolderAsync().ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(chosen))
            GameDir = chosen;
    }

    [RelayCommand]
    private async Task UseFolderAsync()
    {
        var client = InstallFinder.Normalize(GameDir);
        if (client is null)
        {
            InstallError = "That folder does not look like a WoW: Forever client. It should contain Wow.exe and the Interface and WTF folders.";
            return;
        }
        InstallError = "";
        GameDir = client;
        if (_preview || _host is null)
        {
            Page = "setup";
            SetupDone = true;
            return;
        }
        _host.Config.GameDir = client;
        if (!Persist())
            return;
        Page = "setup";
        SetupDone = false;
        SetupMessage = "Installing the GuildSync addon...";
        var update = await _host.CheckAddonAsync(manual: false).ConfigureAwait(true);
        SetupDone = true;
        if (update.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            SetupMessage = "Completed, but the addon could not be installed automatically. You can retry from Check for Addon Updates.";
        }
        else
        {
            var version = string.IsNullOrWhiteSpace(_host.Config.AddonVersion) ? "latest" : _host.Config.AddonVersion;
            SetupMessage = $"Completed! The addon is installed ({version}).\n\nThe app is now running in the background. You may now play WoW: Forever with your data synced.";
        }
        _host.RefreshWatcher();
        Pull();
    }

    [RelayCommand]
    private void Finish()
    {
        Page = "home";
        if (_host is not null)
            WindowsStartup.Apply(_host.Config.StartWithWindows);
        Pull();
    }

    [RelayCommand]
    private void ShowHome() => Page = _host?.Config.Configured == false && !_preview ? "token" : "home";

    [RelayCommand]
    private void ShowSettings()
    {
        if (_host is not null && !_host.Config.Configured && !_preview)
        {
            Page = "token";
        }
        else
        {
            Page = "settings";
        }
        Dialogs?.ShowWindow();
    }

    [RelayCommand]
    private void ShowAbout()
    {
        Page = "about";
        Dialogs?.ShowWindow();
    }

    [RelayCommand]
    private void Show()
    {
        if (Page is "token" or "install" or "setup")
        {
            Dialogs?.ShowWindow();
            return;
        }
        Page = "home";
        Dialogs?.ShowWindow();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (_preview || _host is null)
        {
            NoteLocal("Preview only. Nothing was uploaded.");
            return;
        }
        await _host.SyncAsync(force: true, toast: true).ConfigureAwait(true);
        Pull();
    }

    [RelayCommand]
    private async Task CheckAddonAsync()
    {
        if (_preview || _host is null)
        {
            NoteLocal("Preview only. The addon was not checked.");
            return;
        }
        await _host.CheckAddonAsync(manual: true).ConfigureAwait(true);
        Pull();
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SaveAsync()
    {
        if (_preview || _host is null)
        {
            SettingsMessage = "Saved.";
            return;
        }
        var problem = TokenRules.Problem(Token);
        if (problem is not null)
        {
            SettingsMessage = problem;
            return;
        }
        string? client = "";
        if (!string.IsNullOrWhiteSpace(GameDir))
        {
            client = InstallFinder.Normalize(GameDir);
            if (client is null)
            {
                SettingsMessage = "Install folder is not a valid WoW: Forever client.";
                return;
            }
        }

        Busy = true;
        _host.Config.Token = Token.Trim();
        _host.Config.GameDir = client ?? "";
        _host.Config.AutoUpdateAddon = AutoUpdate;
        _host.Config.Notify = NotifyMode;
        _host.Config.StartWithWindows = StartWithWindows;
        if (!Persist())
            return;
        WindowsStartup.Apply(StartWithWindows);
        _host.RefreshWatcher();
        SettingsMessage = "Saved. Checking the token...";
        var check = await CheckTypedToken().ConfigureAwait(true);
        Busy = false;
        if (check.Status == "ok")
        {
            var who = string.IsNullOrEmpty(check.Name) ? "." : $": {check.Name}";
            SettingsMessage = "Saved. Token OK" + who;
        }
        else
        {
            SettingsMessage = $"Saved, but the server rejected that token. Get a fresh one at {AppConstants.UploadPageUrl}.";
        }
        Pull();
    }

    [RelayCommand]
    private void SetNotify(string mode)
    {
        if (mode is "failures" or "always" or "never")
            NotifyMode = mode;
    }

    [RelayCommand]
    private void OpenUploadPage() => Dialogs?.OpenUrl(AppConstants.UploadPageUrl);

    [RelayCommand]
    private void OpenSite() => Dialogs?.OpenUrl(AppConstants.ServerBase);

    [RelayCommand]
    private void Quit()
    {
        _host?.Dispose();
        Dialogs?.Shutdown();
    }

    public void Banner(string message)
    {
        ToastText = message;
        ToastOpen = true;
        var gen = ++_toastGen;
        _ = Task.Run(async () =>
        {
            await Task.Delay(4200).ConfigureAwait(false);
            if (gen == _toastGen)
                Dispatcher.UIThread.Post(() => ToastOpen = false);
        });
    }

    private bool Persist()
    {
        try
        {
            _host!.Save();
            return true;
        }
        catch (IOException ex)
        {
            var message = "Could not save settings: " + ex.Message;
            SettingsMessage = message;
            TokenMessage = message;
            return false;
        }
    }

    private async Task<TokenCheck> CheckTypedToken()
    {
        using var client = new IngestClient();
        return await client.CheckTokenAsync(Token.Trim()).ConfigureAwait(true);
    }

    private void Pull()
    {
        if (_host is null)
            return;
        var running = _host.GameRunning;
        GameValue = running ? "Open" : "Closed";
        AddonValue = string.IsNullOrWhiteSpace(_host.Config.AddonVersion)
            ? "Not installed"
            : "v" + _host.Config.AddonVersion;
        if (_host.LastSyncUtc is { } when)
        {
            var ago = DateTimeOffset.Now - when;
            SyncValue = ago.TotalSeconds < 45 ? "Just now"
                : ago.TotalMinutes < 60 ? $"{Math.Max(1, (int)ago.TotalMinutes)}m ago"
                : when.LocalDateTime.ToString("HH:mm");
        }
        else
        {
            SyncValue = "Waiting";
        }

        if (_host.LastSyncFailed)
        {
            Presence = "bad";
            Headline = "The last sync did not land";
            Detail = string.IsNullOrWhiteSpace(_host.LastMessage)
                ? "Open Settings and check the upload token."
                : _host.LastMessage;
        }
        else if (running)
        {
            Presence = "live";
            Headline = "Characters are syncing to the tavern";
            Detail = "The game is open. GuildSync uploads when your saved data changes.";
        }
        else
        {
            Presence = "idle";
            Headline = "Idle until you log in";
            Detail = "World of Warcraft is closed. GuildSync uploads while you play, and once more when you log out.";
        }

        Activity.Clear();
        foreach (var line in _host.ActivitySnapshot())
            Activity.Add(line);
    }

    private void LoadHomePreview()
    {
        Headline = "Characters are syncing to the tavern";
        Detail = "The game is open. GuildSync uploads when your saved data changes.";
        GameValue = "Open";
        SyncValue = "2m ago";
        AddonValue = "v0.3.5";
        Presence = "live";
        Activity.Add("12:41  Synced 1 file.");
        Activity.Add("12:40  World of Warcraft opened.");
        Activity.Add("09:12  Addon is up to date (0.3.5).");
    }

    private void PullPreviewPresence()
    {
        if (Page == "home")
            Presence = "live";
    }

    private void NoteLocal(string message)
    {
        Activity.Insert(0, DateTime.Now.ToString("HH:mm") + "  " + message);
        Dialogs?.ShowToast(message);
    }
}
