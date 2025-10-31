using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimpleVPN.Desktop;

public sealed class FirebaseClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly JsonSerializerOptions _j = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private string? _idToken;
    private string? _uid;

    private record SignUpResp(string idToken, string localId);
    private record DocResp(Dictionary<string, object> fields, string? name, string? updateTime);

    public string? Uid => _uid;
    public string? IdToken => _idToken;

    private static string LocalPath(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimpleVPN", name);

    public async Task EnsureAnonAsync()
    {
        // try load
        var credFile = LocalPath("auth.json");
        if (File.Exists(credFile))
        {
            var obj = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(credFile));
            if (obj != null && obj.TryGetValue("idToken", out var t) && obj.TryGetValue("uid", out var u))
            {
                _idToken = t; _uid = u;
                return;
            }
        }

        // fresh anonymous sign-up
        var url = $"https://identitytoolkit.googleapis.com/v1/accounts:signUp?key={AppSecrets.ApiKey}";
        var body = new { returnSecureToken = true };
        var resp = await _http.PostAsync(url, new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        var json = JsonSerializer.Deserialize<SignUpResp>(await resp.Content.ReadAsStringAsync(), _j)!;
        _idToken = json.idToken; _uid = json.localId;

        Directory.CreateDirectory(Path.GetDirectoryName(credFile)!);
        await File.WriteAllTextAsync(credFile, JsonSerializer.Serialize(new { idToken = _idToken, uid = _uid }));
    }

    private HttpRequestMessage Authed(HttpMethod m, string url)
    {
        var req = new HttpRequestMessage(m, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _idToken);
        return req;
    }

    // Firestore field helpers
    public static object Str(string v) => new { stringValue = v };
    public static object Bool(bool v) => new { booleanValue = v };
    public static object Int(long v) => new { integerValue = v.ToString() };
    public static object Ts(DateTimeOffset v) => new { timestampValue = v.UtcDateTime.ToString("O") };
    public static object Map(Dictionary<string, object> fields) => new { mapValue = new { fields } };

    private static string DocUrl(string path) =>
        $"https://firestore.googleapis.com/v1/projects/{AppSecrets.ProjectId}/databases/{Uri.EscapeDataString(AppSecrets.Database)}/documents/{path}";

    public async Task<Dictionary<string, object>?> GetDocAsync(string path, string? mask = null)
    {
        await EnsureAnonAsync();
        var url = DocUrl(path);
        if (!string.IsNullOrEmpty(mask)) url += $"?mask.fieldPaths={Uri.EscapeDataString(mask)}";
        var resp = await _http.SendAsync(Authed(HttpMethod.Get, url));
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        var raw = await resp.Content.ReadAsStringAsync();
        var doc = JsonSerializer.Deserialize<JsonElement>(raw);
        return ParseFields(doc);
    }

    // PATCH doc with fields (optimistic, no service account)
    public async Task PatchDocAsync(string path, Dictionary<string, object> fields, IEnumerable<string>? updateMask = null)
    {
        await EnsureAnonAsync();
        var mask = updateMask != null ? string.Join(",", updateMask.Select(Uri.EscapeDataString)) : null;
        var url = DocUrl(path) + (mask != null ? $"?updateMask.fieldPaths={mask}" : "");
        var body = JsonSerializer.Serialize(new { fields });
        var req = Authed(HttpMethod.Patch, url);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    public async Task SetDocAsync(string path, Dictionary<string, object> fields, bool merge = true)
    {
        await EnsureAnonAsync();
        var url = DocUrl(path) + (merge ? "?currentDocument.exists=true" : "");
        var body = JsonSerializer.Serialize(new { fields });
        var req = Authed(HttpMethod.Patch, url);
        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
    }

    public static Dictionary<string, object>? ParseFields(JsonElement doc)
    {
        if (!doc.TryGetProperty("fields", out var fields)) return null;

        Dictionary<string, object> ReadMap(JsonElement f)
        {
            var res = new Dictionary<string, object>();
            foreach (var kv in f.EnumerateObject())
                res[kv.Name] = ReadValue(kv.Value);
            return res;
        }
        object ReadValue(JsonElement v)
        {
            if (v.TryGetProperty("stringValue", out var s)) return s.GetString()!;
            if (v.TryGetProperty("booleanValue", out var b)) return b.GetBoolean();
            if (v.TryGetProperty("integerValue", out var i)) return long.Parse(i.GetString()!);
            if (v.TryGetProperty("timestampValue", out var t)) return DateTimeOffset.Parse(t.GetString()!, null);
            if (v.TryGetProperty("mapValue", out var m))
            {
                var mv = m.GetProperty("fields");
                return ReadMap(mv);
            }
            return null!;
        }

        return ReadMap(fields);
    }
}
