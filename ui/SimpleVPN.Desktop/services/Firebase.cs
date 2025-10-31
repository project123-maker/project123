using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Services
{
    /// <summary>Minimal Firebase: anonymous auth + Firestore GET/PATCH via REST.</summary>
    public sealed class Firebase
    {
        // TODO: set these:
        private const string ApiKey    = "AIzaSyCV3CSoZ7V8uSGRuFwY82FJ4fnC66Ky5jc";
        private const string ProjectId = "simplevpn-3272d";

        private readonly HttpClient _http = new HttpClient();
        private string? _idToken;

        public async Task EnsureAnonAuthAsync()
        {
            if (!string.IsNullOrEmpty(_idToken)) return;
            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={ApiKey}";
            using var res = await _http.PostAsync(url, new StringContent("{\"returnSecureToken\":true}", Encoding.UTF8, "application/json"));
            var body = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) throw new Exception("Firebase auth failed: " + body);
            using var doc = JsonDocument.Parse(body);
            _idToken = doc.RootElement.GetProperty("idToken").GetString();
        }

        public async Task<JsonDocument?> GetDocAsync(string path)
        {
            await EnsureAnonAuthAsync();
            var url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/{path}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _idToken);
            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        }

        public async Task PatchDocAsync(string path, string updateMaskField, object firestoreFieldsPayload)
        {
            await EnsureAnonAuthAsync();
            var url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/{path}?updateMask.fieldPaths={Uri.EscapeDataString(updateMaskField)}";
            var body = JsonSerializer.Serialize(new { fields = firestoreFieldsPayload });
            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), url);
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _idToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var res = await _http.SendAsync(req);
            if (!res.IsSuccessStatusCode)
                throw new Exception("Firestore patch failed: " + await res.Content.ReadAsStringAsync());
        }

        // Helpers to read Firestore typed values
        public static bool TryGetString(JsonDocument doc, string path, out string value)
        {
            value = "";
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return false;
            if (!TryWalk(fields, path, out var node)) return false;
            if (node.TryGetProperty("stringValue", out var sv)) { value = sv.GetString() ?? ""; return true; }
            return false;
        }
        public static bool TryGetBool(JsonDocument doc, string path, out bool value)
        {
            value = false;
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return false;
            if (!TryWalk(fields, path, out var node)) return false;
            if (node.TryGetProperty("booleanValue", out var bv)) { value = bv.GetBoolean(); return true; }
            return false;
        }
        public static long TryGetSeconds(JsonDocument doc, string path)
        {
            if (!doc.RootElement.TryGetProperty("fields", out var fields)) return 0;
            if (!TryWalk(fields, path, out var node)) return 0;
            if (node.TryGetProperty("mapValue", out var mv) && mv.TryGetProperty("fields", out var f)
                && f.TryGetProperty("seconds", out var sec) && sec.TryGetInt64(out var s)) return s;
            if (node.TryGetProperty("timestampValue", out var tv)
                && DateTimeOffset.TryParse(tv.GetString(), out var dto)) return dto.ToUnixTimeSeconds();
            return 0;
        }
        public static long NowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static bool TryWalk(JsonElement cur, string path, out JsonElement node)
        {
            node = default;
            foreach (var seg in path.Split('.'))
            {
                if (cur.TryGetProperty(seg, out var f))
                {
                    if (f.ValueKind == JsonValueKind.Object && f.TryGetProperty("mapValue", out var mv) && mv.TryGetProperty("fields", out var fields))
                        cur = fields;
                    else { node = f; return true; }
                }
                else return false;
            }
            node = cur; return true;
        }

        // Build Firestore "fields" for lock updates
        public static object BuildLockFields(string deviceId, bool active, long seconds)
        {
            return new
            {
                @lock = new
                {
                    mapValue = new
                    {
                        fields = new
                        {
                            ownerDeviceId = new { stringValue = deviceId },
                            platform      = new { stringValue = "windows" },
                            active        = new { booleanValue = active },
                            lastSeen      = new
                            {
                                mapValue = new
                                {
                                    fields = new
                                    {
                                        seconds = new { integerValue = seconds.ToString() },
                                        nanos   = new { integerValue = "0" }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }
    }
}
