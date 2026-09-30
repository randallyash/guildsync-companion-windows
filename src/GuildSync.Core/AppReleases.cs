using System.Net;
using System.Text.Json;

namespace GuildSync.Core;

public sealed record AppRelease(string Version, string DownloadUrl);

/// <summary>
/// Picks a newer setup from the Forgejo release list.
/// Only https links on our Forgejo host, and only the requested installer name, are eligible.
/// </summary>
public static class AppReleases
{
    public static AppRelease? Choose(string json, string currentVersion, string forgejoBase, string? assetName = null)
    {
        assetName ??= AppConstants.WindowsSetupAsset;
        if (!Uri.TryCreate(forgejoBase, UriKind.Absolute, out var forgejo))
            return null;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            AppRelease? best = null;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (Flag(release, "draft") || Flag(release, "prerelease"))
                    continue;
                var tag = Text(release, "tag_name");
                if (!VersionOrder.IsNewer(tag, currentVersion))
                    continue;
                if (best is not null && !VersionOrder.IsNewer(tag, best.Version))
                    continue;
                var url = SetupUrl(release, forgejo, assetName);
                if (url is null)
                    continue;
                best = new AppRelease(VersionOrder.Normalize(tag), url);
            }
            return best;
        }
    }

    /// <summary>Newest release tag, ignoring which installer files it carries.</summary>
    public static string? Newest(string json, string currentVersion)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            string? best = null;
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (Flag(release, "draft") || Flag(release, "prerelease"))
                    continue;
                var tag = Text(release, "tag_name");
                if (!VersionOrder.IsNewer(tag, currentVersion))
                    continue;
                if (best is not null && !VersionOrder.IsNewer(tag, best))
                    continue;
                best = VersionOrder.Normalize(tag);
            }
            return best;
        }
    }

    public static bool IsTrustedSetupUrl(string? url, string forgejoBase, string? assetName = null)
    {
        assetName ??= AppConstants.WindowsSetupAsset;
        if (!Uri.TryCreate(forgejoBase, UriKind.Absolute, out var forgejo))
            return false;
        return IsTrustedSetupUrl(url, forgejo, assetName);
    }

    private static string? SetupUrl(JsonElement release, Uri forgejo, string assetName)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(Text(asset, "name"), assetName, StringComparison.Ordinal))
                continue;
            var url = Text(asset, "browser_download_url");
            if (IsTrustedSetupUrl(url, forgejo, assetName))
                return url;
        }
        return null;
    }

    private static bool IsTrustedSetupUrl(string? url, Uri forgejo, string assetName)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(parsed.Host, forgejo.Host, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(parsed.UserInfo))
            return false;
        var name = Path.GetFileName(parsed.AbsolutePath);
        return string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Flag(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static string Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString() ?? "";
    }
}

public static class VersionOrder
{
    public static string Normalize(string version)
    {
        var text = version.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
            text = text[1..];
        return text;
    }

    public static bool IsNewer(string? remote, string? local)
    {
        if (!TryParse(remote, out var left) || !TryParse(local, out var right))
            return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] == right[i])
                continue;
            return left[i] > right[i];
        }
        return false;
    }

    private static bool TryParse(string? version, out int[] parts)
    {
        parts = [0, 0, 0];
        if (string.IsNullOrWhiteSpace(version))
            return false;
        var text = Normalize(version);
        var bits = text.Split('.');
        if (bits.Length is < 1 or > 3)
            return false;
        for (var i = 0; i < bits.Length; i++)
        {
            if (bits[i].Length == 0 || !int.TryParse(bits[i], out var number) || number < 0)
                return false;
            parts[i] = number;
        }
        return true;
    }
}

public sealed class AppUpdateClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _owns;

    public AppUpdateClient(HttpMessageHandler handler, bool disposeHandler = false)
        : this(new HttpClient(handler, disposeHandler) { Timeout = TimeSpan.FromMinutes(5) }, owns: false)
    {
    }

    public AppUpdateClient()
        : this(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }, owns: true)
    {
    }

    private AppUpdateClient(HttpClient http, bool owns)
    {
        _http = http;
        _owns = owns;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
    }

    public async Task<AppRelease?> LatestNewerAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(AppConstants.AppReleasesUrl, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return AppReleases.Choose(json, AppConstants.Version, AppConstants.ForgejoBase, AppConstants.SetupAssetName);
    }

    public async Task<string?> LatestTagAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(AppConstants.AppReleasesUrl, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return AppReleases.Newest(json, AppConstants.Version);
    }

    public async Task<string?> DownloadInstallerAsync(AppRelease release, string directory, CancellationToken ct = default)
    {
        if (!AppReleases.IsTrustedSetupUrl(release.DownloadUrl, AppConstants.ForgejoBase, AppConstants.SetupAssetName))
            return null;
        Directory.CreateDirectory(directory);
        var dest = Path.Combine(directory, Path.GetFileName(new Uri(release.DownloadUrl).AbsolutePath));
        using var response = await _http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            return null;
        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, ct).ConfigureAwait(false);
        return dest;
    }

    public void Dispose()
    {
        if (_owns)
            _http.Dispose();
    }
}
