using Google.Cloud.Firestore;
using System;

namespace SimpleVPN.Desktop
{
    [FirestoreData]
    public sealed class CodeDoc
    {
        [FirestoreProperty] public bool active { get; set; }
        [FirestoreProperty] public string vless { get; set; } = "";
        [FirestoreProperty] public LockInfo? @lock { get; set; }
        [FirestoreProperty] public string? owner { get; set; }    // optional
    }

    [FirestoreData]
    public sealed class LockInfo
    {
        [FirestoreProperty] public string deviceId { get; set; } = "";
        [FirestoreProperty] public string platform { get; set; } = "windows";
        [FirestoreProperty] public Timestamp since { get; set; }
        [FirestoreProperty] public Timestamp lastSeen { get; set; }
    }
}
