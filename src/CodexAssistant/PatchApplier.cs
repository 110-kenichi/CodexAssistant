using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CodexAssistant
{
    internal static class PatchApplier
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static string Text(byte[] bytes)
        {
            string text = Utf8.GetString(bytes);
            if (text.IndexOf('\0') >= 0) throw new InvalidOperationException("バイナリ・UTF-16ファイルは未対応です。UTF-8を使用してください。");
            if (text.StartsWith("\uFEFF", StringComparison.Ordinal)) text = text.Substring(1);
            if (text.Contains("\r\n") && text.Replace("\r\n", "").Contains("\n"))
                throw new InvalidOperationException("改行コードが混在したファイルは未対応です。");
            return text;
        }

        private static byte[] RestoreFormat(byte[] updated, byte[] original)
        {
            string text = Text(updated).Replace("\r\n", "\n");
            if (original != null)
            {
                if (Text(original).Contains("\r\n")) text = text.Replace("\n", "\r\n");
                if (original.Length >= 3 && original[0] == 239 && original[1] == 187 && original[2] == 191) text = "\uFEFF" + text;
            }
            return Utf8.GetBytes(text);
        }
        // Only ordinary text-file patches are accepted; git parses and checks the hunks.
        internal static string[] Paths(string root, string patch)
        {
            var paths = new List<string>();
            string current = null;
            bool inHunk = false, oldHeader = false, newHeader = false;
            foreach (string raw in patch.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    if (current != null && (!oldHeader || !newHeader || !inHunk)) throw new InvalidOperationException("不完全なdiffです。");
                    var match = Regex.Match(line, @"\Adiff --git a/(.+) b/\1\z");
                    if (!match.Success) throw new InvalidOperationException("リネーム・引用符付きパスは未対応です。");
                    current = match.Groups[1].Value;
                    SafePath(root, current);
                    if (paths.Contains(current, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("同じファイルのdiffが重複しています。");
                    paths.Add(current); inHunk = oldHeader = newHeader = false;
                    if (paths.Count > 100) throw new InvalidOperationException("一度に適用できるのは100ファイルまでです。");
                }
                else if (current == null) { if (line.Length != 0) throw new InvalidOperationException("git形式のdiffが必要です。"); }
                else if (line.StartsWith("@@ ", StringComparison.Ordinal))
                {
                    if (!oldHeader || !newHeader) throw new InvalidOperationException("diffのファイルヘッダーがありません。");
                    inHunk = true;
                }
                else if (inHunk)
                {
                    if (line.Length > 0 && " +-\\".IndexOf(line[0]) < 0) throw new InvalidOperationException("未対応のdiff形式です。");
                }
                else if (line == "--- a/" + current || line == "--- /dev/null") oldHeader = true;
                else if (line == "+++ b/" + current || line == "+++ /dev/null") newHeader = true;
                else if (Regex.IsMatch(line, @"\Aindex [0-9a-f]+\.\.[0-9a-f]+(?: 100644)?\z")) { }
                else if (line == "new file mode 100644" || line == "deleted file mode 100644" || line.Length == 0) { }
                else throw new InvalidOperationException("バイナリ・リンク・属性変更を含むdiffは未対応です。");
            }
            if (paths.Count == 0 || !oldHeader || !newHeader || !inHunk) throw new InvalidOperationException("適用できるテキストdiffがありません。");
            return paths.ToArray();
        }

        internal static string SafePath(string root, string relative)
        {
            var parts = relative.Split('/');
            if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ") ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                p.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(p, @"\A(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)", RegexOptions.IgnoreCase)))
                throw new InvalidOperationException("許可されないファイルパスです: " + relative);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Solution外には適用できません。");
            string cursor = full;
            while (cursor != null)
            {
                if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("リンクを経由するパスには適用できません。");
                cursor = Path.GetDirectoryName(cursor);
            }
            return full;
        }

        internal static async Task<string[]> ApplyAsync(string root, string patch, CancellationToken token, Func<Action, Task> commitOnUi = null)
        {
            string[] paths = Paths(root, patch);
            string stage = Path.Combine(Path.GetTempPath(), "CodexAssistant-patch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            var original = new Dictionary<string, byte[]>();
            var written = new List<string>();
            try
            {
                var init = await CliRunner.RunAsync("git.exe", "init --quiet", stage, null, token).ConfigureAwait(false);
                if (init.ExitCode != 0) throw new InvalidOperationException("差分検証用のGit環境を作成できません。\n" + init.Error);
                long totalBytes = 0;
                foreach (string path in paths)
                {
                    string full = SafePath(root, path);
                    if (File.Exists(full) && new FileInfo(full).Length > 10 * 1024 * 1024) throw new InvalidOperationException("10MBを超えるファイルは適用対象外です。");
                    original[path] = File.Exists(full) ? File.ReadAllBytes(full) : null;
                    totalBytes += original[path]?.Length ?? 0;
                    if (totalBytes > 20 * 1024 * 1024) throw new InvalidOperationException("適用対象の合計は20MBまでです。");
                    if (original[path] != null)
                    {
                        string copy = SafePath(stage, path);
                        Directory.CreateDirectory(Path.GetDirectoryName(copy));
                        File.WriteAllBytes(copy, Utf8.GetBytes(Text(original[path]).Replace("\r\n", "\n")));
                    }
                }
                string patchFile = Path.Combine(stage, ".git", "proposal.patch");
                File.WriteAllText(patchFile, patch.Replace("\r\n", "\n"), Utf8);
                var result = await CliRunner.RunAsync("git.exe", "-c core.autocrlf=false apply --no-index --whitespace=nowarn " + CliRunner.Quote(patchFile), stage, null, token).ConfigureAwait(false);
                if (result.ExitCode != 0) throw new InvalidOperationException("差分が一致せず適用できません。最新のコードで提案を作り直してください。\n" + result.Error);
                token.ThrowIfCancellationRequested();
                Action commit = () =>
                {
                    token.ThrowIfCancellationRequested();
                    foreach (string path in paths)
                    {
                        string full = SafePath(root, path);
                        byte[] now = File.Exists(full) ? File.ReadAllBytes(full) : null;
                        if ((now == null) != (original[path] == null) || (now != null && !now.SequenceEqual(original[path])))
                            throw new InvalidOperationException("適用準備中にファイルが変更されました: " + path);
                    }
                    try
                    {
                        // Finish this short batch without cancellation once writing starts.
                        foreach (string path in paths)
                        {
                            string full = SafePath(root, path), copy = SafePath(stage, path);
                            written.Add(path);
                            if (File.Exists(copy))
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(full));
                                File.WriteAllBytes(full, RestoreFormat(File.ReadAllBytes(copy), original[path]));
                            }
                            else if (File.Exists(full)) File.Delete(full);
                        }
                    }
                    catch
                    {
                        foreach (string path in written)
                        {
                            string full = SafePath(root, path);
                            if (original[path] == null) { if (File.Exists(full)) File.Delete(full); }
                            else File.WriteAllBytes(full, original[path]);
                        }
                        throw;
                    }
                };
                if (commitOnUi == null) commit();
                else await commitOnUi(commit).ConfigureAwait(false);
                return paths;
            }
            finally { Directory.Delete(stage, true); }
        }
    }
}
