using System;
using System.Text.RegularExpressions;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Services
{
    /// <summary>
    /// Shared markdown syntax highlighting helper for RichEditBox on Windows/Phone.
    /// </summary>
    public static class MarkdownHighlightingHelper
    {
        public static void Highlight(RichEditBox editorBox)
        {
            if (editorBox == null || editorBox.Document == null) return;

            ITextDocument doc = editorBox.Document;
            string text = string.Empty;
            doc.GetText(TextGetOptions.None, out text);

            if (string.IsNullOrEmpty(text)) return;

            try
            {
                doc.BatchDisplayUpdates();

                bool isDark = false;
                var rootFrame = Window.Current.Content as FrameworkElement;

                if (rootFrame != null)
                {
                    isDark = rootFrame.RequestedTheme == ElementTheme.Dark;
                }
                else
                {
                    isDark = Application.Current.RequestedTheme == ApplicationTheme.Dark;
                }

                Color bodyColor;
                Color syntaxColor;

                if (isDark)
                {
                    bodyColor = Colors.White;
                    syntaxColor = Color.FromArgb(255, 120, 120, 120);
                }
                else
                {
                    bodyColor = Colors.Black;
                    syntaxColor = Color.FromArgb(255, 150, 150, 150);
                }

                int start = doc.Selection.StartPosition;
                int end = doc.Selection.EndPosition;

                ITextRange fullRange = doc.GetRange(0, text.Length);
                fullRange.CharacterFormat.ForegroundColor = bodyColor;

                RegexOptions options = RegexOptions.Multiline;

                // Headers
                MatchCollection headers = Regex.Matches(text, @"(?:^|\r)(#{1,6})(?=\s)", options);
                foreach (Match m in headers)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // Links / images
                MatchCollection links = Regex.Matches(text, @"(!?\[)(.*?)(\])(\(.*?\))", options);
                foreach (Match m in links)
                {
                    ITextRange r1 = doc.GetRange(m.Groups[1].Index, m.Groups[1].Index + m.Groups[1].Length);
                    r1.CharacterFormat.ForegroundColor = syntaxColor;

                    ITextRange r3 = doc.GetRange(m.Groups[3].Index, m.Groups[3].Index + m.Groups[3].Length);
                    r3.CharacterFormat.ForegroundColor = syntaxColor;

                    ITextRange r4 = doc.GetRange(m.Groups[4].Index, m.Groups[4].Index + m.Groups[4].Length);
                    r4.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // Bold / italic / strikethrough
                MatchCollection styles = Regex.Matches(text, @"(\*\*|__|\*|_|~~)(.+?)\1", options);
                foreach (Match m in styles)
                {
                    Group leftSign = m.Groups[1];
                    ITextRange rLeft = doc.GetRange(leftSign.Index, leftSign.Index + leftSign.Length);
                    rLeft.CharacterFormat.ForegroundColor = syntaxColor;

                    int rightSignStart = m.Index + m.Length - leftSign.Length;
                    ITextRange rRight = doc.GetRange(rightSignStart, rightSignStart + leftSign.Length);
                    rRight.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // Blockquote
                MatchCollection quotes = Regex.Matches(text, @"(?:^|\r)(>\s)", options);
                foreach (Match m in quotes)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                // Horizontal rule
                MatchCollection hrs = Regex.Matches(text, @"(?:^|\r)(\-\-\-|\*\*\*)$", options);
                foreach (Match m in hrs)
                {
                    Group g = m.Groups[1];
                    ITextRange range = doc.GetRange(g.Index, g.Index + g.Length);
                    range.CharacterFormat.ForegroundColor = syntaxColor;
                }

                doc.Selection.SetRange(start, end);
                doc.Selection.CharacterFormat.ForegroundColor = bodyColor;
            }
            catch
            {
                // ignore
            }
            finally
            {
                doc.ApplyDisplayUpdates();
            }
        }
    }
}
