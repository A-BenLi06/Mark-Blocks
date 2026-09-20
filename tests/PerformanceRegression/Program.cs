using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MetroMarkdownEditor.Services;
using MetroMarkdownEditor.ViewModels;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

class Program
{
    public static string SharedRoot;
    static int checks;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }
    static string Body(MarkdownRenderResult result) { return string.Concat(result.Blocks.Select(b => b.InnerHtml)); }

    static async Task Main(string[] args)
    {
        SharedRoot = Path.GetFullPath("MetroMarkdownEditor/MetroMarkdownEditor.Shared");
        Directory.CreateDirectory("tests/PerformanceRegression/artifacts");
        // The standalone file fixture needs the same font assets as the app package.
        Directory.CreateDirectory("tests/PerformanceRegression/artifacts/fonts");
        foreach (var font in Directory.GetFiles(Path.Combine(SharedRoot, "Assets/KaTex/fonts")))
            File.Copy(font, Path.Combine("tests/PerformanceRegression/artifacts/fonts", Path.GetFileName(font)), true);
        Check(EditorPerformancePolicy.NormalizeText("a\r\r\0", true) == "a\n", "preserve user blank line; strip one native terminator");
        var plain = "plain\ntext";
        Check(object.ReferenceEquals(plain, EditorPerformancePolicy.NormalizeText(plain)), "normalized text avoids copy");
        Check(EditorPerformancePolicy.NormalizeText("\r\na\rb\n") == "\na\nb\n", "mixed line endings");
        Check(EditorSettingsService.NormalizeContentForSave("\tfoo\n  \tbar\n", true, "  ", true) == "  foo\r\n  \tbar\r\n", "save indentation and CRLF semantics");
        Check(EditorPerformancePolicy.PreviewDelay(3000000, 10) > EditorPerformancePolicy.PreviewDelay(100, 10), "large documents receive longer quiet period");

        var renderer = new MarkdownRenderService();
        var before = await renderer.RenderMarkdownAsync("# 标题\n\nfirst\n\nlast\n", x => x);
        var after = await renderer.RenderMarkdownAsync("\n# 标题\n\nfirst\n\nlast\n", x => x);
        Check(PreviewPatchBuilder.Build(before.Blocks, after.Blocks).Count == 0, "source line shifts do not rebuild content");
        var inserted = await renderer.RenderMarkdownAsync("new\n\n# 标题\n\nfirst\n\nlast\n", x => x);
        var patch = PreviewPatchBuilder.Build(before.Blocks, inserted.Blocks);
        Check(patch.Count == 1 && patch[0].RemoveCount == 0 && patch[0].InsertCount == 1, "prefix insertion preserves every old node");
        patch = PreviewPatchBuilder.Build(inserted.Blocks, before.Blocks);
        Check(patch.Count == 1 && patch[0].RemoveCount == 1 && patch[0].InsertCount == 0, "prefix deletion preserves suffix");
        var changed = await renderer.RenderMarkdownAsync("# different\n\nfirst\n\nchanged\n", x => x);
        Check(PreviewPatchBuilder.Build(before.Blocks, changed.Blocks).Count == 2, "distant changes preserve intervening nodes");
        Check(PreviewPatchBuilder.Build(before.Blocks, new List<MarkdownBlock>()).Single().RemoveCount == 3, "delete entire document");
        Check(PreviewPatchBuilder.Build(null, new List<MarkdownBlock>()).Count == 0, "empty initial document");
        var references = await renderer.RenderMarkdownAsync("[link][ref]\n\n[ref]: https://example.com\n", x => x);
        Check(Body(references).Contains("href=\"https://example.com\""), "whole-document reference link resolution");
        var headings = await renderer.RenderMarkdownAsync("# same\n\n# same\n", x => x);
        Check(headings.Outline.Count == 2 && headings.Outline[0].AnchorId != headings.Outline[1].AnchorId, "duplicate heading anchors");
        var fencedHeading = await renderer.RenderMarkdownAsync("```\n# not a heading\n```\n", x => x);
        Check(fencedHeading.Outline.Count == 0, "no fallback whole-text scan for documents without headings");
        var cancellationExceptions = 0;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observeCancellation = (sender, e) =>
        {
            if (e.Exception is OperationCanceledException)
                System.Threading.Interlocked.Increment(ref cancellationExceptions);
        };
        AppDomain.CurrentDomain.FirstChanceException += observeCancellation;
        try
        {
            using (var cancelled = new System.Threading.CancellationTokenSource())
            {
                cancelled.Cancel();
                Check(await renderer.RenderMarkdownAsync("cancelled", x => x, cancelled.Token) == null,
                    "pre-cancelled render is discarded");
                Check(await renderer.RenderMarkdownAsync("", x => x, cancelled.Token) == null,
                    "cancelled empty render cannot clear the preview");
            }
            for (var cancelAt = 1; cancelAt <= 3; cancelAt++)
            {
                using (var cancelled = new System.Threading.CancellationTokenSource())
                {
                    var visited = 0;
                    var result = await renderer.RenderMarkdownAsync("first\n\nsecond\n\nlast\n", html =>
                    {
                        if (++visited == cancelAt) cancelled.Cancel();
                        return html;
                    }, cancelled.Token);
                    Check(result == null && visited == cancelAt,
                        "cancellation at block " + cancelAt + " stops work without publishing partial content");
                }
            }
            Check(cancellationExceptions == 0, "routine cancellation raises no first-chance exception");
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= observeCancellation; }
        Check(Body(await renderer.RenderMarkdownAsync("latest", x => x)).Contains("latest"),
            "latest render succeeds after cancelled requests");
        var expectedFailure = new InvalidOperationException("render failure probe");
        try
        {
            await renderer.RenderMarkdownAsync("failure", x => { throw expectedFailure; });
            throw new Exception("render failure swallowed");
        }
        catch (InvalidOperationException ex)
        {
            Check(object.ReferenceEquals(ex, expectedFailure), "real rendering failures still propagate");
        }

        // Randomized splice reconstruction validates index handling and duplicates.
        var random = new Random(42);
        for (var round = 0; round < 1000; round++)
        {
            Func<List<MarkdownBlock>> sample = () => Enumerable.Range(0, random.Next(30)).Select(i => new MarkdownBlock { InnerHtml = random.Next(8).ToString(), Html = "<div></div>" }).ToList();
            var a = sample(); var b = sample(); var splices = PreviewPatchBuilder.Build(a, b);
            var actual = a.Select(x => x.InnerHtml).ToList();
            foreach (var splice in splices.AsEnumerable().Reverse())
            {
                actual.RemoveRange(splice.Start, splice.RemoveCount);
                actual.InsertRange(splice.Start, b.Skip(splice.Start).Take(splice.InsertCount).Select(x => x.InnerHtml));
            }
            if (!actual.SequenceEqual(b.Select(x => x.InnerHtml))) throw new Exception("random splice " + round);
        }
        Check(true, "1000 randomized splice sequences");

        var view = new WebView();
        await renderer.LoadSkeletonAsync(view, "", ElementTheme.Light, before.Blocks);
        Check(view.Html.Contains("window.__mdApplyPatches"), "skeleton installs patch bridge");
        Check(view.Html.Length < 150000, "plain document does not embed heavy libraries");
        File.WriteAllText("tests/PerformanceRegression/artifacts/preview.html", view.Html);
        var scriptMethod = typeof(MarkdownRenderService).GetMethod("BuildPatchScript", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var cases = new[] { scriptMethod.Invoke(null, new object[] { null, before.Blocks }), scriptMethod.Invoke(null, new object[] { before.Blocks, inserted.Blocks }), scriptMethod.Invoke(null, new object[] { inserted.Blocks, before.Blocks }) };
        File.WriteAllText("tests/PerformanceRegression/artifacts/patches.json", System.Text.Json.JsonSerializer.Serialize(cases));

        var rich = await renderer.RenderMarkdownAsync("```csharp\nvar x = 1;\n```\n\n$$x^2$$\n\n```mermaid\ngraph TD; A-->B;\n```\n", x => x);
        Check(renderer.NeedsLibraries(rich.Blocks), "first rich content requests missing libraries");
        await renderer.LoadSkeletonAsync(view, "", ElementTheme.Light, rich.Blocks);
        Check(!renderer.NeedsLibraries(rich.Blocks), "loaded libraries are reused");
        File.WriteAllText("tests/PerformanceRegression/artifacts/rich-preview.html", view.Html);
        File.WriteAllText("tests/PerformanceRegression/artifacts/rich-patch.json", System.Text.Json.JsonSerializer.Serialize(scriptMethod.Invoke(null, new object[] { null, rich.Blocks })));

        var document = new DocumentViewModel { Content = "before" };
        var entered = new TaskCompletionSource<bool>(); var release = new TaskCompletionSource<bool>();
        FileIO.WriteOverride = async (file, text) => { entered.TrySetResult(true); await release.Task; };
        var saving = document.SaveAsync(new StorageFile { Path = "test.md" });
        await entered.Task;
        document.MarkEditorChanged(); // newer native buffer has not been snapshotted yet
        release.SetResult(true);
        await saving;
        Check(document.IsDirty, "in-flight save cannot clear pending native edit");
        document.CommitEditorText("new text");
        FileIO.WriteOverride = (file, text) => Task.CompletedTask;
        await document.SaveAsync();
        Check(!document.IsDirty, "saving committed latest text clears dirty");
        entered = new TaskCompletionSource<bool>(); release = new TaskCompletionSource<bool>();
        FileIO.WriteOverride = async (file, text) => { entered.TrySetResult(true); await release.Task; };
        saving = document.SaveAsync(); await entered.Task;
        document.Content = "updated during save";
        release.SetResult(true); await saving;
        Check(document.IsDirty, "in-flight save cannot clear newer model content");
        var inFlight = 0; var maximumInFlight = 0;
        FileIO.WriteOverride = async (file, text) =>
        {
            var count = System.Threading.Interlocked.Increment(ref inFlight);
            maximumInFlight = Math.Max(count, maximumInFlight);
            await Task.Delay(20);
            System.Threading.Interlocked.Decrement(ref inFlight);
        };
        await Task.WhenAll(document.SaveAsync(), document.SaveAsync());
        Check(maximumInFlight == 1, "same-document writes are serialized");
        Console.WriteLine("Completed " + checks + " checks (plus 1000 randomized cases). OS UI/IME require device QA.");
    }
}
