package main

import (
    "bytes"
    "crypto/hmac"
    "crypto/sha256"
    "encoding/hex"
    "encoding/json"
    "errors"
    "fmt"
    "io"
    "net/http"
    "net/url"
    "os"
    "os/exec"
    "path/filepath"
    "runtime"
    "strings"
    "time"

    "github.com/getlantern/systray"
    "simplevpn-desktop/deviceid"
)

var (
    gatewayURL = os.Getenv("SVPN_GATEWAY") // e.g. http://localhost:8787
    secret     = os.Getenv("SVPN_SECRET")  // same as SIMPLEVPN_APP_SECRET
    deviceID   = deviceid.MachineID()
    curCode    string
    curVLESS   string
    sbxCmd     *exec.Cmd
    hbStop     chan struct{}
)

func binSingBox() string {
    // Use your path: SimpleVPNDesktop\bin\sing-box\sing-box.exe
    root := filepath.Join(os.Getenv("USERPROFILE"), "Desktop", "SimpleVPNDesktop", "bin", "sing-box")
    if runtime.GOOS == "windows" {
        return filepath.Join(root, "sing-box.exe")
    }
    return filepath.Join(root, "sing-box")
}

func sign(method, path string, body []byte) (sig, ts, nonce string) {
    ts = fmt.Sprintf("%d", time.Now().Unix())
    nonce = fmt.Sprintf("%d", time.Now().UnixNano())
    base := method + "|" + path + "|" + ts + "|" + nonce + "|" + string(body)
    m := hmac.New(sha256.New, []byte(secret))
    m.Write([]byte(base))
    sig = hex.EncodeToString(m.Sum(nil))
    return
}

func postJSON(path string, payload any, out any) error {
    b, _ := json.Marshal(payload)
    req, _ := http.NewRequest("POST", gatewayURL+path, bytes.NewReader(b))
    sig, ts, nonce := sign("POST", path, b)
    req.Header.Set("X-Simple-Signature", sig)
    req.Header.Set("X-Simple-Timestamp", ts)
    req.Header.Set("X-Simple-Nonce", nonce)
    req.Header.Set("Content-Type", "application/json")
    client := &http.Client{ Timeout: 10 * time.Second }
    resp, err := client.Do(req)
    if err != nil { return err }
    defer resp.Body.Close()
    body, _ := io.ReadAll(resp.Body)
    if resp.StatusCode >= 300 {
        return fmt.Errorf("%s: %s", resp.Status, string(body))
    }
    if out != nil { return json.Unmarshal(body, out) }
    return nil
}

func redeem(code string) (string, error) {
    var r struct{ Vless string `json:"vless"` }
    err := postJSON("/redeem", map[string]any{
        "code": code, "uid": deviceID, "deviceId": deviceID, "platform": platformTag(),
    }, &r)
    return r.Vless, err
}

func acquireLock(code string) error {
    return postJSON("/acquireLock", map[string]any{
        "code": code, "deviceId": deviceID, "platform": platformTag(),
    }, nil)
}
func releaseLock(code string) { _ = postJSON("/releaseLock", map[string]any{
    "code": code, "deviceId": deviceID,
}, nil) }

func heartbeatLoop(code string) {
    t := time.NewTicker(7 * time.Second)
    hbStop = make(chan struct{})
    go func() {
        for {
            select {
            case <-t.C:
                _ = postJSON("/heartbeat", map[string]any{
                    "code": code, "deviceId": deviceID,
                }, nil)
            case <-hbStop:
                t.Stop(); return
            }
        }
    }()
}

func platformTag() string {
    switch runtime.GOOS {
    case "windows": return "windows"
    case "darwin": return "macos"
    default: return runtime.GOOS
    }
}

