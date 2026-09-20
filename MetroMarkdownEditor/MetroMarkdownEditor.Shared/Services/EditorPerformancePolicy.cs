using System;
using System.Text;

namespace MetroMarkdownEditor.Services
{
    /// <summary>Shared, platform-independent limits for work scheduled after input.</summary>
    public static class EditorPerformancePolicy
    {
        public const int InputQuietMilliseconds = 700;

        public static int PreviewDelay(int characters, double previousWorkMilliseconds)
        {
            var sizeDelay = characters >= 2000000 ? 1200 : characters >= 250000 ? 900 : InputQuietMilliseconds;
            return Math.Min(1500, Math.Max(sizeDelay, (int)Math.Min(1500, previousWorkMilliseconds * 2)));
        }

        public static string NormalizeText(string text, bool richEditTerminalMarker = false)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var length = text.Length;
            if (richEditTerminalMarker)
            {
                while (length > 0 && text[length - 1] == '\0') length--;
                if (length > 0 && text[length - 1] == '\r') length--;
            }
            var firstCr = text.IndexOf('\r', 0, length);
            if (firstCr < 0) return length == text.Length ? text : text.Substring(0, length);
            var result = new StringBuilder(length);
            result.Append(text, 0, firstCr);
            for (var i = firstCr; i < length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    result.Append('\n');
                    if (i + 1 < length && text[i + 1] == '\n') i++;
                }
                else result.Append(c);
            }
            return result.ToString();
        }
    }
}
