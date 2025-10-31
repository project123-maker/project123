// ui/SimpleVPN.Desktop/services/Firebase.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Services
{
    // Minimal REST client for Firebase Auth (anonymous) + Firestore
    public sealed class Firebase
    {
        private readonly string _projectId;
        private readonly string _apiKey;
        private string? _idToken;
        private string? _uid;

        private static readonly HttpClient Http = new HttpClient();

        public Firebase(string projectId, string apiKey)
        {
            _projectId = projectId;
            _apiKey = apiKey;
        }

        public string Uid => _uid ?? throw new InvalidOperationException("Not authed");
        private string FsBase => $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";

        public async Task SignInAnonymouslyAsync(CancellationToken ct = default)
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}";
            var body = new { returnSecureToken = true };
            var rsp = await PostJsonAsync(url, body, ct);
            _idToken = rsp.GetProperty("idToken").GetString();
            _uid = rsp.GetProperty("localId").GetString();
        }

        // ---- Firestore: get/patch/query ----
        public async Task<JsonElement?> GetDocAsync(string path, CancellationToken ct = default)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{FsBase}/{path}");
            AttachAuth(req);
            var res = await Http.SendAsync(req, ct);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.Clone();
        }

        public async Task PatchDocAsync(string path, IDictionary<string, object> fields, IEnumerable<string>? updateMask = null, CancellationToken ct = default)
        {
            var mask = updateMask is null ? "" : $"?updateMask.fieldPaths={string.Join("&updateMask.fieldPaths=", updateMask)}";
            var req = new HttpRequestMessage(HttpMethod.Patch, $"{FsBase}/{path}{mask}");
            AttachAuth(req);
            req.Content = new StringContent(ToFsDoc(fields), Encoding.UTF8, "application/json");
            var res = await Http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
        }

        public async Task<JsonElement> RunQueryAsync(string collectionPath, string field, string value, CancellationToken ct = default)
        {
            var url = $"{FsBase}/{collectionPath}:runQuery";
            var body = new
            {
                structuredQuery = new
                {
                    from = new[] { new { collectionId = collectionPath.Split('/').Last() } },
                    where = new
                    {
                        fieldFilter = new
                        {
                            field = new { fieldPath = field },
                            op = "EQUAL",
                            value = new { stringValue = value }
                        }
                    },
                    limit = 1
                }
            };
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            AttachAuth(req);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var res = await Http.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return doc.RootElement.Clone();
        }

        private void AttachAuth(HttpRequestMessage req)
        {
            if (!string.IsNullOrEmpty(_idToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _idToken);
        }

        // ---------- helpers ----------
        public static string DeviceId()
        {
            string mg = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString() ?? "";
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(mg + "|" + sid));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static async Task<JsonElement> PostJsonAsync(string url, object body, CancellationToken ct)
        {
            var rsp = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
            var txt = await rsp.Content.ReadAsStringAsync(ct);
            rsp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(txt);
            return doc.RootElement.Clone();
        }

        // Convert Dictionary<string, object> to Firestore {fields:{...}} JSON
        private static string ToFsDoc(IDictionary<string, object> src)
        {
            var fields = new Dictionary<string, object>();
            foreach (var (k, v) in src)
                fields[k] = ToFsValue(v);

            return JsonSerializer.Serialize(new { fields });

            static object ToFsValue(object? v)
            {
                if (v is null) return new { nullValue = (string?)null };
                return v switch
                {
                    string s => new { stringValue = s },
                    bool b => new { booleanValue = b },
                    int i => new { integerValue = i.ToString() },
                    long l => new { integerValue = l.ToString() },
                    double d => new { doubleValue = d },
                    DateTime dt => new { timestampValue = dt.ToUniversalTime().ToString("o") },
                    IDictionary<string, object> map => new { mapValue = new { fields = map.ToDictionary(kv => kv.Key, kv => ToFsValue(kv.Value)) } },
                    _ => new { stringValue = v.ToString() }
                };
            }
        }

        // Readers
        public static string? GetString(JsonElement doc, string field)
        {
            if (!doc.TryGetProperty("fields", out var f)) return null;
            return f.TryGetProperty(field, out var v) && v.TryGetProperty("stringValue", out var s) ? s.GetString() : null;
        }
    }
}
