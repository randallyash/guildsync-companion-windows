using System.Runtime.Versioning;
using GuildSync.Core;
using Microsoft.Win32;

namespace GuildSync.Companion.Platform;

public readonly record struct SearchRoots(IReadOnlyList<string> Probe, IReadOnlyList<string> Walk);

/// <summary>
/// Where a Windows WoW: Forever client actually lives. Program Files is probed
/// at the known Battle.net layout instead of being walked.
/// </summary>
public static class WindowsInstallRoots
{
    public static SearchRoots Collect()
    {
        var probe = new List<string>();
        var walk = new List<string>();

        void AddProbe(string? path)
        {
            var full = Existing(path);
            if (full is not null)
                probe.Add(full);
        }

        void AddWalk(string? path)
        {
            var full = Existing(path);
            if (full is not null)
                walk.Add(full);
        }

        foreach (var extra in Split(Environment.GetEnvironmentVariable("GSC_EXTRA_ROOTS")))
        {
            AddProbe(extra);
            AddWalk(extra);
        }

        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var candidate in UnixSearchRoots.Candidates(home, OperatingSystem.IsMacOS()))
                AddWalk(candidate);
            return Dedup(probe, walk);
        }

        AddProbe(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddProbe(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddProbe(Environment.GetEnvironmentVariable("ProgramW6432"));

        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        AddWalk(Path.Combine(user, "Games"));
        AddWalk(Path.Combine(user, "games"));

        AddSteam(AddProbe, AddWalk);
        AddBattleNet(AddProbe);
        AddUninstall(AddProbe);

        return Dedup(probe, walk);
    }

    [SupportedOSPlatform("windows")]
    private static void AddSteam(Action<string?> probe, Action<string?> walk)
    {
        var steamRoots = new List<string>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            var steamPath = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(steamPath))
                steamRoots.Add(steamPath.Replace('/', Path.DirectorySeparatorChar));
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (IOException)
        {
        }

        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        steamRoots.Add(Path.Combine(pf86, "Steam"));

        foreach (var steam in steamRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                walk(Path.Combine(steam, "steamapps", "common"));
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var library in PathHints.SteamLibraries(text))
            {
                probe(library);
                walk(Path.Combine(library, "steamapps", "common"));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddBattleNet(Action<string?> probe)
    {
        var config = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Battle.net",
            "Battle.net.config");
        if (!File.Exists(config))
            return;
        try
        {
            foreach (var path in PathHints.BattleNetPaths(File.ReadAllText(config)))
                probe(path);
        }
        catch (IOException)
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddUninstall(Action<string?> probe)
    {
        string[] keys =
        [
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        ];
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var path in keys)
                ReadUninstall(hive, path, probe);
        }

        try
        {
            using var wow = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft");
            probe(wow?.GetValue("InstallPath") as string);
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (IOException)
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReadUninstall(RegistryKey hive, string path, Action<string?> probe)
    {
        try
        {
            using var root = hive.OpenSubKey(path);
            if (root is null)
                return;
            foreach (var name in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(name);
                var display = sub?.GetValue("DisplayName") as string ?? "";
                if (!display.Contains("Warcraft", StringComparison.OrdinalIgnoreCase))
                    continue;
                probe(sub?.GetValue("InstallLocation") as string);
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static SearchRoots Dedup(List<string> probe, List<string> walk)
    {
        return new SearchRoots(
            probe.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            walk.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string? Existing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            var full = Path.GetFullPath(path);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Split(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;
        foreach (var part in raw.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return part;
    }
}
