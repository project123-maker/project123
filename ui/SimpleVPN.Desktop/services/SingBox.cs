using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Services
{
    public sealed class SingBox
    {
        private readonly string _baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SimpleVPN");
        private readonly string _workDir;
        private readonly string _exePath;
        private Process? _proc;

        public SingBox()
        {
            _workDir = Path.Combine(_baseDir, "gateway");
            Directory.CreateDirectory(_workDir);
            var appDir = AppContext.BaseDirectory;
            var pubExe = Path.Combine(appDir, "sing-box", "sing-box.exe");
            _exePath = File.Exists(pubExe) ? pubExe : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sing-box", "sing-box.exe");
        }

        public async Task StartAsync(string vlessUri, CancellationToken ct)
        {
            var cfgPath = Path.Combine(_workDir, "config.json");
            await File.WriteAllTextAsync(cfgPath, BuildConfigFromVless(vlessUri), ct);

            var psi = new ProcessStartInfo
            {
                FileName = _exePath,
                Arguments = $"run -c \"{cfgPath}\"",
                WorkingDirectory = Path.GetDirectoryName(_exePath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            _proc = Process.Start(psi);
        }

        public void Stop()
        {
            try { _proc?.Kill(entireProcessTree: true); } catch { }
            _proc = null;
        }

        public bool IsRunning => _proc is { HasExited: false };

        private static string Q(string s, string key)
        {
            var m = Regex.Match(s, @"(?:^|&)" + Regex.Escape(key) + @"=([^&]+)");
            return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value) : "";
        }

        private static string BuildConfigFromVless(string vless)
        {
            var m = Regex.Match(vless, @"^vless://(?<id>[^@]+)@(?<host>[^:]+):(?<port>\d+)\?([^#]+)");
            if (!m.Success) throw new ArgumentException("Invalid VLESS");
            string id = m.Groups["id"].Value;
            string host = m.Groups["host"].Value;
            int port = int.Parse(m.Groups["port"].Value);
            string q = vless[(vless.IndexOf('?') + 1)..];

            string sec = Q(q, "security");      // "reality"
            string pbk = Q(q, "pbk");
            string sid = Q(q, "sid");
            string sni = Q(q, "sni");
            string flow = Q(q, "flow");
            string fp = Q(q, "fp");

            var cfg = new
            {
                log = new { disabled = false, level = "info", timestamp = true },
                dns = new
                {
                    servers = new object[]
                    {
                        new { tag = "cf", address = "https://cloudflare-dns.com/dns-query", detour = "direct" }
                    },
                    strategy = "prefer_ipv4"
                },
                inbounds = new object[]
                {
                    new {
                        type="tun", tag="tun-in",
                        inet4_address="172.19.0.1/30", mtu=1400,
                        auto_route=true, strict_route=false, stack="gvisor"
                    }
                },
                outbounds = new object[]
                {
                    new {
                        type="vless", tag="proxy",
                        server=host, server_port=port, uuid=id,
                        flow = string.IsNullOrWhiteSpace(flow)? null: flow,
                        tls = sec=="reality" ? new {
                            enabled=true,
                            server_name= string.IsNullOrWhiteSpace(sni)? "www.microsoft.com": sni,
                            insecure=false,
                            utls = new { enabled=true, fingerprint= string.IsNullOrWhiteSpace(fp)? "chrome": fp }
                        } : null,
                        reality = sec=="reality" ? new { enabled=true, public_key=pbk, short_id= sid??"" } : null,
                        transport = new { type="tcp" }
                    },
                    new { type="direct", tag="direct" },
                    new { type="block", tag="block" }
                },
                route = new
                {
                    rules = new object[]
                    {
                        new { outbound="direct", protocol = new[] {"dns"} },
                        new { outbound="direct", ip_cidr = new[] {"10.0.0.0/8","172.16.0.0/12","192.168.0.0/16","127.0.0.0/8"} },
                        new { outbound="proxy" }
                    },
                    final = "proxy",
                    auto_detect_interface = true
                }
            };

            return JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
