using System.IO.Compression;

namespace GuildSync.Core;

public readonly record struct InstallResult(bool Ok, string Detail);

public static class AddonInstaller
{
    /// <summary>
    /// Extract the GuildSync folder from a Forgejo repo archive into
    /// Interface/AddOns/GuildSync. The game executable, archives, and every
    /// other folder are left alone. Entries that would escape the addon
    /// folder are skipped.
    /// </summary>
    public static InstallResult InstallFromZip(string gameDir, byte[] blob)
    {
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(new MemoryStream(blob), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return new InstallResult(false, "downloaded archive is not a valid zip");
        }

        using (archive)
        {
            var prefix = FindPrefix(archive.Entries.Select(e => e.FullName.Replace('\\', '/')));
            if (prefix is null)
                return new InstallResult(false, "archive does not contain the addon folder");

            var target = Path.Combine(gameDir, "Interface", "AddOns", AppConstants.AddonFolder);
            Directory.CreateDirectory(target);
            var root = Path.GetFullPath(target);

            foreach (var entry in archive.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.EndsWith('/'))
                    continue;
                var rel = name[prefix.Length..];
                if (rel.Length == 0 || rel.Contains("..", StringComparison.Ordinal))
                    continue;

                var dest = Path.GetFullPath(Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !string.Equals(dest, root, StringComparison.Ordinal))
                    continue;

                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var src = entry.Open();
                using var output = File.Create(dest);
                src.CopyTo(output);
            }
        }

        return new InstallResult(true, "");
    }

    internal static string? FindPrefix(IEnumerable<string> names)
    {
        var marker = "/" + AppConstants.AddonFolder + "/";
        foreach (var name in names)
        {
            var idx = name.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0)
                return name[..(idx + marker.Length)];
            var rooted = AppConstants.AddonFolder + "/";
            if (name.StartsWith(rooted, StringComparison.Ordinal))
                return rooted;
        }
        return null;
    }
}