func vlessToSingBox(vlessURI string) (string, error) {
    u, err := url.Parse(vlessURI)
    if err != nil { return "", err }
    if u.Scheme != "vless" { return "", errors.New("not vless://") }

    host := u.Hostname()
    port := u.Port(); if port == "" { port = "443" }
    uuid := u.User.Username(); if uuid == "" { return "", errors.New("missing uuid") }
    q := u.Query()
    sni := q.Get("sni"); if sni == "" { sni = q.Get("host") }
    pbk := q.Get("pbk")
    sid := q.Get("sid")
    flow := q.Get("flow")

    // Modern sing-box config:
    cfg := fmt.Sprintf(`{
  "log": { "level": "info" },

  "dns": {
    "servers": [
      { "address": "https://1.1.1.1/dns-query", "strategy": "ipv4_only", "detour": "proxy" },
      { "address": "8.8.8.8", "strategy": "ipv4_only", "detour": "proxy" }
    ]
  },

  "inbounds": [
    {
      "type": "tun",
      "name": "svpn",
      "inet4_address": "172.18.0.1/30",
      "mtu": 1500,
      "auto_route": true,
      "strict_route": false,
      "endpoint_independent_nat": true,
      "stack": "system",
      "sniff": true
    }
  ],

  "outbounds": [
    {
      "type": "vless",
      "tag": "proxy",
      "server": "%s",
      "server_port": %s,
      "uuid": "%s",
      "flow": "%s",
      "tls": {
        "enabled": true,
        "server_name": "%s",
        "reality": { "enabled": true, "public_key": "%s", "short_id": "%s" },
        "utls": { "enabled": true, "fingerprint": "chrome" }
      },
      "packet_encoding": "xudp"
    },
    { "type": "dns",   "tag": "dns-out" },
    { "type": "direct","tag": "direct" },
    { "type": "block", "tag": "block" }
  ],

  "route": {
    "final": "proxy",
    "auto_detect_interface": true,
    "rules": [
      { "protocol": ["dns"], "outbound": "dns-out" }
    ]
  }
}`, host, port, uuid, flow, sni, pbk, sid)

    return cfg, nil
}


func startSingBox(vless string) error {
    confDir := filepath.Join(os.TempDir(), "svpn")
    _ = os.MkdirAll(confDir, 0o755)
    confPath := filepath.Join(confDir, "sb.json")

    cfg, err := vlessToSingBox(vless)
    if err != nil { return err }
    if err := os.WriteFile(confPath, []byte(cfg), 0o600); err != nil {
        return err
    }

    bin := binSingBox()
    if _, err := os.Stat(bin); err != nil {
        return fmt.Errorf("sing-box missing at %s", bin)
    }
    cmd := exec.Command(bin, "run", "-c", confPath)
    cmd.Stdout = os.Stdout
    cmd.Stderr = os.Stderr
    if err := cmd.Start(); err != nil { return err }
    sbxCmd = cmd
    go func(){ _ = cmd.Wait() }()
    return nil
}

func stopSingBox() {
    if sbxCmd != nil && sbxCmd.Process != nil {
        _ = sbxCmd.Process.Kill()
    }
    sbxCmd = nil
}

func readCode() string {
    b, _ := os.ReadFile(filepath.Join(filepath.Dir(os.Args[0]), "code.txt"))
    return strings.TrimSpace(string(b))
}

func notify(title, msg string) {
    fmt.Println(title + ": " + msg)
}

func connect(code string) {
    if secret == "" || gatewayURL == "" {
        notify("SimpleVPN", "Set SVPN_GATEWAY and SVPN_SECRET environment variables")
        return
    }
    if err := acquireLock(code); err != nil {
        notify("Lock", err.Error()); return
    }
    var r struct{ Vless string `json:"vless"` }
    if curVLESS == "" {
        if err := postJSON("/fetchLatestVless", map[string]any{"code": code}, &r); err != nil {
            notify("Config", err.Error()); releaseLock(code); return
        }
        curVLESS = r.Vless
    }
    if err := startSingBox(curVLESS); err != nil {
        notify("Start", err.Error()); releaseLock(code); return
    }
    curCode = code
    heartbeatLoop(code)
    notify("SimpleVPN", "Connected")
}

func disconnect() {
    if curCode != "" { releaseLock(curCode) }
    if hbStop != nil { close(hbStop); hbStop = nil }
    stopSingBox()
    notify("SimpleVPN", "Disconnected")
    curCode = ""
}

func main() {
    if gatewayURL == "" { gatewayURL = "http://localhost:8787" }

    onReady := func() {
        systray.SetTitle("SimpleVPN")
        systray.SetTooltip("SimpleVPN Desktop")
        mConnect := systray.AddMenuItem("Connect", "Connect")
        mDisconnect := systray.AddMenuItem("Disconnect", "Disconnect")
        mQuit := systray.AddMenuItem("Quit", "Quit")

        go func() {
            for {
                select {
                case <-mConnect.ClickedCh:
                    code := readCode()
                    if code == "" { notify("SimpleVPN", "Put redeem code in code.txt next to the EXE"); continue }
                    connect(code)
                case <-mDisconnect.ClickedCh:
                    disconnect()
                case <-mQuit.ClickedCh:
                    disconnect()
                    systray.Quit()
                    return
                }
            }
        }()
    }
    onExit := func() { disconnect() }
    systray.Run(onReady, onExit)
}
