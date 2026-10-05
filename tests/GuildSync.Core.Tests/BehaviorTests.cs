using System.IO.Compression;
using System.Net;
using System.Text;
using Xunit;

namespace GuildSync.Core.Tests;

public class BehaviorTests
{
    [Fact]
    public void Crc32_matches_zlib()
    {
        Assert.Equal(0xCBF43926u, SavedVariables.Crc32("123456789"u8));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("ffk_live_key", true)]
    [InlineData("real-upload-token", false)]
    public void Token_rules_reject_blanks_and_api_keys(string token, bool rejected)
    {
        Assert.Equal(rejected, TokenRules.Problem(token) is not null);
    }

    [Fact]
    public void Payload_accepts_the_crlf_wow_writes()
    {
        var dir = Temp();
        var path = Path.Combine(dir, "GuildSync.lua");
        var body = Encoding.ASCII.GetBytes("\r\nGuildSyncDB = {\n}\n");
        File.WriteAllBytes(path, body);

        var state = SavedVariables.Evaluate(path, null);
        Assert.True(state.Valid);
        Assert.True(state.Changed);
        Assert.Equal(SavedVariables.Crc32(body), state.Crc);
    }

    [Fact]
    public void Payload_rejects_a_different_addon_file()
    {
        var path = Path.Combine(Temp(), "GuildSync.lua");
        File.WriteAllText(path, "SomeOtherDB = {\n}\n");
        var state = SavedVariables.Evaluate(path, null);
        Assert.True(state.Exists);
        Assert.False(state.Valid);
    }

    [Fact]
    public void Unchanged_mtime_skips_the_read_and_a_reload_is_not_a_change()
    {
        var path = Path.Combine(Temp(), "GuildSync.lua");
        File.WriteAllText(path, "\nGuildSyncDB = {\n}\n");
        var first = SavedVariables.Evaluate(path, null);
        var again = SavedVariables.Evaluate(path, new SyncStamp
        {
            Crc = first.Crc!.Value,
            Size = first.Size,
            Mtime = first.Mtime,
        });
        Assert.False(again.Changed);
        Assert.True(again.Valid);

        File.WriteAllText(path, "\nGuildSyncDB = {\n}\n");
        var rewritten = SavedVariables.Evaluate(path, new SyncStamp
        {
            Crc = first.Crc!.Value,
            Size = first.Size,
            Mtime = first.Mtime - 5_000,
        });
        Assert.False(rewritten.Changed);
    }

    [Fact]
    public void Plan_messages_match_the_linux_companion()
    {
        var empty = SyncPlan.Build([], _ => null, force: false);
        Assert.Contains("Play once", empty.Message);

        var dir = Temp();
        var bad = Path.Combine(dir, "GuildSync.lua");
        File.WriteAllText(bad, "nope");
        var invalid = SyncPlan.Build([bad], _ => null, force: false);
        Assert.Contains("not a GuildSync export", invalid.Message);

        var good = Path.Combine(dir, "ok.lua");
        File.WriteAllText(good, "GuildSyncDB = {}\n");
        var first = SyncPlan.Build([good], _ => null, force: false);
        Assert.True(first.ShouldUpload);
        var stamp = new SyncStamp
        {
            Crc = first.Files[0].State.Crc!.Value,
            Size = first.Files[0].State.Size,
            Mtime = first.Files[0].State.Mtime,
        };
        var same = SyncPlan.Build([good], _ => stamp, force: false);
        Assert.Equal("Already up to date.", same.Message);
        var forced = SyncPlan.Build([good], _ => stamp, force: true);
        Assert.True(forced.ShouldUpload);
    }

    [Fact]
    public void Cooldown_does_not_slide()
    {
        var gate = new CooldownGate();
        var start = DateTimeOffset.Parse("2026-09-30T12:00:00Z");
        gate.MarkUploaded(start);
        var first = gate.Defer(start.AddSeconds(10));
        var second = gate.Defer(start.AddSeconds(20));
        Assert.Equal(start.AddSeconds(30), first);
        Assert.Equal(first, second);
        Assert.Null(gate.Defer(start.AddSeconds(31)));
    }

