package deviceid

import (
    "crypto/sha256"
    "encoding/hex"
)

// MachineID returns a stable, hashed device ID (prefix "d-").
// Uses Windows MachineGuid or macOS IOPlatformUUID; falls back to a persisted UUID.
func MachineID() string {
    id := baseMachineID()
    sum := sha256.Sum256([]byte(id))
    return "d-" + hex.EncodeToString(sum[:])[:32]
}
