using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodexAssistant
{
    internal static class ModelSelection
    {
        internal const string DefaultLabel = "CLIの既定設定を使用";

        internal static string Normalize(string value)
        {
            value = (value ?? "").Trim();
            if (value == DefaultLabel || value.Length == 0) return "";
            if (value.Length > 200 || !Regex.IsMatch(value, @"\A[A-Za-z0-9][A-Za-z0-9._:/-]*\z"))
                throw new ArgumentException("モデルIDには英数字と . _ : / - を使用してください（最大200文字）。");
            return value;
        }

        internal static string Remember(string history, string selected)
        {
            var models = new List<string>();
            if (!string.IsNullOrEmpty(selected)) models.Add(Normalize(selected));
            foreach (var value in (history ?? "").Split('\n'))
            {
                try
                {
                    var model = Normalize(value);
                    if (model.Length > 0 && !models.Contains(model) && models.Count < 10) models.Add(model);
                }
                catch (ArgumentException) { /* Ignore obsolete or malformed saved entries. */ }
            }
            return string.Join("\n", models);
        }

        internal static IEnumerable<string> Choices(string history)
        {
            yield return DefaultLabel;
            var seen = new HashSet<string>();
            foreach (var model in (Remember(history, "") + "\ngpt-6-astra\ngpt-5.6-terra\ngpt-5.6-luna").Split('\n'))
                if (model.Length > 0 && seen.Add(model)) yield return model;
        }
    }
}
