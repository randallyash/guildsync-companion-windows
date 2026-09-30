using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GuildSync.Core;

public sealed record TokenCheck(string Status, string Name, string Detail);

public sealed record UploadResult(bool Ok, int Status, string Detail);

public sealed class IngestClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _owns;

    public IngestClient(HttpMessageHandler handler, bool disposeHandler = false)
        : this(new HttpClient(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(30) }, owns: false)
    {
    }

    public IngestClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, owns: true)
    {
    }

    private IngestClient(HttpClient http, bool owns)
    {
        _http = http;
        _owns = owns;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
    }

    public async Task<TokenCheck> CheckTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return new TokenCheck("invalid", "", "no token");

        var (status, body) = await SendAsync(AppConstants.MeUrl, token, null, null, ct).ConfigureAwait(false);
        if (status == 200)
        {
            var name = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("display_name", out var display) && display.ValueKind == JsonValueKind.String)
                    name = display.GetString() ?? "";
                else if (doc.RootElement.TryGetProperty("name", out var plain) && plain.ValueKind == JsonValueKind.String)
                    name = plain.GetString() ?? "";
            }
            catch (JsonException)
            {
                // A 200 with a body we cannot read is still a valid token.
            }
            return new TokenCheck("ok", name, "");
        }

        if (status is 401 or 403)
            return new TokenCheck("invalid", "", "token rejected");
        if (status is 404 or 405 or 501)
            return new TokenCheck("ok", "", "unverified");
        return new TokenCheck("error", "", $"server returned {(status == 0 ? "no response" : status.ToString())}");
    }

    public async Task<UploadResult> UploadAsync(string token, string path, CancellationToken ct = default)
    {
        byte[] data;
        try
        {
            data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UploadResult(false, 0, $"read failed: {ex.Message}");
        }

        var boundary = Guid.NewGuid().ToString("N");
        var body = BuildMultipart(boundary, data);
        var contentType = $"multipart/form-data; boundary={boundary}";
        var (status, _) = await SendAsync(AppConstants.IngestUrl, token, body, contentType, ct).ConfigureAwait(false);
        return status switch
        {
            200 => new UploadResult(true, status, ""),
            401 => new UploadResult(false, status, "token rejected"),
            422 => new UploadResult(false, status, "payload rejected by server"),
            429 => new UploadResult(false, status, "rate limited, try later"),
            _ => new UploadResult(false, status, $"server returned {(status == 0 ? "no response" : status.ToString())}"),
        };
    }

    internal static byte[] BuildMultipart(string boundary, byte[] data)
    {
        var head = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\n" +
            $"Content-Disposition: form-data; name=\"file\"; filename=\"{AppConstants.SavedVariablesFile}\"\r\n" +
            "Content-Type: application/octet-stream\r\n\r\n");
        var tail = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");
        var body = new byte[head.Length + data.Length + tail.Length];
        Buffer.BlockCopy(head, 0, body, 0, head.Length);
        Buffer.BlockCopy(data, 0, body, head.Length, data.Length);
        Buffer.BlockCopy(tail, 0, body, head.Length + data.Length, tail.Length);
        return body;
    }

    private async Task<(int Status, byte[] Body)> SendAsync(
        string url, string token, byte[]? data, string? contentType, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(data is null ? HttpMethod.Get : HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("X-Upload-Token", token);
            if (data is not null)
            {
                var content = new ByteArrayContent(data);
                if (contentType is not null)
                    content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                request.Content = content;
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return ((int)response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return (0, Encoding.UTF8.GetBytes(ex.Message));
        }
    }

    public void Dispose()
    {
        if (_owns)
            _http.Dispose();
    }
}
