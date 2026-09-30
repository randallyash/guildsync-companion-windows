namespace GuildSync.Core;

public sealed class SyncStamp
{
    public uint Crc { get; set; }
    public long Size { get; set; }
    public long Mtime { get; set; }
}
