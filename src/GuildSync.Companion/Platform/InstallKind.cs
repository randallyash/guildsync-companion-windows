namespace GuildSync.Companion.Platform;

/// <summary>
/// How this copy of the companion was installed. Drives the update story:
/// flatpak installs are updated by the system's software updater, pacman/AUR
/// installs by the user's AUR helper - the app never prompts there. Only
/// portable installs (Forgejo release bundles) use the in-app updater.
/// </summary>
public static class InstallKind
{
    /// <summary>Running inside a flatpak sandbox (flatpak sets FLATPAK_ID).</summary>
    public static bool IsFlatpak =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID"));

    /// <summary>Installed under a system prefix (/usr, /opt) by a package manager.</summary>
    public static bool IsSystemPackage
    {
        get
        {
            var exe = Environment.ProcessPath;
            return exe is not null
                && (exe.StartsWith("/usr/", StringComparison.Ordinal)
                    || exe.StartsWith("/opt/", StringComparison.Ordinal));
        }
    }

    /// <summary>The in-app updater is in charge (portable bundle install).</summary>
    public static bool AppOwnsUpdates => !IsFlatpak && !IsSystemPackage;
}
