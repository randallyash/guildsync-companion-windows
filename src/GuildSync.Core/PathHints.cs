using System.Text.RegularExpressions;

namespace GuildSync.Core;

public static partial class PathHints
{
    /// <summary>Steam's libraryfolders.vdf lists every library "path".</summary>
    public static IEnumerable<string> SteamLibraries(string vdf)
    {
        foreach (Match match in SteamPath().Matches(vdf))
        {
            var path = match.Groups[1].Value.Replace("\\\\", "\\");
            if (path.Length > 0)
                yield return path;
        }
    }

    /// <summary>
    /// Battle.net.config is JSON with escaped Windows paths. Keep the ones that
    /// look like a Warcraft install.
    /// </summary>
    public static IEnumerable<string> BattleNetPaths(string text)
    {
        var unescaped = text.Replace("\\/", "/");
        while (unescaped.Contains("\\\\", StringComparison.Ordinal))
            unescaped = unescaped.Replace("\\\\", "\\");
        foreach (Match match in WindowsPath().Matches(unescaped))
        {
            var path = match.Value.Trim().TrimEnd('\\', '/', ',', ' ');
            if (path.Contains("Warcraft", StringComparison.OrdinalIgnoreCase)
                || path.Contains("_classic", StringComparison.OrdinalIgnoreCase))
                yield return path;
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPath();

    [GeneratedRegex(@"[A-Za-z]:\\[^\r\n""<>|]+")]
    private static partial Regex WindowsPath();
}
