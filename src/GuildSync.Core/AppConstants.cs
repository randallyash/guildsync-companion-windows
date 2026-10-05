using System.Globalization;

namespace GuildSync.Core;

/// <summary>
/// Protocol and paths shared with the Linux companion. The server rejects
/// anything that does not match these.
/// </summary>
public static class AppConstants
{
    public const string Version = "0.1.8";
    public const string AppName = "GuildSync Companion";

    /// <summary>Same name on Windows, Linux, and Mac. The server only checks the upload token.</summary>
    public static string UserAgent => "GuildSync-Companion/" + Version;

    public const string DefaultServerBase = "https://twilighttavern.co";
    public const string ForgejoBase = "https://forgejo.fifthdread.com";
    public const string AddonRepo = "Fifthdread/Guild_Sync";
    public const string AppRepo = "Ramzal/guildsync-companion";
    public const string WindowsSetupAsset = "GuildSyncCompanion-Setup.exe";
    public const string LinuxSetupAsset = "GuildSyncCompanion-Setup.run";
    public const string LinuxDebAsset = "GuildSyncCompanion.deb";
    public const string MacSetupAsset = "GuildSyncCompanion-AppleSilicon.dmg";

    /// <summary>
    /// The installer this process should download. A copy installed by the deb
    /// lives under /opt and updates with another deb. Everything else on Linux
    /// uses the per-user .run file.
    /// </summary>
    public static string SetupAssetName
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return WindowsSetupAsset;
            if (OperatingSystem.IsMacOS())
                return MacSetupAsset;

            var path = Environment.ProcessPath ?? "";
            if (path.StartsWith("/opt/guildsync-companion", StringComparison.Ordinal))
                return LinuxDebAsset;
            return LinuxSetupAsset;
        }
    }
    public const string AddonBranch = "master";
    public const string AddonFolder = "GuildSync";
    public const string SavedVariablesFile = "GuildSync.lua";

    /// <summary>The game writes this exact prefix. The server rejects anything else.</summary>
    public static ReadOnlySpan<byte> SavedVariablesPrefix => "GuildSyncDB = {"u8;

    /// <summary>Client directory inside a WoW: Forever install (Wow*.exe, Interface, WTF).</summary>
    public const string ClientSubdir = "_classic_beta_";

    public const string IpcName = "guildsync-companion";
    public const string RunValueName = "GuildSync Companion";

    public static string ServerBase { get; set; } = ResolveServerBase();

    public static string IngestUrl => ServerBase + "/api/v1/ingest";
    public static string MeUrl => ServerBase + "/api/v1/me";
    public static string RosterUrl => ServerBase + "/api/v1/roster";
    public static string UploadPageUrl => ServerBase + "/upload";

    public static string CharacterPageUrl(int id) =>
        ServerBase + "/characters/" + id.ToString(CultureInfo.InvariantCulture);

    public static string AddonTocUrl =>
        $"{ForgejoBase}/{AddonRepo}/raw/branch/{AddonBranch}/{AddonFolder}/{AddonFolder}.toc";

    public static string AddonCommitsUrl =>
        $"{ForgejoBase}/api/v1/repos/{AddonRepo}/commits?limit=1&sha={AddonBranch}";

    public static string AddonArchiveUrl =>
        $"{ForgejoBase}/{AddonRepo}/archive/{AddonBranch}.zip";

    public static string AppReleasesUrl =>
        $"{ForgejoBase}/api/v1/repos/{AppRepo}/releases?limit=20";

    public static string ResolveServerBase()
    {
        var raw = Environment.GetEnvironmentVariable("GSC_SERVER_BASE");
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultServerBase;
        return raw.Trim().TrimEnd('/');
    }
}
