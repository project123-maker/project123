using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop
{
    public static class SingBox
    {
        private static Process? _p;

        // publish\  ->  ..\bin\sing-box\ (exe + wintun.dll + config.json)
        static string BaseDir => AppContext.BaseDirectory;
        static string BinDir => Path.GetFullPath(Path.Combine(BaseDir, "..", "bin", "sing-box"));
        static string SbExe => Path.Combine(BinDir, "sing-box.exe");
        static string CfgPath => Path.Combine(BinDir, "config.json");

        public static bool IsRunning => _p is { HasExited: false } && !_p.HasExited;

        public static async Task StartAsync(string vlessUri)
        {
            Directory.CreateDirectory(BinDir);

            // sanity: sing-box + wintun must exist
            if (!File.Exists(SbExe)) throw new FileNotFoundException("sing-box.exe missing", SbExe);
            if (!File.Exists(Path.Combine(BinDir, "wintun.dll"))) throw new FileNotFoundException("wintun.dll missing");

            await File.WriteAllTextAsync(CfgPath, BuildConfig(vlessUri), Encoding.UTF8).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName = SbExe,
                Arguments = $"run -c \"{CfgPath}\"",
                WorkingDirectory = BinDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Log("[sb] " + e.Data); };
            _p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Log("[sb!] " + e.Data); };

            _p.Start();
            _p.BeginOutputReadLine();
            _p.BeginErrorReadLine();
        }

        public static Task StopAsync()
        {
            try
            {
                if (_p != null && !_p.HasExited)
                {
                    _p.Kill(entireProcessTree: true);
                    _p.WaitForExit(2000);
                }
            }
            catch { /* ignore */ }
            finally { _p = null; }
            return Task.CompletedTask;
        }

        // ---- config ----
        static string BuildConfig(string vless)
        {
            var (host, port, uuid, sni, pbk, sid, flow, fp) = ParseVless(vless);

            var cfg = new
            {
                log = new { level = "info" },

                dns = new
                {
                    servers = new object[]
                    {
                        // Bootstrap first: system/local resolver (never through proxy)
                        new { address = "local" },
                        // DoH directly (not through proxy) so the first handshake can succeed
                        new { address = "https://1.1.1.1/dns-query", detour = "direct" }
                    },
                    strategy = "ipv4_only" // avoid v6 pitfalls on some ISPs
                },

                inbounds = new object[]
                {
                    new {
                        type = "tun",
                        tag = "tun-in",
                        interface_name = "SimpleVPN",
                        stack = "gvisor",
                        mtu = 1400,
                        inet4_address = new []{ "172.19.0.2/30" },
                        auto_route = true,
                        strict_route = true
                    }
                },

                outbounds = new object[]
                {
                    new { type = "direct", tag = "direct" },
                    new { type = "block",  tag = "block"  },
                    new {
                        type = "vless",
                        tag  = "proxy",
                        server = host,
                        server_port = port,
                        uuid = uuid,
                        flow = string.IsNullOrWhiteSpace(flow) ? null : flow,
                        transport = new { type = "tcp" },
                        tls = new
                        {
                            enabled = true,
                            server_name = string.IsNullOrWhiteSpace(sni) ? host : sni,
                            insecure = false,
                            utls = new { enabled = true, fingerprint = string.IsNullOrWhiteSpace(fp) ? "chrome" : fp },
                            reality = new { enabled = true, public_key = pbk, short_id = sid ?? "" }
                        }
                    }
                },

                route = new
                {
                    rules = new object[]
                    {
                        // send all traffic from TUN to proxy
                        new { inbound = new [] { "tun-in" }, outbound = "proxy" },

                        // DNS must go out direct during bootstrap
                        new { protocol = new[]{ "dns" }, outbound = "direct" },

                        // safety
                        new { geoip = new[]{ "private" }, outbound = "direct" }
                    }
                }
            };

            return JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        }

        static (string host, int port, string uuid, string sni, string pbk, string sid, string flow, string fp) ParseVless(string vless)
        {
            // expected like:
            // vless://<uuid>@host:443?encryption=none&security=reality&type=tcp&flow=xtls-rprx-vision&fp=chrome&pbk=...&sid=...&sni=www.microsoft.com#name
            var u = new Uri(vless);
            var host = u.Host;
            var port = u.IsDefaultPort ? 443 : u.Port;
            var uuid = u.UserInfo;

            var q = System.Web.HttpUtility.ParseQueryString(u.Query);
            return (
                host,
                port,
                uuid,
                q["sni"] ?? host,
                q["pbk"] ?? "",
                q["sid"] ?? "",
                q["flow"] ?? "",
                q["fp"] ?? "chrome"
            );
        }
    }
}
