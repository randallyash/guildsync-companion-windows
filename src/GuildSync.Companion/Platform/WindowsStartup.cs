using System.Runtime.Versioning;
using GuildSync.Core;
using Microsoft.Win32;

namespace GuildSync.Companion.Platform;

public static class WindowsStartup
{
    private const string Subkey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
            return;
        ApplyWindows(enabled);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Subkey, writable: true);
            if (key is null)
                return;
            if (!enabled)
            {
                key.DeleteValue(AppConstants.RunValueName, throwOnMissingValue: false);
                return;
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return;
            key.SetValue(AppConstants.RunValueName, $"\"{exe}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
