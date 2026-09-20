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
        public System.Collections.Generic.List<MarkdownColorRun> Runs { get; set; }
        public int NextRun { get; set; }
        public int ProtectedStart { get; set; }
        public int ProtectedEnd { get; set; }
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
            MarkdownHighlightRange previousRange, bool allowFocusedViewport = false)
        {
            if (editorBox == null || editorBox.Document == null)
            {
                return new MarkdownHighlightRange();
            }

            var doc = editorBox.Document;
            // Windows 8.1 RichEditBox exposes no composition lifecycle events.
            // A pause in pinyin is not an IME commit. Only viewport refreshes
            // may run while focused, and those exclude the active paragraph.
            var focused = editorBox.FocusState != FocusState.Unfocused;
            if (focused && !allowFocusedViewport) return previousRange;
            // Formatting would discard the user's redo branch. Wait for a new edit.
            if (doc.CanRedo()) return previousRange;
            var selectionStart = doc.Selection.StartPosition;
            var selectionEnd = doc.Selection.EndPosition;
            var protectedStart = 0;
            var protectedEnd = 0;
            if (focused)
            {
                // Exclude the whole active paragraph (or selected paragraphs),
                // including its terminator. Never move the live selection.
                var protectedRange = doc.GetRange(Math.Min(selectionStart, selectionEnd), Math.Max(selectionStart, selectionEnd));
                protectedRange.Expand(TextRangeUnit.Paragraph);
                protectedStart = protectedRange.StartPosition;
                protectedEnd = Math.Max(protectedStart + 1, protectedRange.EndPosition);
            }
            var bodyColor = GetBodyColor();
            var syntaxColor = GetSyntaxColor();
            var currentRange = GetVisibleRange(editorBox, selectionStart, selectionEnd);
            string text;
            doc.GetRange(currentRange.Start, currentRange.End).GetText(TextGetOptions.None, out text);
            text = (text ?? string.Empty).TrimEnd('\0').Replace('\r', '\n');
            if (previousRange.IsValid && previousRange.Start == currentRange.Start && previousRange.End == currentRange.End
                && previousRange.ProtectedStart == protectedStart && previousRange.ProtectedEnd == protectedEnd
                && previousRange.BodyColor.Equals(bodyColor) && previousRange.SyntaxColor.Equals(syntaxColor)
                && string.Equals(previousRange.Text, text, StringComparison.Ordinal)) return previousRange;
            // A plan may be partially applied over several idle ticks. Only a
            // completely applied plan can satisfy the cache hit above.
            var samePlan = previousRange.Runs != null && previousRange.Start == currentRange.Start
                && previousRange.ProtectedStart == protectedStart && previousRange.ProtectedEnd == protectedEnd
                && previousRange.End == currentRange.End && previousRange.BodyColor.Equals(bodyColor)
                && previousRange.SyntaxColor.Equals(syntaxColor) && previousRange.Text == text;
            if (samePlan) currentRange = previousRange;
            else
            {
                currentRange.IsValid = false;
                currentRange.Text = text;
                currentRange.BodyColor = bodyColor;
                currentRange.SyntaxColor = syntaxColor;
                currentRange.ProtectedStart = protectedStart;
                currentRange.ProtectedEnd = protectedEnd;
                try { currentRange.Runs = MarkdownSyntaxColorPlan.Build(text); }
                catch (RegexMatchTimeoutException) { return new MarkdownHighlightRange(); }
                currentRange.NextRun = 0;
            }
            var displayUpdatesBatched = false;
            var undoGroupOpen = false;
            try
            {
                doc.BatchDisplayUpdates();
                displayUpdatesBatched = true;
                doc.BeginUndoGroup();
                undoGroupOpen = true;
                var budget = System.Diagnostics.Stopwatch.StartNew();
                var offset = currentRange.Start;
                currentRange.NextRun = MarkdownSyntaxColorPlan.ApplyBatch(currentRange.Runs, currentRange.NextRun, run =>
                {
                    var color = run.IsSyntax ? syntaxColor : bodyColor;
                    // A mixed-color range cannot be compared as a single color.
                    MarkdownSyntaxColorPlan.ApplyOutsideProtectedRange(offset + run.Start, offset + run.Start + run.Length,
                        protectedStart, protectedEnd, (start, end) => doc.GetRange(start, end).CharacterFormat.ForegroundColor = color);
                }, () => budget.ElapsedMilliseconds >= 8);
                if (!focused && selectionStart == selectionEnd && !doc.Selection.CharacterFormat.ForegroundColor.Equals(bodyColor))
                    doc.Selection.CharacterFormat.ForegroundColor = bodyColor;
                currentRange.IsValid = currentRange.NextRun == currentRange.Runs.Count;
            }
            catch
            {
                // Native formatting may already have changed: never return an old
                // valid cache or reuse a progress cursor after failure.
                currentRange = new MarkdownHighlightRange();
            }
            finally
            {
                try
                {
                    if (undoGroupOpen) doc.EndUndoGroup();
                }
                catch { currentRange = new MarkdownHighlightRange(); }
                finally
                {
                    if (displayUpdatesBatched)
                    {
                        try { doc.ApplyDisplayUpdates(); }
                        catch { currentRange = new MarkdownHighlightRange(); }
                    }
                }
            }
            return currentRange;
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

        public static void PrepareForInput(RichEditBox editorBox)
        {
            // Called on focus entry, before a new composition, never on each
            // TextChanged/SelectionChanged. Do not recolor a selected text range.
            var doc = editorBox.Document;
            if (doc.CanRedo()) return;
            var selection = doc.Selection;
            if (selection.StartPosition == selection.EndPosition)
            {
                var color = GetBodyColor();
                if (!selection.CharacterFormat.ForegroundColor.Equals(color))
                    selection.CharacterFormat.ForegroundColor = color;
            }
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
            return root != null && root.RequestedTheme != ElementTheme.Default
                ? root.RequestedTheme == ElementTheme.Dark
                : Application.Current.RequestedTheme == ApplicationTheme.Dark;
        }
    }
}
