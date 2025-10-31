namespace SimpleVPN.Desktop;
using System;



public static class AppSecrets
{
    // 🔁 fill these from your Firebase console (safe to ship in client)
    public const string ProjectId = "simplevpn-3272d";         // e.g., "simplevpn-eur3"
    public const string ApiKey = "AIzaSyCV3CSoZ7V8uSGRuFwY82FJ4fnC66Ky5jc";         // e.g., "AIzaSyD*****"
    public const string Database = "(default)";

    // 🔧 Firestore collection/doc names you showed in screenshots
    public const string CodesCol = "codes";
    public const string GrantsCol = "grants";
    public const string ConnsCol = "connections";
    public const string ConfigsCol = "configs";   // contains docs like "current", "u08", ...
    public const string DefaultConfigDoc = "current"; // fallback when code has no vlessPath


    // heartbeat (matches your Android habit)
    public static readonly TimeSpan HeartbeatPeriod = TimeSpan.FromSeconds(7);
}
