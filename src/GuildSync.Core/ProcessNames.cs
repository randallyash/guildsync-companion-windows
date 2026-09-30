namespace GuildSync.Core;

public static class ProcessNames
{
    /// <summary>
    /// Windows reports the image name without ".exe" (Wow, WowClassic). Wine
    /// on Linux reports Wow.exe. Helper processes that share the prefix are
    /// not the game.
    /// </summary>
    public static bool IsWowClient(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;
        var name = processName.Trim();
        if (name.Equals("World of Warcraft", StringComparison.OrdinalIgnoreCase)
            || name.Equals("World of Warcraft Classic", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        if (name.Length < 3 || !name.StartsWith("wow", StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<string> blocked = ["voice", "error", "crash", "helper", "bugreport", "companion"];
        foreach (var word in blocked)
        {
            if (name.Contains(word, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }
}
