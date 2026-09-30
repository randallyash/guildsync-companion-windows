namespace GuildSync.Core;

public static class CompanionStamp
{
    public const string FileName = "Companion.lua";

    public static string Body(string version) =>
        "-- Companion.lua: written by GuildSync Companion v" + version + ". Do not edit.\n" +
        "-- The app rewrites this file after every addon update.\n\n" +
        "local _, GuildSync = ...\n\n" +
        "GuildSync.CompanionVersion = \"" + version + "\"\n";

    /// <summary>No-op when the addon folder is absent. Returns true when written.</summary>
    public static bool Write(string gameDir, string? version = null)
    {
        var dir = Path.Combine(gameDir, "Interface", "AddOns", AppConstants.AddonFolder);
        if (!Directory.Exists(dir))
            return false;
        try
        {
            File.WriteAllText(Path.Combine(dir, FileName), Body(version ?? AppConstants.Version));
            return true;
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
}
