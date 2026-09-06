using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodexAssistant;

internal static class PatchTests
{
    internal static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "Codex-patch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string patch = "diff --git a/source.cs b/source.cs\n--- a/source.cs\n+++ b/source.cs\n@@ -1 +1 @@\n-old\n+new\n";
        try
        {
            File.WriteAllText(Path.Combine(root, "source.cs"), "old\n");
            await PatchApplier.ApplyAsync(root, patch, CancellationToken.None);
            if (File.ReadAllText(Path.Combine(root, "source.cs")) != "new\n") throw new Exception("Patch edit failed: " + BitConverter.ToString(File.ReadAllBytes(Path.Combine(root, "source.cs"))));
            bool rejected = false;
            try { await PatchApplier.ApplyAsync(root, patch, CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || File.ReadAllText(Path.Combine(root, "source.cs")) != "new\n") throw new Exception("Conflict must preserve source");
            foreach (string path in new[] { "../outside.cs", ".git/config", "a:stream", "CON", "sub/../../outside" })
            {
                rejected = false;
                try { PatchApplier.Paths(root, patch.Replace("source.cs", path)); }
                catch (InvalidOperationException) { rejected = true; }
                if (!rejected) throw new Exception("Unsafe path accepted");
            }
            string add = "diff --git a/sub/new.cs b/sub/new.cs\nnew file mode 100644\n--- /dev/null\n+++ b/sub/new.cs\n@@ -0,0 +1 @@\n+hello\n";
            await PatchApplier.ApplyAsync(root, add, CancellationToken.None);
            if (File.ReadAllText(Path.Combine(root,"sub/new.cs")) != "hello\n") throw new Exception("New file failed");
            File.WriteAllText(Path.Combine(root, "source.cs"), "old\r\n", new System.Text.UTF8Encoding(true));
            await PatchApplier.ApplyAsync(root, patch, CancellationToken.None);
            var bytes = File.ReadAllBytes(Path.Combine(root, "source.cs"));
            if (BitConverter.ToString(bytes) != "EF-BB-BF-6E-65-77-0D-0A") throw new Exception("UTF-8 BOM / CRLF not preserved");
            string deletion = "diff --git a/sub/new.cs b/sub/new.cs\ndeleted file mode 100644\n--- a/sub/new.cs\n+++ /dev/null\n@@ -1 +0,0 @@\n-hello\n";
            await PatchApplier.ApplyAsync(root, deletion, CancellationToken.None);
            if (File.Exists(Path.Combine(root, "sub/new.cs"))) throw new Exception("Deletion failed");
            File.WriteAllText(Path.Combine(root, "source.cs"), "old\n");
            File.WriteAllText(Path.Combine(root, "other.cs"), "different\n");
            rejected = false;
            try { await PatchApplier.ApplyAsync(root, patch + patch.Replace("source.cs", "other.cs"), CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || File.ReadAllText(Path.Combine(root, "source.cs")) != "old\n") throw new Exception("Batch conflict modified source");
            rejected = false;
            try
            {
                await PatchApplier.ApplyAsync(root, patch, CancellationToken.None, commit =>
                {
                    File.WriteAllText(Path.Combine(root, "source.cs"), "external edit\n");
                    commit();
                    return Task.CompletedTask;
                });
            }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected || File.ReadAllText(Path.Combine(root, "source.cs")) != "external edit\n") throw new Exception("Concurrent edit overwritten");
            Console.WriteLine("PASS: patch edit/add/delete, BOM/CRLF preservation, batch conflict, concurrent edit, path validation.");
        }
        finally { Directory.Delete(root, true); }
    }
}

