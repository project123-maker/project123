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
        static Process? _p;

        static string BaseDir => AppContext.BaseDirectory;
        static string BinDir => Path.Combine(BaseDir, "..", "bin", "sing-box");
        static string SbExe => Path.Combine(BinDir, "sing-box.exe");
        static string CfgPath => Path.Combine(BinDir, "config.json");

        public static async Task StartAsync(string vlessUri)
        {
            Directory.CreateDirectory(BinDir);
            await File.WriteAllTextAsync(CfgPath, BuildConfig(vlessUri), Encoding.UTF8);

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
            _p.Start();
        }

        public static Task StopAsync()
        {
            try
            {
                if (_p != null && !_p.HasExited)
                {
                    _p.Kill(true);
                    _p.WaitForExit(2000);
                }
            }
            catch { /* ignore */ }
            finally { _p = null; }
            return Task.CompletedTask;
        }

        // Generates a modern sing-box config using TUN + REALITY vless
        static string BuildConfig(string vless)
        {
            // keep it short; DNS new format (no deprecated)
            // You can tweak these defaults later
            var cfg = new
            {
                log = new { level = "info" },
                dns = new
                {
                    servers = new object[]
                    {
                        new { address = "https://1.1.1.1/dns-query", detour = "proxy" },
                        new { address = "local" }
                    }
                },
                inbounds = new object[]
                {
                    new {
                        type = "tun",
                        tag = "tun-in",
                        interface_name = "SimpleVPN",
                        stack = "gvisor",
                        mtu = 1400,
                        inet4_address = new [] { "172.19.0.2/30" },
                        auto_route = true,
                        strict_route = true
                    }
                },
                outbounds = new object[]
                {
                    new { type = "direct", tag = "direct" },
                    new { type = "block", tag = "block" },
                    ParseVlessRealityOutbound(vless)
                },
                route = new
                {
                    rules = new object[]
                    {
                        new { inbound = new []{"tun-in"}, outbound = "proxy" }
                    }
                }
            };

            return JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        }

        static object ParseVlessRealityOutbound(string vless)
        {
            // VERY small parser for vless://…&sni=…&pbk=…
            // You already supply correct URIs.
            var uri = new Uri(vless);
            var host = uri.Host;
            var port = uri.Port;
            var id = uri.UserInfo; // uuid

            var qp = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var sni = qp["sni"] ?? host;
            var pbk = qp["pbk"] ?? "";
            var flow = qp["flow"] ?? "";

            return new
            {
                type = "vless",
                tag = "proxy",
                server = host,
                server_port = port,
                uuid = id,
                flow = string.IsNullOrWhiteSpace(flow) ? null : flow,
                packet_encoding = "xudp",
                tls = new
                {
                    enabled = true,
                    server_name = sni,
                    insecure = false,
                    reality = new { enabled = true, public_key = pbk, short_id = qp["sid"] ?? "" }
                }
            };
        }
    }
}
