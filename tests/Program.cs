using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexAssistant;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        if (args.Length > 0)
        {
            if (args[0] == "echo") { Console.Write(args[1]); return 0; }
            if (args[0] == "wait") { Thread.Sleep(30000); return 0; }
            if (args[0] == "flood") { Console.Error.Write(new string('E', 100000)); Console.Write(new string('O', 100000)); return 7; }
            if (args[0] == "exec")
            {
                var prompt = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8).ReadToEnd();
                int index = Array.IndexOf(args, "--output-last-message");
                int modelIndex = Array.IndexOf(args, "--model");
                string selected = modelIndex < 0 ? "DEFAULT" : args[modelIndex + 1];
                File.WriteAllText(args[index + 1], selected + "\n" + Environment.CurrentDirectory + "\n" + prompt, new UTF8Encoding(false));
                return 0;
            }
        }
        try { TestAsync().GetAwaiter().GetResult(); Console.WriteLine("PASS: CLI integration and model selection checks (mock CLI, no account/network)."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static async Task TestAsync()
    {
        await PatchTests.Run();
        var exe = Assembly.GetExecutingAssembly().Location;
        var root = Path.Combine(Path.GetTempPath(), "Codex tests 日本語 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var value in new[] { "", "space 日本語", "quote\"slash\\", "trailing\\" })
            {
                var result = await CliRunner.RunAsync(exe, "echo " + CliRunner.Quote(value), root, null, CancellationToken.None);
                Check(result.ExitCode == 0 && result.Output == value, "Argument round-trip: " + value);
            }
            var answer = await CliRunner.AskAsync(exe, root, "この関数を直して\n次の行", CancellationToken.None);
            Check(answer == "DEFAULT\n" + root + "\nこの関数を直して\n次の行", "Default model / UTF-8 stdin / cwd / final response");
            foreach (var model in new[] { "gpt-6-astra", "gpt-5.6-terra", "provider/custom-model:latest", "  gpt-5.6-luna  " })
            {
                var explicitAnswer = await CliRunner.AskAsync(exe, root, "test", CancellationToken.None, model);
                Check(explicitAnswer.StartsWith(model.Trim() + "\n"), "Explicit model passed to CLI");
            }
            var defaultAnswer = await CliRunner.AskAsync(exe, root, "test", CancellationToken.None, ModelSelection.DefaultLabel);
            Check(defaultAnswer.StartsWith("DEFAULT\n"), "Default selection omits --model");
            foreach (var invalid in new[] { "--flag", "a\nb", "a b", "a\"b", new string('a', 201) })
            {
                bool invalidRejected = false;
                try { await CliRunner.AskAsync(exe, root, "test", CancellationToken.None, invalid); }
                catch (ArgumentException) { invalidRejected = true; }
                Check(invalidRejected, "Invalid model rejected");
            }
            Check(ModelSelection.Remember("old\nnew\nold\nbad id", "new") == "new\nold", "Recent model order / dedup / validation");
            Check(ModelSelection.Remember("old", "") == "old", "Default selection preserves recent custom models");
            var flood = await CliRunner.RunAsync(exe, "flood", root, null, CancellationToken.None);
            Check(flood.ExitCode == 7 && flood.Output.Length == 64000 && flood.Error.Length == 64000, "Concurrent bounded streams and exit code");
            using (var cts = new CancellationTokenSource(300))
            {
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                bool canceled = false;
                try { await CliRunner.RunAsync(exe, "wait", root, null, cts.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Check(canceled && elapsed.Elapsed < TimeSpan.FromSeconds(8), "Prompt process cancellation");
            }
            bool rejected = false;
            try { await CliRunner.AskAsync("codex.cmd", root, "test", CancellationToken.None); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Shell wrapper must not be executed");
        }
        finally { Directory.Delete(root, false); }
    }
}
