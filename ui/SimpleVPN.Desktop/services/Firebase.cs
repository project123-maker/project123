// File: services/Firebase.cs
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
    public sealed class FirebaseClient
    {
        private readonly string _projectId;
        private readonly string _apiKey;
        private string? _idToken;
        private string? _uid;

        private static readonly HttpClient Http = new HttpClient();

        public FirebaseClient(string projectId, string apiKey)
        {
            _projectId = projectId;
            _apiKey = apiKey;
        }

        public string Uid => _uid ?? throw new InvalidOperationException("Not authed");

        // ---- Auth (anonymous) ----
        public async Task SignInAnonymouslyAsync(CancellationToken ct = default)
        {
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}";
            var body = new { returnSecureToken = true };
            var res = await PostJsonAsync(url, body, ct);
            _idToken = res.GetProperty("idToken").GetString();
            _uid = res.GetProperty("localId").GetString();
        }

        // ---- Firestore REST v1 ----
        private string FsBase => $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";

        public async Task<JsonElement?> GetDocAsync(string path, CancellationToken ct = default)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, $"{FsBase}/{TrimSlash(path)}");
            AttachAuth(req);
            var rsp = await Http.SendAsync(req, ct);
            if (rsp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            rsp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.Clone();
        }

        // Set/merge whole document (serverTimestamp etc. not needed on client here)
        public async Task SetDocAsync(string path, IDictionary<string, object> fields, CancellationToken ct = default)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, $"{FsBase}/{TrimSlash(path)}");
            AttachAuth(req);
            req.Content = new StringContent(ToFsDoc(fields), Encoding.UTF8, "application/json");
            var rsp = await Http.SendAsync(req, ct);
            rsp.EnsureSuccessStatusCode();
        }

        public async Task<JsonElement> RunQueryEqAsync(string collectionPath, string field, string value, CancellationToken ct = default)
        {
            var url = $"{FsBase}/{TrimSlash(collectionPath)}:runQuery";
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
            var rsp = await Http.SendAsync(req, ct);
            rsp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync(ct));
            return doc.RootElement.Clone();
        }

        // ---- Helpers ----
        private static string TrimSlash(string p) => p.TrimStart('/');

        private void AttachAuth(HttpRequestMessage req)
        {
            if (!string.IsNullOrEmpty(_idToken))
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _idToken);
        }

        private static async Task<JsonElement> PostJsonAsync(string url, object body, CancellationToken ct)
        {
            var rsp = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
            var txt = await rsp.Content.ReadAsStringAsync(ct);
            rsp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(txt);
            return doc.RootElement.Clone();
        }

        // Convert dictionary to Firestore doc JSON { fields: ... }
        private static string ToFsDoc(IDictionary<string, object> map)
        {
            return JsonSerializer.Serialize(new { fields = Map(map) });
        }

        // Build Firestore "fields" mapValue recursively
        public static object Map(IDictionary<string, object> map)
        {
            return map.ToDictionary(kv => kv.Key, kv => ToFsValue(kv.Value));
        }

        public static string? GetString(JsonElement document, string field)
        {
            if (!document.TryGetProperty("fields", out var fields)) return null;
            if (!fields.TryGetProperty(field, out var f)) return null;
            return f.TryGetProperty("stringValue", out var s) ? s.GetString() : null;
        }

        public static bool? GetBool(JsonElement document, string field)
        {
            if (!document.TryGetProperty("fields", out var fields)) return null;
            if (!fields.TryGetProperty(field, out var f)) return null;
            return f.TryGetProperty("booleanValue", out var b) ? b.GetBoolean() : (bool?)null;
        }

        public static JsonElement? GetMap(JsonElement document, string field)
        {
            if (!document.TryGetProperty("fields", out var fields)) return null;
            if (!fields.TryGetProperty(field, out var f)) return null;
            return f.TryGetProperty("mapValue", out var m) ? m.GetProperty("fields") : (JsonElement?)null;
        }

        // Stable device id (MachineGuid + User SID)
        public static string DeviceId()
        {
            string mg = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", ""
            )?.ToString() ?? "";
            string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(mg + "|" + sid));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static object ToFsValue(object? v)
        {
            if (v == null) return new { nullValue = (string?)null };
            switch (v)
            {
                case string s: return new { stringValue = s };
                case bool b: return new { booleanValue = b };
                case int i: return new { integerValue = i.ToString() };
                case long l: return new { integerValue = l.ToString() };
                case double d: return new { doubleValue = d };
                case DateTime dt: return new { timestampValue = dt.ToUniversalTime().ToString("o") };
                case IDictionary<string, object> map:
                    return new { mapValue = new { fields = Map(map) } };
                default:
                    return new { stringValue = v.ToString() };
            }
        }
    }
}
