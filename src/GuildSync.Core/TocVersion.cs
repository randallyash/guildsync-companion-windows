namespace GuildSync.Core;

public static class TocVersion
{
    public static string? Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimEnd('\r');
            if (line.StartsWith("## Version:", StringComparison.Ordinal))
                return line.Split(':', 2)[1].Trim();
        }
        return null;
    }

    public static string? ReadLocal(string gameDir)
    {
        var toc = Path.Combine(
            gameDir, "Interface", "AddOns", AppConstants.AddonFolder, AppConstants.AddonFolder + ".toc");
        try
        {
            return Parse(File.ReadAllText(toc));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
