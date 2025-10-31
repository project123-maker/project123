using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class Firebase
{
    readonly string _projectId;
    readonly string _apiKey;
    string? _idToken;
    string? _uid;

    static readonly HttpClient Http = new HttpClient();

    public Firebase(string projectId, string apiKey)
    {
        _projectId = projectId;
        _apiKey = apiKey;
    }

    public string Uid => _uid ?? throw new InvalidOperationException("Not authed");

    public async Task SignInAnonymouslyAsync(CancellationToken ct = default)
    {
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={_apiKey}";
        var body = new { returnSecureToken = true };
        var res = await PostJsonAsync(url, body, ct);
        _idToken = res.GetProperty("idToken").GetString();
        _uid = res.GetProperty("localId").GetString();
    }

    // ---- Firestore helpers (v1 REST) ----

    string FsBase => $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents";

    public async Task<JsonElement?> GetDocAsync(string path, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{FsBase}/{path}");
        AttachAuth(req);
        var rsp = await Http.SendAsync(req, ct);
        if (rsp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        rsp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.Clone();
    }

    // set/merge
    public async Task PatchDocAsync(string path, object payload, IEnumerable<string>? updateMask = null, CancellationToken ct = default)
    {
        var mask = updateMask is null ? "" : $"?updateMask.fieldPaths={string.Join("&updateMask.fieldPaths=", updateMask)}";
        var req = new HttpRequestMessage(HttpMethod.Patch, $"{FsBase}/{path}{mask}");
        AttachAuth(req);
        req.Content = new StringContent(ToFsDoc(payload), Encoding.UTF8, "application/json");
        var rsp = await Http.SendAsync(req, ct);
        rsp.EnsureSuccessStatusCode();
    }

    // create (server only in rules – not used by client app)
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
        var rsp = await Http.SendAsync(req, ct);
        rsp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await rsp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.Clone();
    }

    void AttachAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(_idToken))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _idToken);
    }

    // Convert POCO/anon to Firestore document JSON
    static string ToFsDoc(object o)
    {
        return JsonSerializer.Serialize(new { fields = ToFields(o) });

        static Dictionary<string, object> ToFields(object obj)
        {
            var dict = new Dictionary<string, object>();
            foreach (var p in obj.GetType().GetProperties())
            {
                var name = p.Name;
                var value = p.GetValue(obj);

                dict[name] = ToFsValue(value);
            }
            return dict;
        }

        static object ToFsValue(object? v)
        {
            if (v == null) return new { nullValue = (string?)null };
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

    // Firestore field reader helpers
    public static string? GetString(JsonElement document, string fieldPath)
    {
        // doc.fields.<field>.stringValue
        if (!document.TryGetProperty("fields", out var fields)) return null;
        if (!fields.TryGetProperty(fieldPath, out var f)) return null;
        return f.TryGetProperty("stringValue", out var s) ? s.GetString() : null;
    }

    public static bool? GetBool(JsonElement document, string fieldPath)
    {
        if (!document.TryGetProperty("fields", out var fields)) return null;
        if (!fields.TryGetProperty(fieldPath, out var f)) return null;
        return f.TryGetProperty("booleanValue", out var b) ? b.GetBoolean() : (bool?)null;
    }

    public static JsonElement? GetMap(JsonElement document, string fieldPath)
    {
        if (!document.TryGetProperty("fields", out var fields)) return null;
        if (!fields.TryGetProperty(fieldPath, out var f)) return null;
        return f.TryGetProperty("mapValue", out var m) ? m.GetProperty("fields") : (JsonElement?)null;
    }

    // Stable device id
    public static string DeviceId()
    {
        // MachineGuid + User SID hashed
        string mg = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "")?.ToString() ?? "";
        string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(mg + "|" + sid));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    static async Task<JsonElement> PostJsonAsync(string url, object body, CancellationToken ct)
    {
        var rsp = await Http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        var txt = await rsp.Content.ReadAsStringAsync(ct);
        rsp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(txt);
        return doc.RootElement.Clone();
    }
}
