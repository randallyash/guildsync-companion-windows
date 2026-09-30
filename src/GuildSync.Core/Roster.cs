using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuildSync.Core;

public sealed record RosterCharacter(
    string Name,
    string ClassName,
    string Spec,
    string Race,
    int Level,
    double ItemLevel,
    long LastSeenUnix,
    int Id = 0,
    string Player = "",
    int? Dkp = null);

public static class RosterList
{
    public static IReadOnlyList<RosterCharacter> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("characters", out var list) || list.ValueKind != JsonValueKind.Array)
            return [];

        var found = new List<RosterCharacter>();
        foreach (var item in list.EnumerateArray())
        {
            var name = JoinName(Text(item, "name"), Text(item, "surname"));
            if (name.Length == 0)
                continue;
            found.Add(new RosterCharacter(
                name,
                Text(item, "class"),
                Text(item, "spec"),
                Text(item, "race"),
                Number(item, "level"),
                Double(item, "ilvl"),
                Long(item, "last_seen"),
                Number(item, "id"),
                Text(item, "player")));
        }

        found.Sort(static (a, b) =>
        {
            var level = b.Level.CompareTo(a.Level);
            if (level != 0)
                return level;
            return b.ItemLevel.CompareTo(a.ItemLevel);
        });
        return found;
    }

    /// <summary>
    /// Characters whose roster <c>player</c> is this member. An empty name matches nobody.
    /// </summary>
    public static IReadOnlyList<RosterCharacter> ForPlayer(IReadOnlyList<RosterCharacter> rows, string player)
    {
        var name = player.Trim();
        if (name.Length == 0)
            return [];
        return rows
            .Where(row => string.Equals(row.Player, name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Pinned main first, then highest level, then item level.</summary>
    public static IReadOnlyList<RosterCharacter> Order(IEnumerable<RosterCharacter> rows, int? mainId)
    {
        return rows
            .OrderByDescending(row => mainId is int id && id > 0 && row.Id == id)
            .ThenByDescending(row => row.Level)
            .ThenByDescending(row => row.ItemLevel)
            .ToList();
    }

    public static string Ago(long unixSeconds, DateTimeOffset now)
    {
        if (unixSeconds <= 0)
            return "";
        var ago = now - DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        if (ago < TimeSpan.Zero)
            ago = TimeSpan.Zero;
        if (ago.TotalMinutes < 1)
            return "just now";
        if (ago.TotalHours < 1)
            return $"{(int)ago.TotalMinutes}m ago";
        if (ago.TotalDays < 1)
            return $"{(int)ago.TotalHours}h ago";
        return $"{(int)ago.TotalDays}d ago";
    }

    public static string FormatItemLevel(double itemLevel)
    {
        if (itemLevel <= 0)
            return "—";
        return itemLevel.ToString("0.0", CultureInfo.InvariantCulture);
    }

    public static string FormatDkp(int? dkp)
    {
        return dkp is int value ? value.ToString(CultureInfo.InvariantCulture) : "—";
    }

    private static string JoinName(string name, string surname)
    {
        if (name.Length == 0)
            return surname;
        if (surname.Length == 0)
            return name;
        return name + " " + surname;
    }

    private static string Text(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return "";
        return (value.GetString() ?? "").Trim();
    }

    private static int Number(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0;
        return value.TryGetInt32(out var whole) ? whole : (int)value.GetDouble();
    }

    private static double Double(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0;
        return value.GetDouble();
    }

    private static long Long(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number)
            return 0;
        return value.TryGetInt64(out var whole) ? whole : (long)value.GetDouble();
    }
}

public sealed class RosterClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _owns;

    public RosterClient(HttpMessageHandler handler, bool disposeHandler = false)
        : this(new HttpClient(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(30) }, owns: false)
    {
    }

    public RosterClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, owns: true)
    {
    }

    private RosterClient(HttpClient http, bool owns)
    {
        _http = http;
        _owns = owns;
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(AppConstants.UserAgent);
    }

    public async Task<string?> GetCharacterPageAsync(int id, CancellationToken ct = default)
    {
        if (id <= 0)
            return null;
        using var request = new HttpRequestMessage(HttpMethod.Get, AppConstants.CharacterPageUrl(id));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<(int Status, string Body)> GetAsync(string? token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppConstants.RosterUrl);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.TryAddWithoutValidation("X-Upload-Token", token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ((int)response.StatusCode, body);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (0, "");
        }
    }

    public void Dispose()
    {
        if (_owns)
            _http.Dispose();
    }
}

public static partial class DkpRead
{
    [GeneratedRegex(@"ff-dkp-(pos|neg)\b[^>]*>\s*(-?[\d,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Span();

    public static int? FromHtml(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return null;
        var match = Span().Match(html);
        if (!match.Success)
            return null;
        var digits = match.Groups[2].Value.Replace(",", "", StringComparison.Ordinal);
        if (!int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
            return null;
        if (match.Groups[1].Value.Equals("neg", StringComparison.OrdinalIgnoreCase) && value > 0)
            value = -value;
        return value;
    }
}
