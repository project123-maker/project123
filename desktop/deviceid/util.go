package deviceid

import (
    "crypto/rand"
    "fmt"
    "os"
    "path/filepath"
    "strings"
)

func parseBetween(s, a, b string) string {
    i := strings.Index(s, a)
    if i < 0 {
        return ""
    }
    i += len(a)
    j := strings.Index(s[i:], b)
    if j < 0 {
        return ""
    }
    return s[i : i+j]
}

func randomUUID() string {
    var b [16]byte
    _, _ = rand.Read(b[:])
    // set version and variant
    b[6] = (b[6] & 0x0f) | 0x40
    b[8] = (b[8] & 0x3f) | 0x80
    return fmt.Sprintf("%08x-%04x-%04x-%04x-%012x",
        b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}

// persistedUUID stores/loads a fallback ID at %AppData%\simplevpn\deviceid (Windows) or ~/.config/simplevpn/deviceid (others).
func persistedUUID() string {
    dir, _ := os.UserConfigDir()
    path := filepath.Join(dir, "simplevpn", "deviceid")
    _ = os.MkdirAll(filepath.Dir(path), 0o755)
    if b, err := os.ReadFile(path); err == nil && len(b) > 0 {
        return string(b)
    }
    u := randomUUID()
    _ = os.WriteFile(path, []byte(u), 0o600)
    return u
}
