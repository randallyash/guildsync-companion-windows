namespace GuildSync.Core;

/// <summary>
/// Usual places a WoW: Forever client shows up under Wine, Lutris, Steam,
/// Bottles, CrossOver, or Whisky. The caller drops paths that are not there.
/// </summary>
public static class UnixSearchRoots
{
    public static IReadOnlyList<string> Candidates(string home, bool mac)
    {
        var list = new List<string>
        {
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
        };

        if (mac)
        {
            list.Add(Path.Combine(home, "Applications"));
            list.Add("/Applications");
            list.Add(Path.Combine(home, "Library", "Application Support", "CrossOver", "Bottles"));
            list.Add(Path.Combine(home, "Library", "Application Support", "Whisky"));
            list.Add(Path.Combine(home, "Library", "Application Support", "com.isaacmarovitz.Whisky"));
        }
        else
        {
            list.Add("/media");
            list.Add("/mnt");
        }

        return list;
    }
}
