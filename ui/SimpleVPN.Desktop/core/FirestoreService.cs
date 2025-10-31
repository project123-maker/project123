using Google.Cloud.Firestore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SimpleVPN.Desktop
{
    public sealed class FirestoreService
    {
        private readonly FirestoreDb _db;
        public FirestoreService(string projectId) => _db = FirestoreDb.Create(projectId);

        public DocumentReference CodeDoc(string code, string coll = "codes") => _db.Collection(coll).Document(code);

        public async Task<T?> GetAsync<T>(DocumentReference doc, CancellationToken ct) where T : class
        {
            var snap = await doc.GetSnapshotAsync(ct);
            return snap.Exists ? snap.ConvertTo<T>() : null;
        }

        public async Task<object?> GetFieldAsync(DocumentReference doc, string field, CancellationToken ct)
        {
            var snap = await doc.GetSnapshotAsync(ct);
            return snap.Exists && snap.ContainsField(field) ? snap.GetValue<object>(field) : null;
        }

        public async Task RunTransaction(Func<Transaction, Task> body, CancellationToken ct)
        {
            await _db.RunTransactionAsync(async t => { await body(t); return 0; }, ct);
        }
    }
}
