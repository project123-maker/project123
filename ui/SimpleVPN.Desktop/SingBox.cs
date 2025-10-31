using System.Diagnostics;
using System.Text.Json;
using System;
using System.IO;
using System.Diagnostics;

namespace SimpleVPN.Desktop;

public static class SingBox
{
    public static string Root => Path.Combine(AppContext.BaseDirectory);
    public static string BinDir => Path.Combine(Root, "bin", "sing-box");
    public static string Exe => Path.Combine(BinDir, "sing-box.exe");
    public static string ConfigPath => Path.Combine(BinDir, "config.json");

    private static Process? _p;

    public static void WriteConfigFromVless(string vless)
    {
        // Parse a few bits
        var uri = new Uri(vless);
        var uuid = uri.UserInfo;
        var host = uri.Host;
        var port = uri.Port;
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var sni = q.Get("sni");
        var pbk = q.Get("pbk");
        var sid = q.Get("sid");
        var fp = q.Get("fp");
        var flow = q.Get("flow");

        var cfg = new
        {
            log = new { level = "info", timestamp = true },
            dns = new
            {
                servers = new object[] {
                    new { tag="cf", address="https://1.1.1.1/dns-query", strategy="ipv4_only", detour="proxy" },
                    new { tag="gg", address="https://dns.google/dns-query", strategy="ipv4_only", detour="proxy" }
                },
                strategy = "ipv4_only",
                disable_cache = true,
                rules = new object[] { new { outbound = "any", server = "cf" } }
            },
            inbounds = new object[] {
                new {
                    type="tun", tag="tun-in", interface_name="SimpleVPN",
                    address = new [] { "172.19.0.1/30" },
                    mtu=1400, stack="gvisor", auto_route=true, strict_route=true, sniff=true
                }
            },
            outbounds = new object[] {
                new {
                    type="vless", tag="proxy", server=host, server_port=port, uuid=uuid, flow=flow,
                    tls = new {
                        enabled = true, server_name = sni,
                        reality = new { enabled = true, public_key = pbk, short_id = sid },
                        utls = fp is null ? null : new { enabled = true, fingerprint = fp }
                    }
                },
                new { type="dns", tag="dns-out" },
                new { type="direct", tag="direct" },
                new { type="block", tag="block" }
            },
            route = new
            {
                auto_detect_interface = true,
                default_domain_resolver = "cf",
                final = "proxy",
                rules = new object[] {
                    new { protocol = new [] { "dns" }, outbound = "dns-out" },
                    new { ip_is_private = true, outbound = "direct" }
                }
            }
        };

        Directory.CreateDirectory(BinDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Start()
    {
        Stop();
        _p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Exe,
                Arguments = $"run -c \"{ConfigPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        _p.Start();
    }

    public static void Stop()
    {
        try
        {
            if (_p != null && !_p.HasExited) _p.Kill(true);
        }
        catch { /* ignore */ }
        _p = null;
    }
}
