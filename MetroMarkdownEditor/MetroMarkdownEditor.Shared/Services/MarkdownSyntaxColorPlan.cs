using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MetroMarkdownEditor.Services
{
    public sealed class MarkdownColorRun
    {
        public int Start;
        public int Length;
        public bool IsSyntax;
    }

    // Finish parsing before touching native formatting. Runs include body text so
    // stale/inherited colors are repaired without first erasing all syntax colors.
    public static class MarkdownSyntaxColorPlan
    {
        public static void ApplyOutsideProtectedRange(int start, int end, int protectedStart, int protectedEnd,
            Action<int, int> apply)
        {
            if (protectedEnd <= protectedStart || end <= protectedStart || start >= protectedEnd)
            {
                if (end > start) apply(start, end);
                return;
            }
            if (start < protectedStart) apply(start, protectedStart);
            if (end > protectedEnd) apply(protectedEnd, end);
        }

        public static int ApplyBatch(System.Collections.Generic.IList<MarkdownColorRun> runs, int next,
            Action<MarkdownColorRun> apply, Func<bool> shouldYield)
        {
            while (next < runs.Count)
            {
                apply(runs[next]);
                next++;
                if (shouldYield()) break;
            }
            return next;
        }

        public static List<MarkdownColorRun> Build(string text)
        {
            var syntax = new bool[text.Length];
            Action<int, int> mark = (start, length) =>
            {
                for (var i = start; i < start + length; i++) syntax[i] = true;
            };
            Action<string, int[]> pattern = (expression, groups) =>
            {
                foreach (Match match in Regex.Matches(text, expression, RegexOptions.Multiline, TimeSpan.FromMilliseconds(20)))
                    foreach (var index in groups)
                    {
                        var group = match.Groups[index];
                        if (group.Success) mark(group.Index, group.Length);
                    }
            };
            pattern(@"(?:^|\n)(#{1,6})(?=\s)", new[] { 1 });
            pattern(@"(!?\[)(.*?)(\])(\(.*?\))", new[] { 1, 3, 4 });
            foreach (Match match in Regex.Matches(text, @"(\*\*|__|\*|_|~~)(.+?)\1", RegexOptions.Multiline, TimeSpan.FromMilliseconds(20)))
            {
                var marker = match.Groups[1];
                mark(marker.Index, marker.Length);
                mark(match.Index + match.Length - marker.Length, marker.Length);
            }
            pattern(@"(?:^|\n)(>\s)", new[] { 1 });
            pattern(@"(?:^|\n)(\-\-\-|\*\*\*)$", new[] { 1 });
            var runs = new List<MarkdownColorRun>();
            for (var start = 0; start < syntax.Length;)
            {
                var end = start + 1;
                while (end < syntax.Length && syntax[end] == syntax[start]) end++;
                runs.Add(new MarkdownColorRun { Start = start, Length = end - start, IsSyntax = syntax[start] });
                start = end;
            }
            return runs;
        }
    }
}
