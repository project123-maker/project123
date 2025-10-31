package deviceid

import (
    "os/exec"
    "runtime"
    
    "golang.org/x/sys/windows/registry"
)

// baseMachineID tries OS-native IDs, then falls back to a persisted UUID.
func baseMachineID() string {
    switch runtime.GOOS {
    case "windows":
        if s := winMachineGUID(); s != "" {
            return s
        }
    case "darwin":
        if s := macIOPlatformUUID(); s != "" {
            return s
        }
    }
    return persistedUUID()
}

func winMachineGUID() string {
    k, err := registry.OpenKey(registry.LOCAL_MACHINE, `SOFTWARE\Microsoft\Cryptography`, registry.QUERY_VALUE|registry.WOW64_64KEY)
    if err != nil {
        return ""
    }
    defer k.Close()
    s, _, err := k.GetStringValue("MachineGuid")
    if err != nil {
        return ""
    }
    return s
}

func macIOPlatformUUID() string {
    if runtime.GOOS != "darwin" {
        return ""
    }
    out, err := exec.Command("ioreg", "-rd1", "-c", "IOPlatformExpertDevice").CombinedOutput()
    if err != nil {
        return ""
    }
    return parseBetween(string(out), "IOPlatformUUID\" = \"", "\"")
}
