namespace GuildSync.Core;

/// <summary>
/// Minimum gap between automatic uploads. A new file event during the gap
/// does not push the deadline later. Manual sync skips the gate entirely.
/// </summary>
public sealed class CooldownGate
{
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(30);

    public DateTimeOffset LastUpload { get; private set; } = DateTimeOffset.MinValue;
    public DateTimeOffset? PendingUntil { get; private set; }

    public void MarkUploaded(DateTimeOffset now) => LastUpload = now;

    public void ClearPending() => PendingUntil = null;

    /// <summary>
    /// Null means upload now. Otherwise the single moment a deferred upload
    /// should run. An existing deadline is left alone.
    /// </summary>
    public DateTimeOffset? Defer(DateTimeOffset now, TimeSpan? cooldown = null)
    {
        var window = cooldown ?? DefaultCooldown;
        var elapsed = now - LastUpload;
        if (elapsed >= window)
        {
            PendingUntil = null;
            return null;
        }

        if (PendingUntil is { } until && until > now)
            return until;

        PendingUntil = LastUpload == DateTimeOffset.MinValue
            ? now + (window - elapsed)
            : LastUpload + window;
        return PendingUntil;
    }
}
