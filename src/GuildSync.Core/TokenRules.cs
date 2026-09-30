namespace GuildSync.Core;

public static class TokenRules
{
    /// <summary>
    /// Local sanity check before any network call. The Guild Hall machine API
    /// key uses an ffk_ prefix and looks like a token, but upload endpoints
    /// reject it.
    /// </summary>
    public static string? Problem(string? token)
    {
        var t = (token ?? "").Trim();
        if (t.Length == 0)
            return $"Paste your upload token from {AppConstants.UploadPageUrl}.";
        if (t.StartsWith("ffk_", StringComparison.Ordinal))
            return "That is a Guild Hall API key, not an upload token. " +
                   $"Get one at {AppConstants.UploadPageUrl}.";
        return null;
    }
}
