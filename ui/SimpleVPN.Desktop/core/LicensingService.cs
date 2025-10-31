using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Core
{
    public sealed class LicensingService
    {
        // Put your values in appsettings or hardcode here:
        private readonly string _apiKey;
        private readonly string _projectId;

        private string _idToken;
        private string _uid;

        private static readonly HttpClient Http = new HttpClient();

        public LicensingService(string apiKey, string projectId)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _projectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        }

        public string CurrentUid => _uid;

        public async Task EnsureAnonAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrEmpty(_idToken)) return;

            var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}";
            var body = new { returnSecureToken = true };
            var res = await Http.PostAsync(url, JsonContent(body), ct);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            _idToken = doc.RootElement.GetProperty("idToken").GetString();
            _uid = doc.RootElement.GetProperty("localId").GetString();
        }

        /// <summary>
        /// Redeem code and fetch vless. For simplicity we read vless from /configs/current (public read).
        /// Your existing Cloud Function / server can enforce locks; client still sends its uid.
        /// </summary>
        public async Task<(bool ok, string message, string vless)> RedeemAsync(string code, string deviceId, string platform, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(code)) return (false, "Empty code", null);

            await EnsureAnonAsync(ct);

            // Check code exists (read-only)
            var codeDoc = await GetDocAsync($"codes/{Uri.EscapeDataString(code)}", ct);
            if (codeDoc == null) return (false, "Code not found", null);

            // Get current vless (configs/current)
            var current = await GetDocAsync("configs/current", ct);
            if (current == null || !current.RootElement.TryGetProperty("fields", out var fields) ||
                !fields.TryGetProperty("vless", out var vlessField) ||
                !vlessField.TryGetProperty("stringValue", out var sv))
            {
                return (false, "CONFIGURATION_MISSING", null);
            }

            var vless = sv.GetString();

            // Optional: touch grants/{uid} (best-effort, ignore failures if rules deny)
            try
            {
                var grantDoc = $"grants/{_uid}";
                var payload = new
                {
                    fields = new
                    {
                        code = Str(code),
                        deviceId = Str(deviceId ?? ""),
                        platform = Str(platform ?? "windows"),
                        lastSeen = new { timestampValue = DateTime.UtcNow.ToString("O") }
                    }
                };
                var url = FirestoreUrl(grantDoc) + "?mask.fieldPaths=code&mask.fieldPaths=deviceId&mask.fieldPaths=platform&mask.fieldPaths=lastSeen";
                var req = new HttpRequestMessage(HttpMethod.Patch, url);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _idToken);
                req.Content = JsonContent(payload);
                await Http.SendAsync(req, ct);
            }
            catch { /* non-fatal */ }

            return (true, "OK", vless);
        }

        public async Task<string> GetFreshVlessAsync(CancellationToken ct = default)
        {
            var current = await GetDocAsync("configs/current", ct);
            if (current == null) return null;

            if (current.RootElement.TryGetProperty("fields", out var fields) &&
                fields.TryGetProperty("vless", out var vlessField) &&
                vlessField.TryGetProperty("stringValue", out var sv))
            {
                return sv.GetString();
            }
            return null;
        }

        // ------- Firestore REST helpers (simple) -------
        private string FirestoreUrl(string docPath) =>
            $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/{docPath}";

        private async Task<JsonDocument> GetDocAsync(string docPath, CancellationToken ct)
        {
            var url = FirestoreUrl(docPath);
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            // configs are public; codes/grants require auth – send token when we have it
            if (!string.IsNullOrEmpty(_idToken))
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _idToken);

            var res = await Http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(json);
        }

        private static StringContent JsonContent(object obj)
        {
            var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        private static object Str(string s) => new { stringValue = s ?? "" };
    }
}
