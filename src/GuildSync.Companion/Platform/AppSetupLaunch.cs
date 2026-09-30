using System.Diagnostics;

namespace GuildSync.Companion.Platform;

public static class AppSetupLaunch
{
    public static void StartSilent(string installerPath)
    {
        if (!OperatingSystem.IsWindows())
            return;
        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/S",
            UseShellExecute = true,
        });
    }
}
