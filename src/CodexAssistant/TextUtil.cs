namespace CodexAssistant
{
    internal static class TextUtil
    {
        internal static string Limit(string value, int max) => value.Length <= max ? value : value.Substring(0, max) + "\n[truncated]";
    }
}
