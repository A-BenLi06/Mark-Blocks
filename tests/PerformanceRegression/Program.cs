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
        if (args.Length == 2 && args[0] == "--benchmark")
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var source = await File.ReadAllTextAsync(args[1]);
            Console.WriteLine("Read ms=" + timer.ElapsedMilliseconds + ", chars=" + source.Length);
            var service = new MarkdownRenderService();
            for (var pass = 0; pass < 3; pass++)
            {
                timer.Restart();
                var result = await service.RenderMarkdownAsync(source, x => x);
                Console.WriteLine("Parse pass=" + pass + ", ms=" + timer.ElapsedMilliseconds + ", blocks=" + result.Blocks.Count + ", html chars=" + result.Blocks.Sum(b => b.Html.Length) + ", libraries=" + result.Blocks.Aggregate(0, (flags, b) => flags | b.LibraryFeatures));
                if (pass == 0)
                {
                    var benchmarkView = new WebView();
                    await service.LoadSkeletonAsync(benchmarkView, "", ElementTheme.Light, result.Blocks);
                    File.WriteAllText("tests/PerformanceRegression/artifacts/benchmark.html", benchmarkView.Html);
                    var method = typeof(MarkdownRenderService).GetMethod("BuildPatchScript", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    File.WriteAllText("tests/PerformanceRegression/artifacts/benchmark-patch.js", (string)method.Invoke(null, new object[] { null, result.Blocks }));
                }
            }
            return;
        }
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
        await ImageCacheTests.RunAsync();
        var colorText = "# 标题\n正文 **粗体** [链接](url)\n> 引用\n---\n";
        var visibleWrites = new List<string>();
        MarkdownSyntaxColorPlan.ApplyOutsideProtectedRange(0, 100, 30, 60, (start, end) => visibleWrites.Add(start + ":" + end));
        Check(visibleWrites.SequenceEqual(new[] { "0:30", "60:100" }), "scroll colors both sides without touching active paragraph");
        visibleWrites.Clear();
        MarkdownSyntaxColorPlan.ApplyOutsideProtectedRange(35, 50, 30, 60, (start, end) => visibleWrites.Add(start + ":" + end));
        Check(visibleWrites.Count == 0, "composition paragraph receives no formatting writes");
        MarkdownSyntaxColorPlan.ApplyOutsideProtectedRange(100, 150, 30, 60, (start, end) => visibleWrites.Add(start + ":" + end));
        Check(visibleWrites.Single() == "100:150", "offscreen caret does not block new visible content");
        visibleWrites.Clear();
        MarkdownSyntaxColorPlan.ApplyOutsideProtectedRange(0, 100, 0, 0, (start, end) => visibleWrites.Add(start + ":" + end));
        Check(visibleWrites.Single() == "0:100", "focus loss permits full visible recoloring");
        var colorRuns = MarkdownSyntaxColorPlan.Build(colorText);
        var colors = new bool[colorText.Length];
        var nextOffset = 0;
        foreach (var run in colorRuns)
        {
            Check(run.Start == nextOffset && run.Length > 0, "color plan contiguous run at " + nextOffset);
            for (var i = run.Start; i < run.Start + run.Length; i++) colors[i] = run.IsSyntax;
            nextOffset += run.Length;
        }
        Check(nextOffset == colorText.Length, "color plan covers all body and syntax characters");
        Check(colors[0] && !colors[2] && !colors[colorText.IndexOf("粗体")] && colors[colorText.IndexOf("**")],
            "syntax markers are muted without recoloring heading and emphasis body");
        Check(colors[colorText.IndexOf("[")] && !colors[colorText.IndexOf("链接")] && colors[colorText.IndexOf("(url)")],
            "link markers and destination preserve link label color");
        var bodyRuns = MarkdownSyntaxColorPlan.Build("中文正文\n222222");
        Check(bodyRuns.Count == 1 && !bodyRuns[0].IsSyntax, "plain body produces one color-repair run");
        Check(MarkdownSyntaxColorPlan.Build("").Count == 0, "empty color plan completes without native writes");
        var denseRuns = MarkdownSyntaxColorPlan.Build(string.Concat(Enumerable.Repeat("**字** ", 1000)));
        Check(denseRuns.Sum(r => r.Length) == 6000 && denseRuns.Last().Start + denseRuns.Last().Length == 6000,
            "dense plan is complete before native application begins");
        var applied = new List<MarkdownColorRun>();
        var cursor = MarkdownSyntaxColorPlan.ApplyBatch(denseRuns, 0, applied.Add, () => true);
        Check(cursor == 1 && cursor < denseRuns.Count, "budget yield retains incomplete progress");
        while (cursor < denseRuns.Count)
            cursor = MarkdownSyntaxColorPlan.ApplyBatch(denseRuns, cursor, applied.Add, () => true);
        Check(applied.SequenceEqual(denseRuns), "resumed color batches apply every run once without erasing earlier work");
        var writesAfterCompletion = 0;
        MarkdownSyntaxColorPlan.ApplyBatch(denseRuns, cursor, r => writesAfterCompletion++, () => false);
        Check(writesAfterCompletion == 0, "completed color plan requires no more native writes");
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
