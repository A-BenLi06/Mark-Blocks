using System;
using System.Text.RegularExpressions;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace MetroMarkdownEditor.Services
{
    public struct MarkdownHighlightRange
    {
        public int Start { get; set; }
        public int End { get; set; }
        public bool IsValid { get; set; }
        public string Text { get; set; }
        public Color BodyColor { get; set; }
        public Color SyntaxColor { get; set; }
    }

    /// <summary>
    /// Virtualized Markdown syntax highlighting for RichEditBox. Only the visible
    /// text plus a small editing margin owns syntax-format spans, keeping native
    /// layout cost bounded independently of document length.
    /// </summary>
    public static class MarkdownHighlightingHelper
    {
        private const int EditingMarginCharacters = 1024;
        private const int MaximumHighlightCharacters = 8192;

        public static MarkdownHighlightRange HighlightVisible(
            RichEditBox editorBox,
            MarkdownHighlightRange previousRange)
        {
            if (editorBox == null || editorBox.Document == null)
            {
                return new MarkdownHighlightRange();
            }

            var doc = editorBox.Document;
            // Windows 8.1 RichEditBox exposes no composition lifecycle events.
            // A pause in pinyin is not an IME commit. Never alter native format
            // runs while the editor owns focus; callers retry after LostFocus.
            if (editorBox.FocusState != FocusState.Unfocused) return previousRange;
            // Formatting would discard the user's redo branch. Wait for a new edit.
            if (doc.CanRedo()) return previousRange;
            var selectionStart = doc.Selection.StartPosition;
            var selectionEnd = doc.Selection.EndPosition;
            var bodyColor = GetBodyColor();
            var syntaxColor = GetSyntaxColor();
            var currentRange = GetVisibleRange(editorBox, selectionStart, selectionEnd);
            string text;
            doc.GetRange(currentRange.Start, currentRange.End).GetText(TextGetOptions.None, out text);
            text = (text ?? string.Empty).TrimEnd('\0').Replace('\r', '\n');
            if (previousRange.IsValid && previousRange.Start == currentRange.Start && previousRange.End == currentRange.End
                && previousRange.BodyColor.Equals(bodyColor) && previousRange.SyntaxColor.Equals(syntaxColor)
                && string.Equals(previousRange.Text, text, StringComparison.Ordinal)) return previousRange;
            var displayUpdatesBatched = false;
            var undoGroupOpen = false;

            try
            {
                doc.BatchDisplayUpdates();
                displayUpdatesBatched = true;
                doc.BeginUndoGroup();
                undoGroupOpen = true;

                if (previousRange.IsValid
                    && (previousRange.Start != currentRange.Start || previousRange.End != currentRange.End))
                {
                    try
                    {
                        doc.GetRange(previousRange.Start, previousRange.End)
                            .CharacterFormat.ForegroundColor = bodyColor;
                    }
                    catch
                    {
                    }
                }

                if (text.Length > 0)
                {
                    currentRange.End = currentRange.Start + text.Length;
                    doc.GetRange(currentRange.Start, currentRange.End)
                        .CharacterFormat.ForegroundColor = bodyColor;
                    ApplySyntaxRanges(doc, text, currentRange.Start, syntaxColor);
                }

                // Range formatting does not require reselecting the document.
                // In particular, never recolor a Ctrl+A selection or move an IME caret.
                if (selectionStart == selectionEnd && !doc.Selection.CharacterFormat.ForegroundColor.Equals(bodyColor))
                    doc.Selection.CharacterFormat.ForegroundColor = bodyColor;
                currentRange.Text = text;
                currentRange.BodyColor = bodyColor;
                currentRange.SyntaxColor = syntaxColor;
                currentRange.IsValid = true;
                return currentRange;
            }
            catch
            {
                return previousRange;
            }
            finally
            {
                if (undoGroupOpen)
                {
                    doc.EndUndoGroup();
                }

                if (displayUpdatesBatched)
                {
                    doc.ApplyDisplayUpdates();
                }
            }
        }

        private static MarkdownHighlightRange GetVisibleRange(
            RichEditBox editorBox,
            int selectionStart,
            int selectionEnd)
        {
            var doc = editorBox.Document;
            var start = Math.Max(0, Math.Min(selectionStart, selectionEnd));
            var end = start;

            try
            {
                var x = Math.Max(1, editorBox.Padding.Left + 1);
                var topY = Math.Max(1, editorBox.Padding.Top + 1);
                var bottomY = Math.Max(topY, editorBox.ActualHeight - editorBox.Padding.Bottom - 1);
                var top = doc.GetRangeFromPoint(new Point(x, topY), PointOptions.ClientCoordinates);
                var bottom = doc.GetRangeFromPoint(new Point(x, bottomY), PointOptions.ClientCoordinates);

                if (top != null && bottom != null)
                {
                    start = Math.Min(top.StartPosition, bottom.StartPosition);
                    end = Math.Max(top.EndPosition, bottom.EndPosition);
                }
            }
            catch
            {
            }

            var result = doc.GetRange(start, Math.Min(end, start + MaximumHighlightCharacters));
            result.MoveStart(TextRangeUnit.Character, -EditingMarginCharacters);
            result.MoveEnd(TextRangeUnit.Character, EditingMarginCharacters);
            result.MoveStart(TextRangeUnit.Line, -1);
            result.MoveEnd(TextRangeUnit.Line, 1);

            return new MarkdownHighlightRange
            {
                Start = Math.Max(0, result.StartPosition),
                End = Math.Max(0, Math.Min(result.EndPosition, result.StartPosition + MaximumHighlightCharacters)),
                IsValid = true
            };
        }

        private static void ApplySyntaxRanges(
            ITextDocument doc,
            string text,
            int offset,
            Color syntaxColor)
        {
            var options = RegexOptions.Multiline;
            var budget = System.Diagnostics.Stopwatch.StartNew();

            foreach (Match match in Regex.Matches(text, @"(?:^|\n)(#{1,6})(?=\s)", options))
            {
                if (budget.ElapsedMilliseconds >= 8) return;
                ApplyGroup(doc, match.Groups[1], offset, syntaxColor);
            }

            foreach (Match match in Regex.Matches(text, @"(!?\[)(.*?)(\])(\(.*?\))", options, TimeSpan.FromMilliseconds(20)))
            {
                if (budget.ElapsedMilliseconds >= 8) return;
                ApplyGroup(doc, match.Groups[1], offset, syntaxColor);
                ApplyGroup(doc, match.Groups[3], offset, syntaxColor);
                ApplyGroup(doc, match.Groups[4], offset, syntaxColor);
            }

            foreach (Match match in Regex.Matches(text, @"(\*\*|__|\*|_|~~)(.+?)\1", options, TimeSpan.FromMilliseconds(20)))
            {
                if (budget.ElapsedMilliseconds >= 8) return;
                var marker = match.Groups[1];
                ApplyGroup(doc, marker, offset, syntaxColor);
                var rightStart = offset + match.Index + match.Length - marker.Length;
                doc.GetRange(rightStart, rightStart + marker.Length)
                    .CharacterFormat.ForegroundColor = syntaxColor;
            }

            foreach (Match match in Regex.Matches(text, @"(?:^|\n)(>\s)", options))
            {
                if (budget.ElapsedMilliseconds >= 8) return;
                ApplyGroup(doc, match.Groups[1], offset, syntaxColor);
            }

            foreach (Match match in Regex.Matches(text, @"(?:^|\n)(\-\-\-|\*\*\*)$", options))
            {
                if (budget.ElapsedMilliseconds >= 8) return;
                ApplyGroup(doc, match.Groups[1], offset, syntaxColor);
            }
        }

        private static void ApplyGroup(ITextDocument doc, Group group, int offset, Color color)
        {
            if (group == null || !group.Success || group.Length == 0) return;
            var start = offset + group.Index;
            doc.GetRange(start, start + group.Length).CharacterFormat.ForegroundColor = color;
        }

        private static Color GetBodyColor()
        {
            return IsDarkTheme() ? Colors.White : Colors.Black;
        }

        private static Color GetSyntaxColor()
        {
            return IsDarkTheme()
                ? Color.FromArgb(255, 120, 120, 120)
                : Color.FromArgb(255, 150, 150, 150);
        }

        private static bool IsDarkTheme()
        {
            var root = Window.Current.Content as FrameworkElement;
            return root != null
                ? root.RequestedTheme == ElementTheme.Dark
                : Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }
}
