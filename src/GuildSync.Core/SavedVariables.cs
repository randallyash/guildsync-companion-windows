namespace GuildSync.Core;

public sealed record SavedVarsState(
    bool Exists,
    bool Changed,
    bool Valid,
    uint? Crc,
    long Size,
    long Mtime);

public static class SavedVariables
{
    public static uint Crc32(ReadOnlySpan<byte> data) => System.IO.Hashing.Crc32.HashToUInt32(data);

    /// <summary>
    /// WoW writes SavedVariables with a leading CRLF before the first assignment
    /// (0d 0a then "GuildSyncDB = {"). The magic check strips leading line
    /// endings exactly like the server, and the CRC covers the whole file.
    /// </summary>
    public static byte[]? ReadPayload(string path)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var span = data.AsSpan();
        var i = 0;
        while (i < span.Length && (span[i] == (byte)'\r' || span[i] == (byte)'\n'))
            i++;
        if (!span[i..].StartsWith(AppConstants.SavedVariablesPrefix))
            return null;
        return data;
    }

    public static long FileMtime(string path)
    {
        return new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Size and mtime skip the read when nothing moved. A matching CRC after a
    /// rewrite (for example /reload) is not a real change.
    /// </summary>
    public static SavedVarsState Evaluate(string path, SyncStamp? last)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
                return Missing();
        }
        catch (IOException)
        {
            return Missing();
        }
        catch (UnauthorizedAccessException)
        {
            return Missing();
        }

        var size = info.Length;
        var mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeMilliseconds();
        if (last is not null && last.Size == size && last.Mtime == mtime)
        {
            return new SavedVarsState(true, false, true, last.Crc, size, mtime);
        }

        var data = ReadPayload(path);
        if (data is null)
            return new SavedVarsState(true, false, false, null, size, mtime);

        var crc = Crc32(data);
        var changed = last is null || last.Crc != crc;
        return new SavedVarsState(true, changed, true, crc, size, mtime);
    }

    private static SavedVarsState Missing() => new(false, false, false, null, 0, 0);
}
