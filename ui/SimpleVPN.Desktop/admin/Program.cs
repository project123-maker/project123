using Google.Cloud.Firestore;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("Usage: codes add|revoke <code> [vless]"); return 1; }

        var projectId = "YOUR_FIREBASE_PROJECT_ID";
        var coll = "codes";
        var db = FirestoreDb.Create(projectId);
        var doc = db.Collection(coll).Document(args[1]);

        switch (args[0].ToLowerInvariant())
        {
            case "add":
                if (args.Length < 3) { Console.WriteLine("add requires vless"); return 1; }
                await doc.SetAsync(new
                {
                    active = true,
                    vless = args[2],
                    @lock = (object?)null
                }, SetOptions.MergeAll);
                Console.WriteLine("Added/updated code.");
                return 0;

            case "revoke":
                await doc.UpdateAsync(new { active = false });
                Console.WriteLine("Revoked code.");
                return 0;

            default:
                Console.WriteLine("Unknown command");
                return 1;
        }
    }
}
