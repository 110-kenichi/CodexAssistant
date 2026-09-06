using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAssistant
{
    internal sealed class ProcessResult
    {
        public int ExitCode;
        public string Output;
        public string Error;
    }
    internal static class CliRunner
    {
        internal static string Quote(string value)
        {
            var b = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                b.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                b.Append(c); slashes = 0;
            }
            b.Append('\\', slashes * 2);
            return b.Append('"').ToString();
        }

        public static async Task<string> AskAsync(string exe, string root, string prompt, CancellationToken token, string model = null)
        {
            if (!string.Equals(Path.GetExtension(exe), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Optionsでnative codex.exeを指定してください（.cmd/.ps1は未対応）。");
            var output = Path.Combine(Path.GetTempPath(), "CodexAssistant-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                string selected = ModelSelection.Normalize(model);
                string modelArgument = selected.Length == 0 ? "" : " --model " + Quote(selected);
                var result = await RunAsync(exe, "exec --sandbox read-only --skip-git-repo-check --color never" + modelArgument + " --output-last-message " + Quote(output) + " -", root, prompt, token);
                if (result.ExitCode != 0)
                    throw new InvalidOperationException($"Codex exited with {result.ExitCode}.\n{result.Error}\n{result.Output}");
                if (!File.Exists(output)) throw new InvalidOperationException("Codex did not produce a final response.\n" + result.Error);
                return File.ReadAllText(output, Encoding.UTF8);
            }
            finally { if (File.Exists(output)) File.Delete(output); }
        }

        public static async Task<ProcessResult> RunAsync(string exe, string args, string root, string input, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo(exe, args)
                {
                    WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                process.Start();
                using (token.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            // taskkill /T terminates CLI child commands as well on .NET Framework.
                            using (var killer = Process.Start(new ProcessStartInfo("taskkill.exe", "/PID " + process.Id + " /T /F")
                            { UseShellExecute = false, CreateNoWindow = true }))
                            {
                                if (killer == null || !killer.WaitForExit(3000) || killer.ExitCode != 0)
                                    process.Kill();
                            }
                        }
                    }
                    catch (Exception) { try { process.Kill(); } catch (Exception) { } }
                }))
                {
                    var stdout = ReadBoundedAsync(process.StandardOutput);
                    var stderr = ReadBoundedAsync(process.StandardError);
                    using (var writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false)))
                        await writer.WriteAsync(input ?? "").ConfigureAwait(false);
                    await Task.Run(() => process.WaitForExit()).ConfigureAwait(false);
                    var result = new ProcessResult { ExitCode = process.ExitCode, Output = await stdout, Error = await stderr };
                    token.ThrowIfCancellationRequested();
                    return result;
                }
            }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            var b = new StringBuilder();
            var buffer = new char[4096];
            int n;
            while ((n = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                if (b.Length < 64000) b.Append(buffer, 0, Math.Min(n, 64000 - b.Length));
            return b.ToString();
        }

        public static async Task<string> GitContextAsync(string root, CancellationToken token)
        {
            var b = new StringBuilder();
            foreach (var args in new[] { "status --short", "diff --no-ext-diff --no-textconv -- .", "diff --cached --no-ext-diff --no-textconv -- ." })
            {
                var result = await RunAsync("git.exe", args, root, null, token);
                b.AppendLine("git " + args);
                b.AppendLine(TextUtil.Limit(result.ExitCode == 0 ? result.Output : result.Error, 10000));
            }
            return b.ToString();
        }
    }
}
