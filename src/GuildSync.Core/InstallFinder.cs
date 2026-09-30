namespace GuildSync.Core;

public static class InstallFinder
{
    public static readonly HashSet<string> Pruned = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cache", "Logs", "Errors", "Data", "Shaders",
        "node_modules", ".git", "runtime", "runners", "compatdata",
        "Windows", "WinSxS", "$Recycle.Bin", "System Volume Information",
    };

    private static readonly string[] WowExeGlobs = ["Wow*.exe", "wow*.exe", "WoW*.exe"];

    private static readonly string[] KnownFolderNames =
    [
        "World of Warcraft",
        "World of Warcraft Forever",
        "WoW Forever",
    ];

    public static bool HasWowExe(string path)
    {
        try
        {
            foreach (var pattern in WowExeGlobs)
            {
                if (Directory.EnumerateFiles(path, pattern).Any())
                    return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        return false;
    }

    public static bool IsClientDir(string path)
    {
        if (!Directory.Exists(path) || !HasWowExe(path))
            return false;
        return Directory.Exists(Path.Combine(path, "Interface"))
               || Directory.Exists(Path.Combine(path, "WTF"));
    }

    /// <summary>
    /// Accept a client dir, the World of Warcraft folder, or a prefix that
    /// contains _classic_beta_.
    /// </summary>
    public static string? Normalize(string? userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
            return null;
        string full;
        try
        {
            full = Path.GetFullPath(userPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!Directory.Exists(full))
            return null;
        if (IsClientDir(full))
            return full;

        var client = Path.Combine(full, AppConstants.ClientSubdir);
        if (IsClientDir(client))
            return client;

        if (string.Equals(Path.GetFileName(full), AppConstants.ClientSubdir, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(Path.Combine(full, "Interface")))
            return full;

        return null;
    }

    public static List<string> FindInstalls(IEnumerable<string> roots, int maxDepth = 7)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                continue;

            var pending = new Stack<(string Dir, int Depth)>();
            pending.Push((Path.GetFullPath(root), 0));
            while (pending.Count > 0)
            {
                var (dir, depth) = pending.Pop();
                if (depth > maxDepth)
                    continue;

                if (string.Equals(Path.GetFileName(dir), AppConstants.ClientSubdir, StringComparison.OrdinalIgnoreCase)
                    && IsClientDir(dir))
                {
                    if (seen.Add(dir))
                        found.Add(dir);
                    continue;
                }

                IEnumerable<string> children;
                try
                {
                    children = Directory.EnumerateDirectories(dir);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (Pruned.Contains(Path.GetFileName(child)))
                        continue;
                    pending.Push((child, depth + 1));
                }
            }
        }

        return found;
    }

    /// <summary>
    /// Check the layouts Battle.net and a manual install actually use, without
    /// walking Program Files.
    /// </summary>
    public static IEnumerable<string> KnownLayoutCandidates(string root)
    {
        yield return root;
        yield return Path.Combine(root, AppConstants.ClientSubdir);
        foreach (var name in KnownFolderNames)
        {
            yield return Path.Combine(root, name);
            yield return Path.Combine(root, name, AppConstants.ClientSubdir);
        }
    }

    public static List<string> Probe(IEnumerable<string> roots)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;
            foreach (var candidate in KnownLayoutCandidates(root))
            {
                var client = Normalize(candidate);
                if (client is not null && seen.Add(client))
                    found.Add(client);
            }
        }
        return found;
    }

    public static List<string> SavedVarsFiles(string gameDir)
    {
        var account = Path.Combine(gameDir, "WTF", "Account");
        if (!Directory.Exists(account))
            return [];

        var list = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(account))
            {
                var file = Path.Combine(dir, "SavedVariables", AppConstants.SavedVariablesFile);
                if (File.Exists(file))
                    list.Add(file);
            }
        }
        catch (IOException)
        {
            return list;
        }
        catch (UnauthorizedAccessException)
        {
            return list;
        }

        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public static string AccountDirectory(string gameDir) =>
        Path.Combine(gameDir, "WTF", "Account");
}
