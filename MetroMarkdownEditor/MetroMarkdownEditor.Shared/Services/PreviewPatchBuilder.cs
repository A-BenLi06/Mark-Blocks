using System;
using System.Collections.Generic;
using System.Text;

namespace MetroMarkdownEditor.Services
{
    public sealed class PreviewSplice
    {
        public int Start;
        public int RemoveCount;
        public int InsertCount;
        public string Html;
    }

    /// <summary>Preserves unchanged DOM, including expensive rendered math and diagrams.</summary>
    public static class PreviewPatchBuilder
    {
        public static List<PreviewSplice> Build(IReadOnlyList<MarkdownBlock> previous, IReadOnlyList<MarkdownBlock> next)
        {
            var result = new List<PreviewSplice>();
            var oldCount = previous == null ? 0 : previous.Count;
            var prefix = 0;
            while (prefix < oldCount && prefix < next.Count && Equal(previous[prefix], next[prefix])) prefix++;
            var suffix = 0;
            while (suffix < oldCount - prefix && suffix < next.Count - prefix &&
                Equal(previous[oldCount - 1 - suffix], next[next.Count - 1 - suffix])) suffix++;

            if (oldCount == next.Count)
            {
                // Distant edits must not destroy the unchanged nodes between them.
                for (var i = prefix; i < next.Count - suffix;)
                {
                    if (Equal(previous[i], next[i])) { i++; continue; }
                    var start = i++;
                    while (i < next.Count - suffix && !Equal(previous[i], next[i])) i++;
                    result.Add(Create(next, start, i - start, i - start));
                }
            }
            else if (prefix + suffix != oldCount || prefix + suffix != next.Count)
                result.Add(Create(next, prefix, oldCount - prefix - suffix, next.Count - prefix - suffix));
            return result;
        }

        private static bool Equal(MarkdownBlock a, MarkdownBlock b)
        {
            return string.Equals(a.InnerHtml ?? a.Html, b.InnerHtml ?? b.Html, StringComparison.Ordinal);
        }

        private static PreviewSplice Create(IReadOnlyList<MarkdownBlock> blocks, int start, int remove, int insert)
        {
            var html = new StringBuilder();
            for (var i = start; i < start + insert; i++) html.Append(blocks[i].Html);
            return new PreviewSplice { Start = start, RemoveCount = remove, InsertCount = insert, Html = html.ToString() };
        }

        public static string Serialize(IReadOnlyList<PreviewSplice> patches)
        {
            var output = new StringBuilder("[");
            for (var i = 0; i < patches.Count; i++)
            {
                if (i != 0) output.Append(',');
                var p = patches[i];
                output.Append('[').Append(p.Start).Append(',').Append(p.RemoveCount).Append(',')
                    .Append(p.InsertCount).Append(",'").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(p.Html))).Append("']");
            }
            return output.Append(']').ToString();
        }
    }
}
