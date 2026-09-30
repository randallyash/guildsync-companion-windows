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
    private AppRelease? _pendingAppUpdate;
    private string _addonUpdateNote = "";
    private List<RosterCharacter> _owned = [];
    private string _ownedStatus = "";
    private int? _previewMainId = 1560;
    private int _characterLoad;
    private DateTimeOffset? _appliedSyncUtc;

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
        Page = preview == "update" ? "home" : preview;
        Token = "";
        GameDir = @"C:\Program Files\World of Warcraft\_classic_beta_";
        AutoUpdate = true;
        NotifyMode = "failures";
        StartWithWindows = true;
        if (preview is "home" or "settings" or "about" or "characters" or "update")
            LoadHomePreview();
        if (preview == "update")
        {
            UpdatePromptOpen = true;
            UpdatePromptDetail = "Companion 0.1.7 is ready. GuildSync will close, install, and open again.";
        }
        if (preview == "characters")
            LoadCharacterPreview();
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
    public ObservableCollection<CharacterRow> Characters { get; } = [];

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
    [ObservableProperty] private string charactersStatus = "";
    [ObservableProperty] private bool charactersBusy;
    [ObservableProperty] private bool updatePromptOpen;
    [ObservableProperty] private bool updatePromptBusy;
    [ObservableProperty] private string updatePromptDetail = "";
    [ObservableProperty] private bool toastOpen;
    [ObservableProperty] private string toastText = "";

    public bool ShowNav => Page is "home" or "characters" or "settings" or "about";
    public bool NotifyFailures => NotifyMode == "failures";
    public bool NotifyAlways => NotifyMode == "always";
    public bool NotifyNever => NotifyMode == "never";
    public bool PresenceLive => Presence == "live";
    public bool PresenceIdle => Presence == "idle";
    public bool PresenceBad => Presence == "bad";
    public char TokenMask => TokenHidden ? '●' : '\0';
    public string TokenToggleLabel => TokenHidden ? "Show" : "Hide";
    public string LoginStartLabel => OperatingSystem.IsWindows() ? "Start with Windows" : "Start when you log in";

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
        LoginStartup.Apply(_host.Config.StartWithWindows);
        _host.Start();
        Pull();
        _ = CheckOnLaunchAsync();
    }

    private async Task CheckOnLaunchAsync()
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (_host is null || UpdatePromptOpen || UpdatePromptBusy)
            return;

        if (_host.Config.AutoUpdateAddon && !string.IsNullOrWhiteSpace(_host.Config.GameDir))
        {
            await _host.CheckAddonAsync(manual: false).ConfigureAwait(true);
            Pull();
        }

        if (UpdatePromptOpen || UpdatePromptBusy)
            return;

        try
        {
            using var client = new AppUpdateClient();
            if (OperatingSystem.IsLinux())
            {
                var tag = await client.LatestTagAsync().ConfigureAwait(true);
                if (string.IsNullOrEmpty(tag) || UpdatePromptOpen)
                    return;
                _host.Mention($"Companion {tag} is out. Update it with paru -Syu.");
                Dialogs?.ShowWindow();
                Pull();
                return;
            }

            var release = await client.LatestNewerAsync().ConfigureAwait(true);
            if (release is null || UpdatePromptOpen)
                return;
            OfferAppUpdate(release, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // A quiet miss at launch is better than a toast before anyone asked.
        }
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
        var depth = OperatingSystem.IsWindows() ? 5 : 8;
        var found = await Task.Run(() =>
        {
            var list = InstallFinder.Probe(roots.Probe);
            var seen = new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);
            foreach (var walked in InstallFinder.FindInstalls(roots.Walk, depth))
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
            SetupMessage = "Completed, but the addon could not be installed automatically. You can retry from Check for updates.";
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
            LoginStartup.Apply(_host.Config.StartWithWindows);
        Pull();
    }

    [RelayCommand]
    private void ShowHome() => Page = _host?.Config.Configured == false && !_preview ? "token" : "home";

    [RelayCommand]
    private async Task ShowCharactersAsync()
    {
        if (_host is not null && !_host.Config.Configured && !_preview)
        {
            Page = "token";
            Dialogs?.ShowWindow();
            return;
        }

        Page = "characters";
        Dialogs?.ShowWindow();
        await LoadCharactersAsync(keepOnError: false).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task RefreshCharactersAsync() => LoadCharactersAsync(keepOnError: _owned.Count > 0);

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
    private async Task CheckUpdatesAsync()
    {
        if (_preview || _host is null)
        {
            const string preview = "Preview only. Nothing was updated.";
            SettingsMessage = preview;
            NoteLocal(preview);
            return;
        }

        SettingsMessage = "Checking for updates...";
        Dialogs?.ShowWindow();
        var addon = await _host.CheckAddonAsync(manual: true).ConfigureAwait(true);
        _addonUpdateNote = addon;

        try
        {
            using var client = new AppUpdateClient();
            if (OperatingSystem.IsLinux())
            {
                var tag = await client.LatestTagAsync().ConfigureAwait(true);
                var line = string.IsNullOrEmpty(tag)
                    ? $"Companion is up to date ({AppConstants.Version})."
                    : $"Companion {tag} is out. Update it with paru -Syu.";
                FinishUpdateCheck(addon, line);
                return;
            }

            var release = await client.LatestNewerAsync().ConfigureAwait(true);
            if (release is null)
            {
                FinishUpdateCheck(addon, $"Companion is up to date ({AppConstants.Version}).");
                return;
            }
            OfferAppUpdate(release, addon);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            FinishUpdateCheck(addon, "Couldn't reach the app release list. Try again in a bit.");
        }
    }

    [RelayCommand]
    private void DeclineUpdate()
    {
        if (UpdatePromptBusy)
            return;
        var version = _pendingAppUpdate?.Version;
        UpdatePromptOpen = false;
        _pendingAppUpdate = null;
        if (_preview || _host is null || version is null)
            return;
        FinishUpdateCheck(_addonUpdateNote, $"Companion {version} is ready when you want it.");
    }

    [RelayCommand]
    private async Task AcceptUpdateAsync()
    {
        if (UpdatePromptBusy)
            return;
        if (_preview || _host is null || _pendingAppUpdate is null)
        {
            UpdatePromptDetail = "Preview only. Nothing was installed.";
            return;
        }

        UpdatePromptBusy = true;
        var release = _pendingAppUpdate;
        UpdatePromptDetail = $"Downloading companion {release.Version}...";
        try
        {
            using var client = new AppUpdateClient();
            var folder = Path.Combine(Path.GetTempPath(), "GuildSyncCompanion");
            var installer = await client.DownloadInstallerAsync(release, folder).ConfigureAwait(true);
            if (installer is null)
            {
                UpdatePromptDetail = "The download didn't finish. You can try again.";
                UpdatePromptBusy = false;
                return;
            }

            UpdatePromptDetail = "Installing. GuildSync will close and open again.";
            _host.Mention($"Updating the app to {release.Version}.");
            AppSetupLaunch.StartSilent(installer);
            Dialogs?.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            UpdatePromptDetail = "Couldn't download the update. Try again in a bit.";
            UpdatePromptBusy = false;
        }
    }

    private void OfferAppUpdate(AppRelease release, string addon)
    {
        _pendingAppUpdate = release;
        UpdatePromptBusy = false;
        UpdatePromptDetail = $"Companion {release.Version} is ready. GuildSync will close, install, and open again.";
        UpdatePromptOpen = true;
        SettingsMessage = string.IsNullOrWhiteSpace(addon)
            ? $"Companion {release.Version} is ready."
            : $"{addon} Companion {release.Version} is ready.";
        Dialogs?.ShowWindow();
    }

    private void FinishUpdateCheck(string addon, string app)
    {
        _host?.Mention(app);
        SettingsMessage = $"{addon} {app}";
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
        LoginStartup.Apply(StartWithWindows);
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

    private void OpenCharacter(int id)
    {
        if (id <= 0)
            return;
        Dialogs?.OpenUrl(AppConstants.CharacterPageUrl(id));
    }

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

        // A sync just landed and this page is open. Pick up level, DKP, and last seen.
        if (Page == "characters"
            && !CharactersBusy
            && _host.LastSyncUtc is { } synced
            && synced != _appliedSyncUtc)
        {
            _ = LoadCharactersAsync(keepOnError: true);
        }
    }

    private void PinCharacter(int id)
    {
        if (id <= 0)
            return;

        if (_preview || _host is null)
        {
            _previewMainId = _previewMainId == id ? null : id;
            ShowOwned();
            return;
        }

        _host.Config.MainCharacterId = _host.Config.MainCharacterId == id ? null : id;
        if (!Persist())
        {
            CharactersStatus = "Couldn't save which character you main.";
            return;
        }
        ShowOwned();
    }

    private async Task LoadCharactersAsync(bool keepOnError)
    {
        var gen = ++_characterLoad;
        var seenSync = _host?.LastSyncUtc;
        if (_preview || _host is null)
        {
            LoadCharacterPreview();
            _appliedSyncUtc = seenSync;
            return;
        }

        CharactersBusy = true;
        CharactersStatus = _owned.Count > 0 ? "Refreshing..." : "Pulling your characters...";
        var token = _host.Config.Token;
        try
        {
            using var roster = new RosterClient();
            using var ingest = new IngestClient();
            var rosterTask = roster.GetAsync(token);
            var meTask = ingest.CheckTokenAsync(token);
            await Task.WhenAll(rosterTask, meTask).ConfigureAwait(true);
            if (gen != _characterLoad)
                return;
            var (status, body) = await rosterTask.ConfigureAwait(true);
            var me = await meTask.ConfigureAwait(true);

            if (me.Status == "invalid")
            {
                FailCharacters("That token doesn't belong to a member. Check it in Settings.", keepOnError);
                return;
            }
            if (me.Status == "error" || status == 0)
            {
                FailCharacters("Couldn't reach the tavern. Try again in a bit.", keepOnError);
                return;
            }
            if (string.IsNullOrWhiteSpace(me.Name))
            {
                FailCharacters("We couldn't tell which account this token belongs to.", keepOnError);
                return;
            }
            if (status != 200)
            {
                FailCharacters("The tavern didn't send your characters.", keepOnError);
                return;
            }

            IReadOnlyList<RosterCharacter> parsed;
            try
            {
                parsed = RosterList.Parse(body);
            }
            catch (System.Text.Json.JsonException)
            {
                FailCharacters("The roster didn't look right.", keepOnError);
                return;
            }

            var mine = RosterList.ForPlayer(parsed, me.Name);
            if (mine.Count == 0)
            {
                _owned = [];
                Characters.Clear();
                CharactersStatus = $"No characters synced for {me.Name} yet. Log one in and it'll show up here.";
                return;
            }

            var withDkp = new RosterCharacter[mine.Count];
            await Task.WhenAll(mine.Select((character, index) => FillDkpAsync(roster, character, withDkp, index))).ConfigureAwait(true);
            if (gen != _characterLoad)
                return;
            _owned = withDkp.ToList();
            _ownedStatus = mine.Count == 1
                ? "1 of yours. Pin it if this is the character you main."
                : $"{mine.Count} of yours, highest level first. Pin the one you main.";
            ShowOwned();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            if (gen != _characterLoad)
                return;
            FailCharacters("Couldn't reach the tavern. Try again in a bit.", keepOnError);
        }
        finally
        {
            if (gen == _characterLoad)
            {
                CharactersBusy = false;
                _appliedSyncUtc = _host?.LastSyncUtc;
                if (Page == "characters" && _host?.LastSyncUtc is { } now && now != seenSync)
                    _ = LoadCharactersAsync(keepOnError: true);
            }
        }
    }

    private static async Task FillDkpAsync(RosterClient roster, RosterCharacter character, RosterCharacter[] dest, int index)
    {
        var html = await roster.GetCharacterPageAsync(character.Id).ConfigureAwait(false);
        dest[index] = character with { Dkp = DkpRead.FromHtml(html) };
    }

    private void LoadCharacterPreview()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _owned =
        [
            new RosterCharacter("Aldric Hollowbrook", "Paladin", "Retribution", "Dwarf", 56, 49.7, now - 2 * 86400, Id: 1560, Player: "You", Dkp: 42),
            new RosterCharacter("Balgor Steelclaw", "Mage", "Fire", "Dwarf", 54, 42.9, now - 2 * 86400, Id: 1559, Player: "You", Dkp: 0),
            new RosterCharacter("Rukh Jadefire", "Rogue", "Combat", "Orc", 36, 39.4, now - 3600, Id: 1562, Player: "You", Dkp: -3),
        ];
        _ownedStatus = "3 of yours, highest level first. Pin the one you main.";
        ShowOwned();
    }

    private void FailCharacters(string status, bool keepOnError)
    {
        if (keepOnError && _owned.Count > 0)
        {
            CharactersStatus = status;
            return;
        }
        ClearCharacters(status);
    }

    private void ClearCharacters(string status)
    {
        _owned = [];
        Characters.Clear();
        CharactersStatus = status;
    }

    private void ShowOwned()
    {
        var main = _preview || _host is null ? _previewMainId : _host.Config.MainCharacterId;
        var now = DateTimeOffset.Now;
        Characters.Clear();
        foreach (var character in RosterList.Order(_owned, main))
        {
            var detail = character.ClassName;
            if (character.Spec.Length > 0 && !string.Equals(character.Spec, character.ClassName, StringComparison.OrdinalIgnoreCase))
                detail = detail.Length == 0 ? character.Spec : detail + " · " + character.Spec;
            if (character.Race.Length > 0)
                detail = detail.Length == 0 ? character.Race : detail + " · " + character.Race;
            var id = character.Id;
            Characters.Add(new CharacterRow
            {
                Id = id,
                Name = character.Name,
                Detail = detail,
                Level = character.Level > 0 ? character.Level.ToString() : "—",
                ItemLevel = RosterList.FormatItemLevel(character.ItemLevel),
                Dkp = RosterList.FormatDkp(character.Dkp),
                Seen = RosterList.Ago(character.LastSeenUnix, now),
                IsMain = main is int pinned && pinned == id,
                OpenCommand = new RelayCommand(() => OpenCharacter(id)),
                PinCommand = new RelayCommand(() => PinCharacter(id)),
                NameBrush = CharacterRow.BrushFor(character.ClassName),
                DkpBrush = CharacterRow.DkpColor(character.Dkp),
            });
        }
        CharactersStatus = _ownedStatus;
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
