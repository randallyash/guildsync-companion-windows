using System.Text.Json;

namespace GuildSync.Core;

public sealed record AddonUpdate(bool Changed, string Version, string Detail, string? Sha);

public sealed class AddonSource : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _owns;

    public AddonSource(HttpMessageHandler handler, bool disposeHandler = false)
        : this(new HttpClient(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(30) }, owns: false)
    {
    }

    public AddonSource()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, owns: true)
    {
    }

    private AddonSource(HttpClient http, bool owns)
    {
        _http = http;
        _owns = owns;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
    }

    public async Task<string?> LatestShaAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync(AppConstants.AddonCommitsUrl, ct).ConfigureAwait(false);
        if (raw is null)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                return null;
            var first = doc.RootElement[0];
            if (first.TryGetProperty("sha", out var sha) && sha.ValueKind == JsonValueKind.String)
                return string.IsNullOrEmpty(sha.GetString()) ? null : sha.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
        return null;
    }

    public async Task<string?> RemoteVersionAsync(CancellationToken ct = default)
    {
        var raw = await GetAsync(AppConstants.AddonTocUrl, ct).ConfigureAwait(false);
        return raw is null ? null : TocVersion.Parse(EncodingUtf8(raw));
    }

    public async Task<AddonUpdate> UpdateAsync(string gameDir, string? sha, CancellationToken ct = default)
    {
        var blob = await GetAsync(AppConstants.AddonArchiveUrl, ct).ConfigureAwait(false);
        if (blob is null)
            return new AddonUpdate(false, "", "download failed", sha);
        var installed = AddonInstaller.InstallFromZip(gameDir, blob);
        if (!installed.Ok)
            return new AddonUpdate(false, "", installed.Detail, sha);
        CompanionStamp.Write(gameDir);
        var version = TocVersion.ReadLocal(gameDir) ?? "";
        return new AddonUpdate(true, version, "", sha);
    }

    private async Task<byte[]?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static string EncodingUtf8(byte[] raw) => System.Text.Encoding.UTF8.GetString(raw);

    public void Dispose()
    {
        if (_owns)
            _http.Dispose();
    }
}
