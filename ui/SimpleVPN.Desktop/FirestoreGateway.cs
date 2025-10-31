using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop
{
    public sealed class FirestoreGateway
    {
        readonly FirestoreDb _db;
        readonly string _deviceId;
        readonly string _sbDir;
        readonly string _sbCfg;
        readonly string _lastVless;
        const int LOCK_TTL_SEC = 120;

        public FirestoreGateway()
        {
            _deviceId = DeviceId.LoadOrCreate();
            var root = AppContext.BaseDirectory;
            _sbDir = Path.Combine(root, "sing-box");
            Directory.CreateDirectory(_sbDir);
            _sbCfg = Path.Combine(_sbDir, "config.json");
            _lastVless = Path.Combine(_sbDir, "last.vless.txt");
            _db = CreateDbOrNull(); // null => offline (gateway/local.json)
        }

        public async Task<(bool ok, string message, string redeemId)> RedeemAsync(string code, CancellationToken ct)
        {
            try { _ = await GetVlessForCodeOrDefault(code, ct); return (true, "ok", string.IsNullOrWhiteSpace(code) ? "default" : code); }
            catch (Exception e) { return (false, e.Message, null); }
        }

        public async Task<(bool ok, string message)> RefreshAsync(string redeemId, CancellationToken ct)
        {
            try { var vless = await GetVlessForCodeOrDefault(redeemId, ct); await WriteConfigJson(vless); return (true, "refreshed"); }
            catch (Exception e) { return (false, e.Message); }
        }

        public async Task<(bool ok, string message)> ConnectAsync(string redeemId, CancellationToken ct)
        {
            try
            {
                if (!await CheckAndTakeLock(redeemId, ct)) return (false, "Single-device lock");
                var vless = await GetVlessForCodeOrDefault(redeemId, ct);
                await WriteConfigJson(vless);
                return (true, "config ready");
            }
            catch (Exception e) { return (false, e.Message); }
        }

        public Task<bool> DisconnectAsync(CancellationToken _) => Task.FromResult(true);

        public async Task<bool> HeartbeatAsync(string redeemId, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(redeemId) || _db is null) return true;
                var ok = await CheckAndTakeLock(redeemId, ct);
                if (ok) await BumpHeartbeat(redeemId, ct);
                return ok;
            }
            catch { return string.IsNullOrWhiteSpace(redeemId); }
        }

        DocumentReference CodeRef(string code) => _db.Collection("codes").Document(code);
        DocumentReference ConfigRefFromPath(string pathStr)
        {
            var parts = pathStr.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return _db.Collection("configs").Document("current");
            var col = _db.Collection(parts[0]);
            int i = 1;
            while (i < parts.Length - 1)
            {
                var docId = parts[i++];
                if (i >= parts.Length) return col.Document(docId);
                var subCol = parts[i++];
                col = col.Document(docId).Collection(subCol);
            }
            return col.Document(parts[^1]);
        }

        async Task<string> GetVlessForCodeOrDefault(string code, CancellationToken ct)
        {
            if (_db is not null)
            {
                if (!string.IsNullOrWhiteSpace(code))
                {
                    var cs = await CodeRef(code).GetSnapshotAsync(ct);
                    if (!cs.Exists) throw new Exception("Code not found");
                    var vpath = cs.ContainsField("vlessPath") ? cs.GetValue<string>("vlessPath") : "configs/current";
                    var cfgDoc = await ConfigRefFromPath(vpath).GetSnapshotAsync(ct);
                    if (!cfgDoc.Exists) throw new Exception("Config missing");
                    var vless = cfgDoc.GetValue<string>("vless");
                    ValidateVless(vless); return vless;
                }
                var current = await _db.Collection("configs").Document("current").GetSnapshotAsync(ct);
                if (current.Exists)
                {
                    var vless = current.GetValue<string>("vless");
                    ValidateVless(vless); return vless;
                }
            }
            // offline fallback
            var localPath = Path.Combine(AppContext.BaseDirectory, "gateway", "local.json");
            var text = await File.ReadAllTextAsync(localPath, ct);
            var obj = JsonSerializer.Deserialize<JsonElement>(text);
            var fallback = obj.TryGetProperty("vless", out var v) ? v.GetString() : null;
            ValidateVless(fallback);
            return fallback!;
        }

        static void ValidateVless(string v)
        {
            if (string.IsNullOrWhiteSpace(v) || !v.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Invalid VLESS");
        }

        async Task<bool> CheckAndTakeLock(string code, CancellationToken ct)
        {
            if (_db is null || string.IsNullOrWhiteSpace(code)) return true;
            var cref = CodeRef(code);

            return await _db.RunTransactionAsync<bool>(async tr =>
            {
                var snap = await tr.GetSnapshotAsync(cref);

                // safe reads (null-friendly)
                string lockedBy = null;
                try { if (snap.ContainsField("lock.lockedBy")) lockedBy = snap.GetValue<string>("lock.lockedBy"); } catch { lockedBy = null; }

                Timestamp lockAt = default;
                try { if (snap.ContainsField("lock.lockAt")) lockAt = snap.GetValue<Timestamp>("lock.lockAt"); } catch { lockAt = default; }

                bool stale = IsStale(lockAt);
                bool same = lockedBy == _deviceId;

                if (same || stale)
                {
                    tr.Update(cref, new Dictionary<string, object>
                    {
                        ["lock.lockedBy"] = _deviceId,
                        ["lock.platform"] = "desktop",
                        ["lock.lockAt"]   = FieldValue.ServerTimestamp
                    });
                    return true;
                }
                return false;
            }, cancellationToken: ct);
        }

        async Task BumpHeartbeat(string code, CancellationToken ct)
        {
            if (_db is null || string.IsNullOrWhiteSpace(code)) return;
            try
            {
                await CodeRef(code).UpdateAsync(new Dictionary<string, object>
                {
                    ["lock.lockedBy"] = _deviceId,
                    ["lock.platform"] = "desktop",
                    ["lock.lockAt"]   = FieldValue.ServerTimestamp
                }, cancellationToken: ct);
            }
            catch { /* ignore */ }
        }

        static bool IsStale(Timestamp ts)
        {
            if (ts == default) return true;
            var age = DateTime.UtcNow - ts.ToDateTime();
            return age.TotalSeconds > LOCK_TTL_SEC;
        }

        async Task WriteConfigJson(string vless)
        {
            var cfg = SingboxConfig.FromVless(vless);
            var json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_sbCfg, json);
            await File.WriteAllTextAsync(_lastVless, vless);
        }

        static FirestoreDb CreateDbOrNull()
        {
            try
            {
                var pk   = Environment.GetEnvironmentVariable("FIREBASE_PRIVATE_KEY");
                var pid  = Environment.GetEnvironmentVariable("FIREBASE_PROJECT_ID");
                var mail = Environment.GetEnvironmentVariable("FIREBASE_CLIENT_EMAIL");
                if (!string.IsNullOrWhiteSpace(pk) && !string.IsNullOrWhiteSpace(pid) && !string.IsNullOrWhiteSpace(mail))
                {
                    var cred = GoogleCredential.FromJson($@"{{
                      ""type"": ""service_account"",
                      ""project_id"": ""{pid}"",
                      ""private_key_id"": ""env"",
                      ""private_key"": ""{pk.Replace("\\n", "\n")}"",
                      ""client_email"": ""{mail}"",
                      ""client_id"": ""env"",
                      ""token_uri"": ""https://oauth2.googleapis.com/token""
                    }}");
                    return new FirestoreDbBuilder { ProjectId = pid, Credential = cred }.Build();
                }
                var baseDir = AppContext.BaseDirectory;
                var fileA = Path.Combine(baseDir, "serviceAccount.json");
                var fileB = Path.Combine(baseDir, "service-account.json");
                var file  = File.Exists(fileA) ? fileA : (File.Exists(fileB) ? fileB : null);
                if (file != null)
                {
                    var cred = GoogleCredential.FromFile(file);
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var projectId = doc.RootElement.GetProperty("project_id").GetString();
                    return new FirestoreDbBuilder { ProjectId = projectId, Credential = cred }.Build();
                }
                return null;
            }
            catch { return null; }
        }
    }

    // VLESS -> sing-box config (unchanged)
    static class SingboxConfig
    {
        public static object FromVless(string vlessUrl)
        {
            var u = new Uri(vlessUrl);
            if (!string.Equals(u.Scheme, "vless", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Bad scheme");

            var uuid = Uri.UnescapeDataString(u.UserInfo);
            var host = u.Host;
            var port = u.IsDefaultPort ? 443 : u.Port;
            var q = ParseQuery(u.Query);
            string sni  = q.TryGetValue("sni", out var s) ? s : (q.TryGetValue("host", out var h) ? h : host);
            string fp   = q.TryGetValue("fp", out var f) ? f : "chrome";
            string alpn = q.TryGetValue("alpn", out var a) ? a : "h2,http/1.1";
            string path = string.IsNullOrEmpty(u.AbsolutePath) ? "/" : u.AbsolutePath;

            return new
            {
                log = new { level = "info", timestamp = true },
                dns = new
                {
                    servers = new object[]
                    {
                        new { tag = "doh1", address = "https://cloudflare-dns.com/dns-query", detour = "direct" },
                        new { tag = "doh2", address = "https://dns.google/dns-query", detour = "direct" }
                    },
                    rules = new object[] { new { outbound = "any", server = "doh1" } },
                    strategy = "ipv4_only"
                },
                inbounds = new object[]
                {
                    new { type="tun", tag="tun-in", mtu=1400, strict_route=true, auto_route=true, sniff=true }
                },
                outbounds = new object[]
                {
                    new {
                        tag = "vless-out", type = "vless",
                        server = host, server_port = port, uuid = uuid, flow = "",
                        packet_encoding = "xudp",
                        tls = new {
                            enabled = true, server_name = sni, insecure = false,
                            utls = new { enabled = true, fingerprint = fp },
                            alpn = alpn.Split(',')
                        },
                        transport = new { type = "ws", path, headers = new { Host = sni } }
                    },
                    new { tag = "direct", type = "direct" },
                    new { tag = "block", type = "block" }
                },
                route = new
                {
                    rules = new object[]
                    {
                        new { dns = true, outbound = "vless-out" },
                        new { ip_is_private = true, outbound = "direct" }
                    },
                    final = "vless-out",
                    auto_detect_interface = true
                }
            };
        }

        static Dictionary<string,string> ParseQuery(string query)
        {
            var dict = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            var q = query[0] == '?' ? query.Substring(1) : query;
            foreach (var pair in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                var k = Uri.UnescapeDataString(kv[0]);
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
                dict[k] = v;
            }
            return dict;
        }
    }
}

