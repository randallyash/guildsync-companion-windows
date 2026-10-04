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

    public static bool HasMacApp(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path, "World of Warcraft*.app").Any();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsClientDir(string path)
    {
        if (!Directory.Exists(path))
            return false;
        var data = Directory.Exists(Path.Combine(path, "Interface"))
                   || Directory.Exists(Path.Combine(path, "WTF"));
        if (HasWowExe(path) && data)
            return true;
        // Battle.net's Mac client: World of Warcraft.app next to, or inside, _classic_beta_.
        var product = string.Equals(Path.GetFileName(path), AppConstants.ClientSubdir, StringComparison.OrdinalIgnoreCase);
        if (HasMacApp(path) && (data || product))
            return true;
        var parent = Directory.GetParent(path)?.FullName;
        return parent is not null && HasMacApp(parent) && (data || product);
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
        // The product folder is where addons go, not the World of Warcraft folder beside the app.
        var client = Path.Combine(full, AppConstants.ClientSubdir);
        if (IsClientDir(client))
            return client;
        if (IsClientDir(full))
            return full;

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

                List<DirectoryInfo> children;
                try
                {
                    children = new DirectoryInfo(dir).EnumerateDirectories().ToList();
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
                    // Wine prefixes link dosdevices/z: at the filesystem root.
                    // Following it walks /proc and /mnt and dies on the first
                    // unreadable entry, so the search stays inside real dirs.
                    if (child.LinkTarget is not null)
                        continue;
                    if (Pruned.Contains(child.Name))
                        continue;
                    pending.Push((child.FullName, depth + 1));
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
