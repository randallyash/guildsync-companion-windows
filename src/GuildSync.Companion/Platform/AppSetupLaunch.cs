using System.Diagnostics;
using GuildSync.Core;

namespace GuildSync.Companion.Platform;

public static class AppSetupLaunch
{
    public static void StartSilent(string installerPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/S",
                UseShellExecute = true,
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            StartDetached(MacScript(installerPath));
            return;
        }

        if (string.Equals(Path.GetFileName(installerPath), AppConstants.LinuxDebAsset, StringComparison.OrdinalIgnoreCase))
            StartDetached(DebScript(installerPath));
        else
            StartDetached(RunScript(installerPath));
    }

    private static string RunScript(string installerPath)
    {
        TryMarkExecutable(installerPath);
        return $"sleep 2\nexec \"{installerPath}\"\n";
    }

    private static string DebScript(string installerPath)
    {
        return string.Join('\n',
        [
            "sleep 2",
            "if command -v pkexec >/dev/null 2>&1; then",
            $"  pkexec dpkg -i \"{installerPath}\"",
            "elif command -v sudo >/dev/null 2>&1; then",
            $"  sudo dpkg -i \"{installerPath}\"",
            "else",
            "  exit 1",
            "fi",
            "if [ -x /usr/bin/guildsync-companion ]; then",
            "  setsid /usr/bin/guildsync-companion --show >/dev/null 2>&1 < /dev/null &",
            "fi",
            "",
        ]);
    }

    private static string MacScript(string dmgPath)
    {
        var dest = MacAppDestination();
        return string.Join('\n',
        [
            "set -e",
            "sleep 2",
            "mnt=$(mktemp -d)",
            "cleanup() { hdiutil detach \"$mnt\" >/dev/null 2>&1 || true; }",
            "trap cleanup EXIT",
            $"hdiutil attach -nobrowse -readonly -mountpoint \"$mnt\" \"{dmgPath}\"",
            $"rm -rf \"{dest}\"",
            $"mkdir -p \"{Path.GetDirectoryName(dest)}\"",
            "cp -R \"$mnt/GuildSync Companion.app\" \"" + Path.GetDirectoryName(dest) + "/\"",
            "xattr -dr com.apple.quarantine \"" + dest + "\" >/dev/null 2>&1 || true",
            "cleanup",
            "trap - EXIT",
            $"open -n \"{dest}\" --args --show",
            "",
        ]);
    }

    /// <summary>
    /// Replace the copy that is running. A drag install lives in /Applications.
    /// A copy that was not dragged there updates under the home Applications folder.
    /// </summary>
    private static string MacAppDestination()
    {
        const string marker = ".app/Contents/MacOS/";
        var running = Environment.ProcessPath ?? "";
        var index = running.IndexOf(marker, StringComparison.Ordinal);
        if (index > 0)
            return running[..(index + 4)];
        return "$HOME/Applications/GuildSync Companion.app";
    }

    private static void StartDetached(string script)
    {
        var path = Path.Combine(Path.GetTempPath(), "guildsync-install-" + Guid.NewGuid().ToString("N") + ".sh");
        File.WriteAllText(path, "#!/bin/sh\nset -u\n" + script);
        TryMarkExecutable(path);
        if (OperatingSystem.IsMacOS())
        {
            // setsid is a Linux command. nohup keeps the installer alive after this app exits.
            Process.Start(new ProcessStartInfo
            {
                FileName = "/bin/sh",
                ArgumentList = { "-c", "nohup \"" + path + "\" >/dev/null 2>&1 &" },
                UseShellExecute = false,
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "setsid",
            ArgumentList = { path },
            UseShellExecute = false,
        });
    }

    private static void TryMarkExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
