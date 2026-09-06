using System;
using System.IO;
using System.Text;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace CodexAssistant
{
    internal sealed class IdeContext
    {
        public string Root;
        public string Text;
    }
    internal static class ContextCollector
    {
        // All DTE/COM access is deliberately confined to the UI thread.
        public static IdeContext Capture(DTE2 dte, bool includeErrors)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var solution = dte?.Solution?.FullName;
            if (string.IsNullOrEmpty(solution) || !File.Exists(solution))
                throw new InvalidOperationException("保存済みのSolutionを開いてください。");
            var b = new StringBuilder();
            b.AppendLine("Solution: " + solution);
            var doc = dte.ActiveDocument;
            if (doc != null)
            {
                b.AppendLine("ActiveDocument: " + doc.FullName);
                try { b.AppendLine("Project: " + doc.ProjectItem?.ContainingProject?.FullName); }
                catch (Exception ex) { b.AppendLine("Project unavailable: " + ex.Message); }
                try
                {
                    var text = doc.Object("TextDocument") as TextDocument;
                    if (text != null)
                    {
                        var cursor = text.Selection.ActivePoint;
                        b.AppendLine($"Cursor: line {cursor.Line}, column {cursor.LineCharOffset}");
                        b.AppendLine("Unsaved changes: " + !doc.Saved);
                        var start = text.StartPoint.CreateEditPoint();
                        start.MoveToLineAndOffset(Math.Max(1, cursor.Line - 60), 1);
                        var end = text.StartPoint.CreateEditPoint();
                        end.MoveToLineAndOffset(Math.Min(text.EndPoint.Line, cursor.Line + 60), 1);
                        end.EndOfLine();
                        b.AppendLine("Editor buffer excerpt (may differ from disk), starting line " + start.Line + ":");
                        b.AppendLine(Limit(start.GetText(end), 18000));
                    }
                }
                catch (Exception ex) { b.AppendLine("Editor context unavailable: " + ex.Message); }
            }
            else b.AppendLine("ActiveDocument: none");
            b.AppendLine("Open documents:");
            int count = 0;
            foreach (Document open in dte.Documents)
            {
                if (++count > 60) { b.AppendLine("[remaining documents omitted]"); break; }
                b.AppendLine(open.FullName + (open.Saved ? "" : " [unsaved]"));
            }
            if (includeErrors)
            {
                b.AppendLine("Error List (best effort, first 40):");
                try
                {
                    var items = dte.ToolWindows.ErrorList.ErrorItems;
                    for (int i = 1; i <= Math.Min(40, items.Count); i++)
                    {
                        var item = items.Item(i);
                        b.AppendLine($"{item.ErrorLevel} {item.FileName}:{item.Line}:{item.Column} {Limit(item.Description, 500)}");
                    }
                }
                catch (Exception ex) { b.AppendLine("Unavailable: " + ex.Message); }
            }
            return new IdeContext { Root = Path.GetDirectoryName(solution), Text = b.ToString() };
        }
        internal static string Limit(string value, int max) => TextUtil.Limit(value, max);
    }
}