    [Fact]
    public void Normalize_finds_the_classic_beta_client()
    {
        var root = Temp();
        var client = Path.Combine(root, "World of Warcraft", AppConstants.ClientSubdir);
        Directory.CreateDirectory(Path.Combine(client, "Interface"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), []);

        Assert.Equal(client, InstallFinder.Normalize(Path.Combine(root, "World of Warcraft")));
        Assert.Equal(client, InstallFinder.Normalize(client));
        Assert.Null(InstallFinder.Normalize(root));

        var decoy = Path.Combine(root, "Games", "Cache", AppConstants.ClientSubdir);
        Directory.CreateDirectory(Path.Combine(decoy, "Interface"));
        File.WriteAllBytes(Path.Combine(decoy, "Wow.exe"), []);
        var found = InstallFinder.FindInstalls([Path.Combine(root, "Games")]);
        Assert.DoesNotContain(found, p => p.Contains("Cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Walk_skips_symlinks_and_unreadable_dirs()
    {
        var root = Temp();
        var games = Path.Combine(root, "Games");
        var client = Path.Combine(games, "wow", AppConstants.ClientSubdir);
        Directory.CreateDirectory(Path.Combine(client, "Interface"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), []);

        // Wine prefix layout: dosdevices/z: is a symlink at the filesystem root.
        var dosdevices = Path.Combine(games, "battlenet", "pfx", "pfx", "dosdevices");
        Directory.CreateDirectory(dosdevices);
        Directory.CreateSymbolicLink(Path.Combine(dosdevices, "z:"), root);

        var locked = Path.Combine(games, "locked");
        Directory.CreateDirectory(locked);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(locked, UnixFileMode.None);

        var found = InstallFinder.FindInstalls([games], maxDepth: 8);
        Assert.Contains(client, found);
        Assert.DoesNotContain(found, p => p.Contains("dosdevices", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(found, p => p.Contains("locked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Probe_checks_program_files_layout_without_a_deep_walk()
    {
        var root = Temp();
        var client = Path.Combine(root, "World of Warcraft", AppConstants.ClientSubdir);
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        File.WriteAllBytes(Path.Combine(client, "WowClassic.exe"), []);
        var found = InstallFinder.Probe([root]);
        Assert.Equal([client], found);
    }

    [Fact]
    public void Steam_and_battlenet_hints_extract_install_paths()
    {
        var vdf = """
            "libraryfolders"
            {
                "0" { "path" "C:\\Program Files (x86)\\Steam" }
                "1" { "path" "D:\\SteamLibrary" }
            }
            """;
        var libs = PathHints.SteamLibraries(vdf).ToArray();
        Assert.Contains(@"C:\Program Files (x86)\Steam", libs);
        Assert.Contains(@"D:\SteamLibrary", libs);

        var config = """{"Path":"C:\\\\Program Files\\\\World of Warcraft","Other":"C:\\\\Games\\\\NotWow"}""";
        var paths = PathHints.BattleNetPaths(config).ToArray();
        Assert.Contains(@"C:\Program Files\World of Warcraft", paths);
        Assert.DoesNotContain(paths, p => p.Contains("NotWow", StringComparison.Ordinal));
    }

    [Fact]
    public void Zip_install_extracts_the_addon_and_rejects_traversal()
    {
        var game = Temp();
        using var mem = new MemoryStream();
        using (var zip = new ZipArchive(mem, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "Guild_Sync/GuildSync/GuildSync.toc", "## Version: 0.3.5\n");
            Write(zip, "Guild_Sync/GuildSync/Core.lua", "-- core\n");
            Write(zip, "Guild_Sync/GuildSync/../../evil.txt", "nope\n");
        }

        var result = AddonInstaller.InstallFromZip(game, mem.ToArray());
        Assert.True(result.Ok);
        var addon = Path.Combine(game, "Interface", "AddOns", "GuildSync");
        Assert.Equal("0.3.5", TocVersion.ReadLocal(game));
        Assert.True(File.Exists(Path.Combine(addon, "Core.lua")));
        Assert.False(File.Exists(Path.Combine(game, "evil.txt")));
        Assert.False(File.Exists(Path.Combine(game, "Interface", "evil.txt")));
    }

    [Fact]
    public void Companion_stamp_uses_the_app_version()
    {
        var game = Temp();
        var addon = Path.Combine(game, "Interface", "AddOns", "GuildSync");
        Directory.CreateDirectory(addon);
        Assert.True(CompanionStamp.Write(game));
        var text = File.ReadAllText(Path.Combine(addon, "Companion.lua"));
        Assert.Contains($"GuildSync.CompanionVersion = \"{AppConstants.Version}\"", text);
    }

    [Fact]
    public void Config_roundtrip_keeps_the_token_private()
    {
        var path = Path.Combine(Temp(), "config.json");
        var store = new ConfigStore(path);
        store.Data.Token = "upload-token";
        store.Data.GameDir = @"C:\Games\Wow\_classic_beta_";
        store.Data.Notify = "always";
        store.MarkSynced();
        store.Save();

        var loaded = new ConfigStore(path);
        Assert.Equal("upload-token", loaded.Data.Token);
        Assert.Equal("always", loaded.Data.Notify);
        Assert.True(loaded.Data.StartWithWindows);
        Assert.NotNull(loaded.Data.LastSynced(store.Data.Synced.Keys.First()));
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }

    [Fact]
    public void Process_names_match_the_game()
    {
        Assert.True(ProcessNames.IsWowClient("Wow"));
        Assert.True(ProcessNames.IsWowClient("WowClassic"));
        Assert.True(ProcessNames.IsWowClient("Wow.exe"));
        Assert.True(ProcessNames.IsWowClient("World of Warcraft"));
        Assert.True(ProcessNames.IsWowClient("World of Warcraft Classic"));
        Assert.True(ProcessNames.IsWowClient("World of Warcra"));
        Assert.False(ProcessNames.IsWowClient("World of Warcraft Helper"));
        Assert.False(ProcessNames.IsWowClient("WowVoiceProxy"));
        Assert.False(ProcessNames.IsWowClient("WowError"));
        Assert.False(ProcessNames.IsWowClient("chrome"));
    }

    [Fact]
    public async Task Upload_body_and_token_check_follow_the_server_contract()
    {
        HttpRequestMessage? seen = null;
        byte[]? bodyBytes = null;
        string? contentType = null;
        var handler = new ScriptedHandler(request =>
        {
            seen = request;
            if (request.Content is not null)
            {
                bodyBytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                contentType = request.Content.Headers.ContentType?.ToString();
            }
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{"display_name":"Tavern Test"}""");
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new IngestClient(handler);
        var check = await client.CheckTokenAsync("tok");
        Assert.Equal("ok", check.Status);
        Assert.Equal("Tavern Test", check.Name);
        Assert.Equal("GuildSync-Companion/" + AppConstants.Version, seen!.Headers.UserAgent.ToString());
        Assert.Equal("tok", seen.Headers.GetValues("X-Upload-Token").Single());

        var dir = Temp();
        var file = Path.Combine(dir, "GuildSync.lua");
        File.WriteAllText(file, "GuildSyncDB = {}\n");
        var upload = await client.UploadAsync("tok", file);
        Assert.True(upload.Ok);
        var body = Encoding.UTF8.GetString(bodyBytes!);
        Assert.Contains("name=\"file\"; filename=\"GuildSync.lua\"", body);
        Assert.Contains("GuildSyncDB = {}", body);
        Assert.StartsWith("multipart/form-data; boundary=", contentType);
    }

    [Fact]
    public async Task Rejected_token_and_unverified_endpoint_match_linux()
    {
        var client = new IngestClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var rejected = await client.CheckTokenAsync("nope");
        Assert.Equal("invalid", rejected.Status);

        client = new IngestClient(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var missing = await client.CheckTokenAsync("maybe");
        Assert.Equal("ok", missing.Status);
        Assert.Equal("unverified", missing.Detail);
    }

    [Fact]
    public async Task Host_uploads_once_debounced_and_respects_cooldown()
    {
        var root = Temp();
        var client = Path.Combine(root, AppConstants.ClientSubdir);
        var saved = Path.Combine(client, "WTF", "Account", "TEST", "SavedVariables");
        Directory.CreateDirectory(saved);
        Directory.CreateDirectory(Path.Combine(client, "Interface"));
        File.WriteAllBytes(Path.Combine(client, "Wow.exe"), []);
        var lua = Path.Combine(saved, "GuildSync.lua");
        File.WriteAllText(lua, "GuildSyncDB = { a = 1 }\n");

        var uploads = 0;
        var handler = new ScriptedHandler(_ =>
        {
            Interlocked.Increment(ref uploads);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var store = new ConfigStore(Path.Combine(Temp(), "config.json"));
        store.Data.Token = "tok";
        store.Data.GameDir = client;
        store.Data.Notify = "always";
        store.Save();

        var host = new CompanionHost(
            store,
            new IngestClient(handler),
            new AddonSource(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            new FakeGame(running: true),
            debounce: TimeSpan.FromMilliseconds(40),
            heartbeat: TimeSpan.FromHours(1),
            addonEvery: TimeSpan.FromHours(1),
            initialAddon: TimeSpan.FromHours(1));

        var message = await host.SyncAsync(force: false, toast: true);
        Assert.Contains("Synced 1", message);
        Assert.Equal(1, uploads);

        File.WriteAllText(lua, "GuildSyncDB = { a = 2 }\n");
        host.OnFileEvent();
        await Task.Delay(200);
        Assert.Contains("cooldown", host.LastMessage);
        Assert.Equal(1, uploads);

        await Task.Delay(400);
        Assert.Equal(1, uploads);
    }

    [Fact]
    public async Task Host_syncs_a_final_time_when_the_game_closes()
    {
        var root = Temp();
        var client = Path.Combine(root, AppConstants.ClientSubdir);
        var saved = Path.Combine(client, "WTF", "Account", "TEST", "SavedVariables");
        Directory.CreateDirectory(saved);
        var lua = Path.Combine(saved, "GuildSync.lua");
        File.WriteAllText(lua, "GuildSyncDB = { a = 1 }\n");

        var uploads = 0;
        var game = new FakeGame(running: true);
        var store = new ConfigStore(Path.Combine(Temp(), "config.json"));
        store.Data.Token = "tok";
        store.Data.GameDir = client;
        store.Save();
        var host = new CompanionHost(
            store,
            new IngestClient(new ScriptedHandler(_ =>
            {
                Interlocked.Increment(ref uploads);
                return new HttpResponseMessage(HttpStatusCode.OK);
            })),
            new AddonSource(new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))),
            game,
            debounce: TimeSpan.FromHours(1),
            heartbeat: TimeSpan.FromHours(1),
            addonEvery: TimeSpan.FromHours(1),
            initialAddon: TimeSpan.FromHours(1));

        host.SampleProcess(syncOnExit: true);
        Assert.Equal(0, uploads);
        game.Running = false;
        host.SampleProcess(syncOnExit: true);
        await Task.Delay(100);
        Assert.Equal(1, uploads);
        Assert.False(host.GameRunning);
    }

    private static void Write(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes);
    }

    private static string Temp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gsc-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json)
    {
        return new HttpResponseMessage(code)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handle(request));
    }

    [Fact]
    public async Task SetMainCharacter_sends_the_id_and_parses_the_accepted_main()
    {
        int? received = null;
        var handler = new ScriptedHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/api/v1/ingest/main-character", request.RequestUri!.AbsolutePath);
            Assert.Equal("tok", request.Headers.GetValues("X-Upload-Token").Single());
            received = System.Text.Json.JsonDocument
                .Parse(request.Content!.ReadAsStringAsync().Result)
                .RootElement.GetProperty("character_id").GetInt32();
            return Json(HttpStatusCode.OK, """{"main_character_id": 42}""");
        });
        using var client = new IngestClient(handler);
        var (mainId, error) = await client.SetMainCharacterAsync("tok", 42);
        Assert.Equal(42, received);
        Assert.Equal(42, mainId);
        Assert.Equal("", error);
    }

    [Fact]
    public async Task SetMainCharacter_surfaces_rejections()
    {
        using var client = new IngestClient(new ScriptedHandler(_ =>
            Json(HttpStatusCode.Forbidden, """{"detail": "not your character"}""")));
        var (mainId, error) = await client.SetMainCharacterAsync("tok", 99);
        Assert.Null(mainId);
        Assert.Equal("token rejected", error);
    }

    [Fact]
    public void Roster_sorts_highest_level_first_and_joins_the_name()
    {
        const string json = """
            {"characters":[
              {"name":"Rukh","surname":"Jadefire","class":"Rogue","spec":"Combat","race":"Orc","level":36,"ilvl":39.4,"last_seen":100},
              {"name":"Aldric","surname":"Hollowbrook","class":"Paladin","spec":"Retribution","race":"Dwarf","level":56,"ilvl":49.7,"last_seen":200},
              {"name":"","surname":"","class":"Mage","level":10},
              {"name":"Fifth","surname":"Dreadclaw","class":"Druid","spec":"Druid","race":"High Order Skyborne","level":3,"ilvl":3.6,"last_seen":300}
            ]}
            """;
        var rows = RosterList.Parse(json);
        Assert.Equal(["Aldric Hollowbrook", "Rukh Jadefire", "Fifth Dreadclaw"], rows.Select(row => row.Name).ToArray());
        Assert.Equal("49.7", RosterList.FormatItemLevel(49.7));
        Assert.Equal("2d ago", RosterList.Ago(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 2 * 86400 - 30, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Roster_keeps_one_players_characters_and_pins_the_main()
    {
        const string json = """
            {"characters":[
              {"id":1562,"name":"Rukh","surname":"Jadefire","player":"Ramzal","class":"Rogue","level":36,"ilvl":39.4,"last_seen":100},
              {"id":1560,"name":"Aldric","surname":"Hollowbrook","player":"Test Player 6","class":"Paladin","level":56,"ilvl":49.7,"last_seen":200},
              {"id":1565,"name":"Ramzal","surname":"Bamzal","player":"ramzal","class":"Mage","level":3,"ilvl":2.3,"last_seen":300},
              {"id":1447,"name":"Fifth","surname":"Dreadclaw","player":"Fifthdread","class":"Druid","level":3,"ilvl":3.6,"last_seen":300}
            ]}
            """;
        var mine = RosterList.ForPlayer(RosterList.Parse(json), " Ramzal ");
        Assert.Equal(["Rukh Jadefire", "Ramzal Bamzal"], mine.Select(row => row.Name).ToArray());
        Assert.Empty(RosterList.ForPlayer(mine, ""));
        Assert.Empty(RosterList.ForPlayer(mine, "Fifthdread"));

        var pinned = RosterList.Order(mine, 1565);
        Assert.Equal(["Ramzal Bamzal", "Rukh Jadefire"], pinned.Select(row => row.Name).ToArray());
        Assert.Equal(["Rukh Jadefire", "Ramzal Bamzal"], RosterList.Order(mine, null).Select(row => row.Name).ToArray());
    }

    [Fact]
    public void Dkp_reads_the_character_page_span()
    {
        Assert.Equal(0, DkpRead.FromHtml("""<span class="ff-stat-num ff-num ff-dkp-pos">0</span>"""));
        Assert.Equal(12, DkpRead.FromHtml("""<span class="ff-dkp-pos">12</span>"""));
        Assert.Equal(-4, DkpRead.FromHtml("""<span class="ff-stat-num ff-num ff-dkp-neg">4</span>"""));
        Assert.Equal(-4, DkpRead.FromHtml("""<span class="ff-dkp-neg">-4</span>"""));
        Assert.Equal(1200, DkpRead.FromHtml("""<span class="ff-dkp-pos">1,200</span>"""));
        Assert.Null(DkpRead.FromHtml("""<span class="ff-stat-num ff-num">0</span>"""));
        Assert.Null(DkpRead.FromHtml(""));
        Assert.Equal("—", RosterList.FormatDkp(null));
        Assert.Equal("-4", RosterList.FormatDkp(-4));
    }

    [Fact]
    public void Config_remembers_the_pinned_main()
    {
        var path = Path.Combine(Temp(), "config.json");
        var store = new ConfigStore(path);
        store.Data.MainCharacterId = 1565;
        store.Save();
        Assert.Equal(1565, new ConfigStore(path).Data.MainCharacterId);

        store.Data.MainCharacterId = 0;
        store.Data.Normalize();
        Assert.Null(store.Data.MainCharacterId);
    }

    [Fact]
    public void App_update_picks_the_newer_setup_on_our_forgejo()
    {
        const string json = """
            [
              {"tag_name":"v0.1.0","draft":false,"prerelease":false,"assets":[
                {"name":"GuildSyncCompanion-Setup.exe","browser_download_url":"https://forgejo.fifthdread.com/Ramzal/guildsync-companion/releases/download/v0.1.0/GuildSyncCompanion-Setup.exe"}
              ]},
              {"tag_name":"v0.2.0","draft":false,"prerelease":false,"assets":[
                {"name":"GuildSyncCompanion-Setup.exe","browser_download_url":"https://evil.example/GuildSyncCompanion-Setup.exe"}
              ]},
              {"tag_name":"v0.3.0","draft":false,"prerelease":true,"assets":[
                {"name":"GuildSyncCompanion-Setup.exe","browser_download_url":"https://forgejo.fifthdread.com/Ramzal/guildsync-companion/releases/download/v0.3.0/GuildSyncCompanion-Setup.exe"}
              ]},
              {"tag_name":"v0.1.4","draft":false,"prerelease":false,"assets":[
                {"name":"GuildSyncCompanion-Setup.exe","browser_download_url":"https://forgejo.fifthdread.com/Ramzal/guildsync-companion/releases/download/v0.1.4/GuildSyncCompanion-Setup.exe"}
              ]}
            ]
            """;

        var chosen = AppReleases.Choose(json, "0.1.0", "https://forgejo.fifthdread.com");
        Assert.NotNull(chosen);
        Assert.Equal("0.1.4", chosen.Version);
        Assert.EndsWith("/v0.1.4/GuildSyncCompanion-Setup.exe", chosen.DownloadUrl);
        Assert.Null(AppReleases.Choose(json, "0.1.4", "https://forgejo.fifthdread.com"));
        Assert.False(AppReleases.IsTrustedSetupUrl("https://evil.example/GuildSyncCompanion-Setup.exe", "https://forgejo.fifthdread.com"));
        Assert.False(VersionOrder.IsNewer("0.1.0", "0.1.0"));
        Assert.True(VersionOrder.IsNewer("v0.2", "0.1.9"));

        const string both = """
            [
              {"tag_name":"v0.1.6","draft":false,"prerelease":false,"assets":[
                {"name":"GuildSyncCompanion-Setup.exe","browser_download_url":"https://forgejo.fifthdread.com/Ramzal/guildsync-companion/releases/download/v0.1.6/GuildSyncCompanion-Setup.exe"},
                {"name":"GuildSyncCompanion-Setup.run","browser_download_url":"https://forgejo.fifthdread.com/Ramzal/guildsync-companion/releases/download/v0.1.6/GuildSyncCompanion-Setup.run"}
              ]}
            ]
            """;
        var linux = AppReleases.Choose(both, "0.1.5", "https://forgejo.fifthdread.com", AppConstants.LinuxSetupAsset);
        Assert.NotNull(linux);
        Assert.EndsWith("/GuildSyncCompanion-Setup.run", linux.DownloadUrl);
        Assert.Null(AppReleases.Choose(both, "0.1.5", "https://forgejo.fifthdread.com", AppConstants.MacSetupAsset));
        Assert.Equal("0.1.6", AppReleases.Newest(both, "0.1.5"));
        Assert.Null(AppReleases.Newest(both, "0.1.6"));
    }

    [Fact]
    public void Unix_search_keeps_mac_on_the_native_client()
    {
        var linux = UnixSearchRoots.Candidates("/home/tavern", mac: false);
        Assert.Contains("/home/tavern/.wine/drive_c", linux);
        Assert.Contains("/home/tavern/.local/share/lutris", linux);
        Assert.Contains("/home/tavern/.steam/steam/steamapps/compatdata", linux);
        Assert.Contains("/media", linux);

        var mac = UnixSearchRoots.Candidates("/Users/tavern", mac: true);
        Assert.Contains("/Applications", mac);
        Assert.Contains("/Users/tavern/Applications", mac);
        Assert.DoesNotContain("/Users/tavern/.wine/drive_c", mac);
        Assert.DoesNotContain("/media", mac);
    }

    [Fact]
    public void Mac_client_is_the_folder_beside_the_app()
    {
        var root = Temp();
        var client = Path.Combine(root, "_classic_beta_");
        Directory.CreateDirectory(Path.Combine(client, "Interface"));
        Directory.CreateDirectory(Path.Combine(client, "WTF"));
        Directory.CreateDirectory(Path.Combine(root, "World of Warcraft.app"));

        Assert.Equal(client, InstallFinder.Normalize(root));
        Assert.Equal(client, InstallFinder.Normalize(client));

        var fresh = Temp();
        var product = Path.Combine(fresh, "_classic_beta_");
        Directory.CreateDirectory(Path.Combine(product, "World of Warcraft.app"));
        Assert.Equal(product, InstallFinder.Normalize(fresh));

        var classic = Temp();
        var beta = Path.Combine(classic, "_classic_beta_");
        Directory.CreateDirectory(Path.Combine(beta, "Interface"));
        Directory.CreateDirectory(Path.Combine(beta, "World of Warcraft Classic.app"));
        Assert.Equal(beta, InstallFinder.Normalize(classic));
    }

    private sealed class FakeGame(bool running) : IGameProcess
    {
        public bool Running { get; set; } = running;
        public bool IsRunning() => Running;
    }
}

file static class ConfigStoreExtensions
{
    public static void MarkSynced(this ConfigStore store)
    {
        store.Data.MarkSynced("/tmp/GuildSync.lua", new SavedVarsState(true, true, true, 1, 2, 3));
    }
}
