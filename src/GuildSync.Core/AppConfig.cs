using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuildSync.Core;

public sealed class AppConfig
{
    public string Token { get; set; } = "";
    public string GameDir { get; set; } = "";
    public bool AutoUpdateAddon { get; set; } = true;
    public string AddonSha { get; set; } = "";
    public string AddonVersion { get; set; } = "";

    /// <summary>"failures" | "always" | "never"</summary>
    public string Notify { get; set; } = "failures";

    public bool StartWithWindows { get; set; } = true;

    /// <summary>Roster id of the character this member pinned as their main.</summary>
    public int? MainCharacterId { get; set; }

    public Dictionary<string, SyncStamp> Synced { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool Configured => !string.IsNullOrWhiteSpace(Token) && !string.IsNullOrWhiteSpace(GameDir);

    public SyncStamp? LastSynced(string path)
    {
        return Synced.TryGetValue(path, out var stamp) ? stamp : null;
    }

    public void MarkSynced(string path, SavedVarsState state)
    {
        if (state.Crc is not uint crc)
            return;
        Synced[path] = new SyncStamp { Crc = crc, Size = state.Size, Mtime = state.Mtime };
    }

    public void Normalize()
    {
        Token = (Token ?? "").Trim();
        GameDir ??= "";
        AddonSha ??= "";
        AddonVersion ??= "";
        if (Notify is not ("failures" or "always" or "never"))
            Notify = "failures";
        if (MainCharacterId is <= 0)
            MainCharacterId = null;
        Synced ??= new Dictionary<string, SyncStamp>(StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public string Path { get; }
    public AppConfig Data { get; private set; }

    public ConfigStore(string path)
    {
        Path = path;
        Data = new AppConfig();
        Load();
    }

    public static string DefaultDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return System.IO.Path.Combine(appData, "GuildSyncCompanion");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(xdg)
            ? System.IO.Path.Combine(home, ".config")
            : xdg;
        var preferred = System.IO.Path.Combine(root, "guildsync-companion");
        var legacy = System.IO.Path.Combine(root, "guildsync-companion-windows");
        try
        {
            if (!Directory.Exists(preferred) && Directory.Exists(legacy))
                Directory.Move(legacy, preferred);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Directory.Exists(legacy))
                return legacy;
        }
        return preferred;
    }

    public static string DefaultFile()
    {
        var overridePath = Environment.GetEnvironmentVariable("GSC_CONFIG");
        if (!string.IsNullOrWhiteSpace(overridePath))
            return overridePath;
        return System.IO.Path.Combine(DefaultDirectory(), "config.json");
    }

    public void Load()
    {
        try
        {
            var raw = File.ReadAllText(Path);
            var loaded = JsonSerializer.Deserialize<AppConfig>(raw, Json);
            Data = loaded ?? new AppConfig();
        }
        catch (FileNotFoundException)
        {
            Data = new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Data = new AppConfig();
        }
        Data.Normalize();
    }

    public void Save()
    {
        Data.Normalize();
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = Path + ".tmp";
        var payload = JsonSerializer.Serialize(Data, Json);
        File.WriteAllText(tmp, payload);
        Restrict(tmp);
        File.Move(tmp, Path, overwrite: true);
        Restrict(Path);
    }

    internal static void Restrict(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var identity = WindowsIdentity.GetCurrent().User;
                if (identity is null)
                    return;
                var security = new FileSecurity();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                new FileInfo(path).SetAccessControl(security);
                return;
            }

            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // A readable config is better than losing the token because the
            // permission call failed.
        }
    }
}
