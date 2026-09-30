namespace GuildSync.Core;

public interface IGameProcess
{
    bool IsRunning();
}

public sealed class CompanionHost : IDisposable
{
    private readonly ConfigStore _store;
    private readonly IngestClient _ingest;
    private readonly AddonSource _addons;
    private readonly IGameProcess _game;
    private readonly TimeSpan _debounce;
    private readonly TimeSpan _heartbeatEvery;
    private readonly TimeSpan _addonEvery;
    private readonly TimeSpan _initialAddon;
    private readonly CooldownGate _gate = new();
    private readonly List<string> _activity = [];
    private readonly object _gateLock = new();

    private Timer? _heartbeat;
    private Timer? _addonTimer;
    private Timer? _debounceTimer;
    private Timer? _cooldownTimer;
    private FileSystemWatcher? _watcher;
    private bool _wasRunning;
    private int _syncing;
    private int _rerun;
    private bool _started;

    public CompanionHost(
        ConfigStore store,
        IngestClient ingest,
        AddonSource addons,
        IGameProcess game,
        TimeSpan? debounce = null,
        TimeSpan? heartbeat = null,
        TimeSpan? addonEvery = null,
        TimeSpan? initialAddon = null)
    {
        _store = store;
        _ingest = ingest;
        _addons = addons;
        _game = game;
        _debounce = debounce ?? TimeSpan.FromSeconds(2);
        _heartbeatEvery = heartbeat ?? TimeSpan.FromSeconds(60);
        _addonEvery = addonEvery ?? TimeSpan.FromHours(24);
        _initialAddon = initialAddon ?? TimeSpan.FromSeconds(5);
    }

    public AppConfig Config => _store.Data;

    public void Save() => _store.Save();
    public bool GameRunning { get; private set; }
    public bool LastSyncFailed { get; private set; }
    public DateTimeOffset? LastSyncUtc { get; private set; }
    public string LastMessage { get; private set; } = "";
    public string[] ActivitySnapshot()
    {
        lock (_activity)
            return _activity.ToArray();
    }

    public event Action? Changed;

    public void Start()
    {
        if (_started)
            return;
        _started = true;
        RefreshWatcher();
        CompanionStamp.Write(Config.GameDir);
        SampleProcess(syncOnExit: false);
        _heartbeat = new Timer(_ => SampleProcess(syncOnExit: true), null, _heartbeatEvery, _heartbeatEvery);
        _addonTimer = new Timer(async _ =>
        {
            if (Config.AutoUpdateAddon)
                await CheckAddonAsync(manual: false).ConfigureAwait(false);
        }, null, _initialAddon, _addonEvery);
        Raise();
    }

    public void RefreshWatcher()
    {
        lock (_gateLock)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
        var gameDir = Config.GameDir;
        if (string.IsNullOrWhiteSpace(gameDir))
            return;
        var account = InstallFinder.AccountDirectory(gameDir);
        if (!Directory.Exists(account))
            return;

        var watcher = new FileSystemWatcher(account)
        {
            Filter = AppConstants.SavedVariablesFile,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        watcher.Changed += (_, _) => OnFileEvent();
        watcher.Created += (_, _) => OnFileEvent();
        watcher.Renamed += (_, _) => OnFileEvent();
        watcher.Error += (_, _) => RefreshWatcher();
        lock (_gateLock)
        {
            _watcher?.Dispose();
            _watcher = watcher;
        }
    }

    public void OnFileEvent()
    {
        lock (_gateLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => _ = SyncAsync(force: false, toast: false), null, _debounce, Timeout.InfiniteTimeSpan);
        }
    }

    public void SampleProcess(bool syncOnExit)
    {
        bool running;
        try
        {
            running = _game.IsRunning();
        }
        catch
        {
            running = false;
        }

        if (running)
            RefreshWatcher();
        if (syncOnExit && _wasRunning && !running)
            _ = SyncAsync(force: false, toast: false);
        _wasRunning = running;
        GameRunning = running;
        Raise();
    }

