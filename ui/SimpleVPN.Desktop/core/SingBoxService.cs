using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop.Core
{
    public sealed class SingBoxService
    {
        private readonly string _root;
        private readonly string _sbExe;
        private Process _proc;

        public SingBoxService()
        {
            _root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "bin", "sing-box"));
            _sbExe = Path.Combine(_root, "sing-box.exe");
        }

        public bool IsRunning => _proc != null && !_proc.HasExited;

        public async Task StartAsync(string vless, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(vless)) throw new ArgumentException("vless is empty");
            WriteConfig(vless);

            if (!File.Exists(_sbExe))
                throw new FileNotFoundException("sing-box.exe not found", _sbExe);

            if (IsRunning) await StopAsync();

            var psi = new ProcessStartInfo
            {
                FileName = _sbExe,
                Arguments = "run -c .\\config.json",
                WorkingDirectory = _root,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine(e.Data); };
            _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.WriteLine(e.Data); };

            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();

            // tiny grace wait so the TUN comes up
            await Task.Delay(300, ct);
        }

        public Task StopAsync()
        {
            try
            {
                if (IsRunning)
                {
                    _proc.Kill(entireProcessTree: true);
                    _proc.WaitForExit(2000);
                }
            }
            catch { /* ignore */ }
            finally
            {
                _proc?.Dispose();
                _proc = null;
            }
            return Task.CompletedTask;
        }

        /// <summary>Writes config.json next to sing-box.exe (safe JSON builder, no new syntax).</summary>
        public void WriteConfig(string vless)
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "last.vless.txt"), vless ?? "");

            // parse vless
            var u = new Uri(vless);
            var uuid = u.UserInfo;
            var host = u.Host;
            var port = u.Port == -1 ? 443 : u.Port;

            // parse query
            var q = System.Web.HttpUtility.ParseQueryString(u.Query);
            string sni = q["sni"] ?? host;
            string pbk = q["pbk"] ?? "";
            string sid = q["sid"] ?? "";
            string flow = q["flow"] ?? "";
            string fp = q["fp"] ?? "chrome";

            // build config (old, stable fields to avoid breaking)
            var cfg = new
            {
                log = new { level = "info", timestamp = true },
                dns = new
                {
                    strategy = "ipv4_only",
                    disable_cache = true,
                    servers = new object[]
                    {
                        new { tag = "cf", address = "https://1.1.1.1/dns-query", strategy = "ipv4_only", detour = "proxy" },
                        new { tag = "gg", address = "https://dns.google/dns-query", strategy = "ipv4_only", detour = "proxy" }
                    },
                    rules = new object[]
                    {
                        new { outbound = "any", server = "cf" }
                    }
                },
                inbounds = new object[]
                {
                    new {
                        type = "tun",
                        tag = "tun-in",
                        interface_name = "SimpleVPN",
                        address = new [] { "172.19.0.1/30" },
                        mtu = 1400,
                        stack = "gvisor",
                        auto_route = true,
                        strict_route = true,
                        sniff = true
                    }
                },
                outbounds = new object[]
                {
                    new {
                        type = "vless",
                        tag = "proxy",
                        server = host,
                        server_port = port,
                        uuid = uuid,
                        flow = flow,
                        tls = new {
                            enabled = true,
                            server_name = sni,
                            reality = new { enabled = true, public_key = pbk, short_id = sid },
                            utls = new { enabled = true, fingerprint = fp }
                        }
                    },
                    new { type = "dns", tag = "dns-out" },
                    new { type = "direct", tag = "direct" },
                    new { type = "block", tag = "block" }
                },
                route = new
                {
                    auto_detect_interface = true,
                    default_domain_resolver = "cf",
                    final = "proxy",
                    rules = new object[]
                    {
                        new { protocol = new [] { "dns" }, outbound = "dns-out" },
                        new { ip_is_private = true, outbound = "direct" }
                    }
                }
            };

            var opts = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(cfg, opts);
            File.WriteAllText(Path.Combine(_root, "config.json"), json);
        }
    }
}
