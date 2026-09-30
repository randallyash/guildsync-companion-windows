namespace GuildSync.Core;

/// <summary>
/// Usual places a WoW: Forever client shows up. Linux is Wine, Lutris, Steam,
/// and Bottles. Mac is the native client in Applications.
/// </summary>
public static class UnixSearchRoots
{
    public static IReadOnlyList<string> Candidates(string home, bool mac)
    {
        if (mac)
        {
            return
            [
                Path.Combine(home, "Applications"),
                "/Applications",
                Path.Combine(home, "Games"),
                Path.Combine(home, "games"),
            ];
        }

        return
        [
            Path.Combine(home, "Games"),
            Path.Combine(home, "games"),
            Path.Combine(home, ".wine", "drive_c"),
            Path.Combine(home, ".local", "share", "lutris"),
            Path.Combine(home, ".var", "app", "net.lutris.Lutris"),
            Path.Combine(home, "Steam"),
            Path.Combine(home, ".steam", "steam", "steamapps", "compatdata"),
            Path.Combine(home, ".local", "share", "Steam", "steamapps", "compatdata"),
            Path.Combine(home, ".steam", "root", "steamapps", "compatdata"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "steamapps", "compatdata"),
            Path.Combine(home, ".local", "share", "bottles", "bottles"),
            Path.Combine(home, ".var", "app", "com.usebottles.bottles", "data", "bottles", "bottles"),
            "/media",
            "/mnt",
        ];
    }
}
