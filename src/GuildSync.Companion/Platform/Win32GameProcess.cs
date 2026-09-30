using System.Diagnostics;
using GuildSync.Core;

namespace GuildSync.Companion.Platform;

/// <summary>
/// Name-only check, the same fact Task Manager shows. Never opens the game
/// for memory access and never injects, hooks, or sends input.
/// </summary>
public sealed class Win32GameProcess : IGameProcess
{
    public bool IsRunning()
    {
        if (OperatingSystem.IsLinux())
            return LinuxComm();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (ProcessNames.IsWowClient(process.ProcessName))
                    return true;
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    /// <summary>
    /// The Windows build reads the process list. This branch exists so the same
    /// window can be reviewed on Linux, where Wine reports Wow.exe under /proc.
    /// </summary>
    private static bool LinuxComm()
    {
        if (!Directory.Exists("/proc"))
            return false;
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories("/proc");
        }
        catch (IOException)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            if (!int.TryParse(Path.GetFileName(entry), out _))
                continue;
            try
            {
                var comm = File.ReadAllText(Path.Combine(entry, "comm")).Trim();
                if (ProcessNames.IsWowClient(comm))
                    return true;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
        return false;
    }
}
