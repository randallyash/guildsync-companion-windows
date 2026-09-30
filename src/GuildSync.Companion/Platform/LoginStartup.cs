using GuildSync.Core;

namespace GuildSync.Companion.Platform;

/// <summary>
/// Start the companion when the member logs in. Windows uses the Run key.
/// Linux uses an XDG autostart entry. macOS uses a per-user LaunchAgent.
/// </summary>
public static class LoginStartup
{
    private const string LinuxDesktopName = "guildsync-companion.desktop";
    private const string MacLabel = "co.twilighttavern.guildsync-companion";

    public static void Apply(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsStartup.Apply(enabled);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            ApplyMac(enabled);
            return;
        }

        ApplyLinux(enabled);
    }

    private static void ApplyLinux(bool enabled)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrWhiteSpace(config) ? Path.Combine(home, ".config") : config;
        var path = Path.Combine(root, "autostart", LinuxDesktopName);
        try
        {
            if (!enabled)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var desktop = string.Join('\n',
            [
                "[Desktop Entry]",
                "Type=Application",
                "Name=GuildSync Companion",
                "Comment=Uploads GuildSync saved data to twilighttavern.co",
                $"Exec=\"{exe}\"",
                "Icon=guildsync-companion",
                "Terminal=false",
                "Categories=Game;",
                "X-GNOME-Autostart-enabled=true",
                "",
            ]);
            File.WriteAllText(path, desktop);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ApplyMac(bool enabled)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, "Library", "LaunchAgents", MacLabel + ".plist");
        try
        {
            if (!enabled)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var plist = string.Join('\n',
            [
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">",
                "<plist version=\"1.0\"><dict>",
                "<key>Label</key><string>" + MacLabel + "</string>",
                "<key>ProgramArguments</key><array>",
                "<string>" + EscapeXml(exe) + "</string>",
                "</array>",
                "<key>RunAtLoad</key><true/>",
                "<key>LimitLoadToSessionType</key><string>Aqua</string>",
                "</dict></plist>",
                "",
            ]);
            File.WriteAllText(path, plist);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string EscapeXml(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