    public async Task<string> SyncAsync(bool force, bool toast)
    {
        if (Interlocked.CompareExchange(ref _syncing, 1, 0) != 0)
        {
            Interlocked.Exchange(ref _rerun, 1);
            return "Sync already running.";
        }

        try
        {
            return await SyncCoreAsync(force, toast).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _syncing, 0);
            if (Interlocked.Exchange(ref _rerun, 0) == 1)
                _ = SyncAsync(force: false, toast: false);
        }
    }

    private async Task<string> SyncCoreAsync(bool force, bool toast)
    {
        if (!force)
        {
            DateTimeOffset? until;
            lock (_gateLock)
            {
                until = _gate.Defer(DateTimeOffset.Now);
                if (until is { } when)
                    ArmCooldown(when);
            }
            if (until is not null)
            {
                var seconds = Math.Max(1, (int)Math.Ceiling((until.Value - DateTimeOffset.Now).TotalSeconds));
                return Note($"Upload cooldown active; retrying in {seconds}s.", toast, failure: false);
            }
        }

        var gameDir = Config.GameDir;
        if (string.IsNullOrWhiteSpace(gameDir))
            return Note("No install folder configured.", toast, failure: false);

        var files = InstallFinder.SavedVarsFiles(gameDir);
        var plan = SyncPlan.Build(files, Config.LastSynced, force);
        if (!plan.ShouldUpload)
            return Note(plan.Message, toast, failure: false);

        Note(plan.Message, toast: false, failure: false);
        var ok = 0;
        var failed = 0;
        string? tokenRejected = null;
        foreach (var item in plan.Files)
        {
            var result = await _ingest.UploadAsync(Config.Token, item.Path).ConfigureAwait(false);
            if (result.Ok)
            {
                ok++;
                Config.MarkSynced(item.Path, item.State);
            }
            else
            {
                failed++;
                if (result.Status == 401)
                    tokenRejected = $"Your token was rejected. Get a fresh one at {AppConstants.UploadPageUrl}.";
            }
        }

        if (ok > 0)
        {
            lock (_gateLock)
                _gate.MarkUploaded(DateTimeOffset.Now);
            LastSyncUtc = DateTimeOffset.Now;
            LastSyncFailed = failed > 0;
        }
        else if (failed > 0)
        {
            LastSyncFailed = true;
        }

        try
        {
            _store.Save();
        }
        catch (IOException)
        {
            // The upload still happened. The next change will retry the bookkeeping.
        }

        if (tokenRejected is not null)
            Note(tokenRejected, toast: true, failure: true);

        var summary = ok > 0 ? $"Synced {ok} file(s)." : "Nothing synced.";
        if (failed > 0)
            summary += $" {failed} failed.";
        return Note(summary, toast, failure: failed > 0);
    }

    public async Task<string> CheckAddonAsync(bool manual)
    {
        var gameDir = Config.GameDir;
        if (string.IsNullOrWhiteSpace(gameDir))
            return Note("No install folder configured.", toast: manual, failure: false);

        if (manual)
            Note("Checking for addon updates...", toast: false, failure: false);

        var sha = await _addons.LatestShaAsync().ConfigureAwait(false);
        var current = Config.AddonSha;
        if (!string.IsNullOrEmpty(sha) && sha == current)
        {
            var version = string.IsNullOrEmpty(Config.AddonVersion) ? "current" : Config.AddonVersion;
            return Note($"Addon is up to date ({version}).", toast: false, failure: false);
        }

        var update = await _addons.UpdateAsync(gameDir, sha).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(update.Detail))
            return Note($"Addon update failed: {update.Detail}", toast: manual, failure: true);

        Config.AddonSha = sha ?? Config.AddonSha;
        Config.AddonVersion = update.Version;
        try
        {
            _store.Save();
        }
        catch (IOException)
        {
        }
        CompanionStamp.Write(gameDir);
        var message = string.IsNullOrEmpty(update.Version)
            ? "Addon updated."
            : $"Addon updated to {update.Version}.";
        return Note(message, toast: manual, failure: false);
    }

    public bool ShouldToast(bool failure)
    {
        return Config.Notify switch
        {
            "never" => false,
            "failures" => failure,
            _ => true,
        };
    }

    public event Action<string, string, bool>? Toast;

    private void ArmCooldown(DateTimeOffset when)
    {
        var delay = when - DateTimeOffset.Now;
        if (delay < TimeSpan.FromMilliseconds(200))
            delay = TimeSpan.FromMilliseconds(200);
        _cooldownTimer?.Dispose();
        _cooldownTimer = new Timer(_ =>
        {
            lock (_gateLock)
                _gate.ClearPending();
            _ = SyncAsync(force: false, toast: false);
        }, null, delay, Timeout.InfiniteTimeSpan);
    }

    private string Note(string message, bool toast, bool failure)
    {
        LastMessage = message;
        var line = DateTime.Now.ToString("HH:mm") + "  " + message;
        lock (_activity)
        {
            _activity.Insert(0, line);
            if (_activity.Count > 8)
                _activity.RemoveAt(_activity.Count - 1);
        }
        if (toast && ShouldToast(failure))
            Toast?.Invoke(AppConstants.AppName, message, failure);
        Raise();
        return message;
    }

    private void Raise()
    {
        try
        {
            Changed?.Invoke();
        }
        catch
        {
            // A listener must not take the watcher down.
        }
    }

    public void Dispose()
    {
        _heartbeat?.Dispose();
        _addonTimer?.Dispose();
        _debounceTimer?.Dispose();
        _cooldownTimer?.Dispose();
        _watcher?.Dispose();
    }
}
