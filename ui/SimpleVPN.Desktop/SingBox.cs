using System;
using System.Collections.Generic;
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
        public static bool IsRunning => _p is { HasExited: false };

        // In publish, sing-box folder sits next to the exe
        private static string BaseDir => AppContext.BaseDirectory;
        private static string SbDir => Path.Combine(BaseDir, "sing-box");
        private static string SbExe => Path.Combine(SbDir, "sing-box.exe");
        private static string CfgPath => Path.Combine(SbDir, "config.json");

        public static async Task StartAsync(string vlessUri)
        {
            try
            {
                Directory.CreateDirectory(SbDir);
                EnsureBinaries();

                var cfg = BuildConfig(vlessUri);
                await File.WriteAllTextAsync(CfgPath, cfg, Encoding.UTF8);

                var psi = new ProcessStartInfo
                {
                    FileName = SbExe,
                    Arguments = $"run -c \"{CfgPath}\"",
                    WorkingDirectory = SbDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // kill previous if any
                await StopAsync();

                _p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                _p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Log("[SB] " + e.Data); };
                _p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Logger.Log("[SB] " + e.Data); };
                _p.Exited += (_, __) => Logger.Log("[SB] exited");

                Logger.Log("Starting sing-box…");
                _p.Start();
                _p.BeginOutputReadLine();
                _p.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Logger.Log("StartAsync error: " + ex);
                throw;
            }
        }

        public static Task StopAsync()
        {
            try
            {
                if (_p is { HasExited: false })
                {
                    Logger.Log("Stopping sing-box…");
                    _p.Kill(entireProcessTree: true);
                    if (!_p.WaitForExit(2000))
                        Logger.Log("sing-box did not exit in 2s (killed).");
                }
            }
            catch (Exception ex)
            {
                Logger.Log("StopAsync error: " + ex);
            }
            finally
            {
                _p = null;
            }
            return Task.CompletedTask;
        }

        private static void EnsureBinaries()
        {
            if (!File.Exists(SbExe))
                throw new FileNotFoundException("sing-box.exe missing at " + SbExe);
            var wintun = Path.Combine(SbDir, "wintun.dll");
            if (!File.Exists(wintun))
                Logger.Log("WARNING: wintun.dll not found next to sing-box.exe (TUN will fail).");
        }

        // ----- Config -----
        private static string BuildConfig(string vless)
        {
            var ob = ParseVless(vless);

            // Stable, leak-free defaults
            var cfg = new
            {
                log = new { level = "info" },

                dns = new
                {
                    servers = new object[]
                    {
                        new { tag = "cf", address = "https://cloudflare-dns.com/dns-query", detour = "proxy" },
                        new { tag = "gg", address = "https://dns.google/dns-query",          detour = "proxy" },
                        new { tag = "local", address = "local" }
                    },
                    strategy = "prefer_ipv4"
                },

                inbounds = new object[]
                {
                    new
                    {
                        type = "tun",
                        tag  = "tun-in",
                        interface_name = "SimpleVPN",
                        stack = "gvisor",
                        mtu = 1400,
                        auto_route = true,
                        strict_route = false,      // avoid black-hole if proxy dies
                        endpoint_independent_nat = true
                    }
                },

                outbounds = new object[]
                {
                    // PRIMARY outbound = VLESS proxy
                    new
                    {
                        type = "vless",
                        tag  = "proxy",
                        server = ob.Server,
                        server_port = ob.Port,
                        uuid = ob.Uuid,
                        flow = string.IsNullOrWhiteSpace(ob.Flow) ? null : ob.Flow,
                        tls = new
                        {
                            enabled = true,
                            server_name = ob.Sni ?? ob.Server,
                            utls = new { enabled = true, fingerprint = ob.Fp ?? "chrome" },
                            reality = new { enabled = true, public_key = ob.Pbk ?? "", short_id = ob.Sid ?? "" }
                        },
                        transport = new { type = ob.Transport ?? "tcp" },
                        packet_encoding = "xudp"
                    },

                    new { type = "direct", tag = "direct" },
                    new { type = "block",  tag = "block" }
                },

                route = new
                {
                    auto_detect_interface = true,
                    final = "proxy",                 // send everything through proxy
                    rules = new object[]
                    {
                        new { protocol = "dns", outbound = "proxy" }   // DNS goes through tunnel
                    }
                }
            };

            return JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        }

        // Minimal VLESS Reality parser (no external deps)
        private static (string Server, int Port, string Uuid, string? Sni, string? Pbk, string? Sid, string? Flow, string? Fp, string? Transport) ParseVless(string uriStr)
        {
            var uri = new Uri(uriStr);
            var server = uri.Host;
            var port = uri.Port;
            var uuid = uri.UserInfo; // vless://<uuid>@host:port

            var q = ParseQuery(uri.Query);
            q.TryGetValue("sni", out var sni);
            q.TryGetValue("pbk", out var pbk);
            q.TryGetValue("sid", out var sid);
            q.TryGetValue("flow", out var flow);
            q.TryGetValue("fp", out var fp);
            q.TryGetValue("type", out var transport);

            return (server, port, uuid, sni, pbk, sid, flow, fp, transport);
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            var span = query.AsSpan();
            if (span[0] == '?') span = span[1..];

            foreach (var part in span.ToString().Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                string k, v;
                if (idx < 0) { k = part; v = ""; }
                else { k = part[..idx]; v = part[(idx + 1)..]; }
                dict[Uri.UnescapeDataString(k)] = Uri.UnescapeDataString(v);
            }
            return dict;
        }
    }
}
