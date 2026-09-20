using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using Windows.Storage; 
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.SmartyPants;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MetroMarkdownEditor.Services
{
    public class MarkdownRenderResult
    {
        public string Html { get; set; }
        public IReadOnlyList<MarkdownBlock> Blocks { get; set; }
        public IReadOnlyList<MarkdownOutlineItem> Outline { get; set; }
    }

    public class MarkdownOutlineItem
    {
        public string Title { get; set; }
        public int Level { get; set; }
        public int Line { get; set; }
        public string AnchorId { get; set; }
        public int BlockIndex { get; set; }
        public Thickness Indent
        {
            get { return new Thickness(Math.Max(0, Level - 1) * 16, 0, 0, 0); }
        }
    }

    public class MarkdownBlock
    {
        public int Index { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Text { get; set; }
        public string InnerHtml { get; set; }
        public int LibraryFeatures { get; set; }
        public string Html { get; set; }
    }

    public class MarkdownRenderService
    {
        private MarkdownPipeline _pipeline;
        private int _loadedLibraryFeatures;
        private string _pipelineSignature;

        private static string _cachedJs = null;
        private static string _cachedCssDark = null;
        private static string _cachedCssLight = null;
        // KaTeX 本地缓存
        private static string _cachedKatexCss = null;
        private static string _cachedKatexJs = null;
        private static string _cachedKatexAutoRender = null;
        // Mermaid 本地缓存 + IE11 polyfills
        private static string _cachedMermaidJs = null;
        private static string _cachedEs6Promise = null;
        // REMOVED: _cachedCoreJs - not used (causes IE11 conflicts)
        private static string _cachedUrlPolyfill = null;
        // REMOVED: _cachedRegeneratorRuntime - not needed for Mermaid v7

        public MarkdownRenderService()
        {
            RebuildPipeline();
        }

        private MarkdownPipeline GetPipeline()
        {
            var signature = MarkdownSettingsService.Instance.BuildSignature() + "|" + EditorSettingsService.Instance.EnableEmojiAutocomplete;
            if (_pipeline == null || _pipelineSignature != signature)
            {
                RebuildPipeline();
            }

            return _pipeline;
        }

        private void RebuildPipeline()
        {
            var markdownSettings = MarkdownSettingsService.Instance;
            var editorSettings = EditorSettingsService.Instance;
            var builder = new MarkdownPipelineBuilder();

            if (!markdownSettings.StrictMode)
            {
                builder.UseAbbreviations();
                builder.UseCitations();
                builder.UseCustomContainers();
                builder.UseDefinitionLists();
                builder.UseFigures();
                builder.UseFooters();
                builder.UseFootnotes();
                builder.UseGridTables();
                builder.UseListExtras();
                builder.UseGenericAttributes();
                builder.UseYamlFrontMatter();
            }

            builder.UsePipeTables(new PipeTableOptions());
            builder.UseTaskLists();
            builder.UseAutoIdentifiers(AutoIdentifierOptions.GitHub);
            builder.UseEmphasisExtras(BuildEmphasisOptions(markdownSettings));

            if (markdownSettings.AutoLinks)
            {
                builder.UseAutoLinks();
            }

            if (editorSettings.EnableEmojiAutocomplete)
            {
                builder.UseEmojiAndSmiley();
            }

            if (markdownSettings.ShouldUseMathematics)
            {
                builder.UseMathematics();
                if (!markdownSettings.InlineMath)
                {
                    builder.InlineParsers.RemoveAll(p => p.GetType().Name == "MathInlineParser");
                }
            }

            if (markdownSettings.Diagrams)
            {
                builder.UseDiagrams();
            }

            if (markdownSettings.ShouldUseSmartyPants)
            {
                builder.UseSmartyPants(new SmartyPantOptions());
            }

            if (markdownSettings.ShouldUseSoftlineBreakAsHardlineBreak)
            {
                builder.UseSoftlineBreakAsHardlineBreak();
            }

            _pipeline = builder.Build();
            _pipelineSignature = markdownSettings.BuildSignature() + "|" + editorSettings.EnableEmojiAutocomplete;
        }

        private static EmphasisExtraOptions BuildEmphasisOptions(MarkdownSettingsService settings)
        {
            var options = EmphasisExtraOptions.Strikethrough;
            if (settings.Subscript) options |= EmphasisExtraOptions.Subscript;
            if (settings.Superscript) options |= EmphasisExtraOptions.Superscript;
            if (settings.Highlight) options |= EmphasisExtraOptions.Marked;
            return options;
        }

        private async Task<string> ReadAssetFileAsync(string path)
        {
            try
            {
                var uri = new Uri("ms-appx:///" + path);
                var file = await StorageFile.GetFileFromApplicationUriAsync(uri);
                return await FileIO.ReadTextAsync(file);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading asset {path}: {ex.Message}");
                return $"/* Error loading {path} */";
            }
        }

        public bool NeedsLibraries(IReadOnlyList<MarkdownBlock> blocks)
        {
            return (GetLibraryFeatures(blocks) & ~_loadedLibraryFeatures) != 0;
        }

        private static int GetLibraryFeatures(IReadOnlyList<MarkdownBlock> blocks)
        {
            var features = 0;
            if (blocks != null) foreach (var block in blocks) features |= block.LibraryFeatures;
            return features;
        }

        public async Task LoadSkeletonAsync(WebView webView, string inlineCss, ElementTheme theme, IReadOnlyList<MarkdownBlock> blocks = null)
        {
            if (webView == null) return;
            _loadedLibraryFeatures |= GetLibraryFeatures(blocks);
            var needCode = (_loadedLibraryFeatures & 1) != 0;
            var needMath = (_loadedLibraryFeatures & 2) != 0;
            var needDiagrams = (_loadedLibraryFeatures & 4) != 0;

            if (needCode && _cachedJs == null)
                _cachedJs = await ReadAssetFileAsync("Assets/highlight.js");

            if (theme == ElementTheme.Dark && _cachedCssDark == null)
                _cachedCssDark = await ReadAssetFileAsync("Assets/atom-one-dark.css");

            if (theme == ElementTheme.Light && _cachedCssLight == null)
                _cachedCssLight = await ReadAssetFileAsync("Assets/atom-one-light.css");

            // 加载本地 KaTeX
            if (needMath && _cachedKatexCss == null)
                _cachedKatexCss = await ReadAssetFileAsync("Assets/KaTex/katex.min.css");
            if (needMath && _cachedKatexJs == null)
                _cachedKatexJs = await ReadAssetFileAsync("Assets/KaTex/katex.min.js");
            if (needMath && _cachedKatexAutoRender == null)
                _cachedKatexAutoRender = await ReadAssetFileAsync("Assets/KaTex/contrib/auto-render.min.js");

            // 加载本地 Mermaid (with ES6 polyfills for IE11)
            if (needDiagrams && _cachedEs6Promise == null)
                _cachedEs6Promise = await ReadAssetFileAsync("Assets/Mermaid/es6-promise.auto.min.js");
            // REMOVED: core-js causes "Function.prototype.toString" errors in IE11
            // if (_cachedCoreJs == null)
            //     _cachedCoreJs = await ReadAssetFileAsync("Assets/Mermaid/core.min.js");
            if (needDiagrams && _cachedUrlPolyfill == null)
                _cachedUrlPolyfill = await ReadAssetFileAsync("Assets/Mermaid/url-polyfill.min.js");
            // REMOVED: regenerator-runtime not needed for Mermaid v7
            // if (_cachedRegeneratorRuntime == null)
            //     _cachedRegeneratorRuntime = await ReadAssetFileAsync("Assets/Mermaid/regenerator-runtime.js");
            if (needDiagrams && _cachedMermaidJs == null)
                _cachedMermaidJs = await ReadAssetFileAsync("Assets/Mermaid/mermaid7.min.js"); // 确保这里是 v7 版本

            string currentCssContent = (theme == ElementTheme.Dark) ? _cachedCssDark : _cachedCssLight;
            var markdownSettings = MarkdownSettingsService.Instance;

            // 颜色变量
            var bodyColor = theme == ElementTheme.Dark ? "#e6e6e6" : "#24292f";
            var bgColor = theme == ElementTheme.Dark ? "#1e1e1e" : "#ffffff";
            var borderColor = "#dfe2e5"; 
            var codeWhiteSpace = markdownSettings.AutoWrapLongLines ? "pre-wrap" : "pre";
            var codeWordBreak = markdownSettings.AutoWrapLongLines ? "break-word" : "normal";

            // 字号配置
            string baseFontSize = "20px";
            string codeFontSize = "16px";
            string lineHeight = "1.6";
            string containerPadding = "24px";

#if WINDOWS_PHONE_APP
            baseFontSize = "16px";
            codeFontSize = "13px";
            lineHeight = "1.8";
            containerPadding = "14px";
            bgColor = theme == ElementTheme.Dark ? "#1d1d1d" : "#ffffff"; // WP uses darker bg
#endif

            var sb = new StringBuilder();

            sb.Append("<!DOCTYPE html><html><head>");
            sb.Append("<meta http-equiv='Content-Type' content='text/html; charset=utf-8' />");
            sb.Append("<meta http-equiv='X-UA-Compatible' content='IE=edge' />");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no' />");
#if WINDOWS_PHONE_APP
            sb.Append("<meta http-equiv='Content-Security-Policy' content=\"default-src * 'unsafe-inline' 'unsafe-eval'; img-src * data: blob:;\" />");
#endif

            // --- 样式注入 ---
            sb.Append("<style>");
            
            // 1. 注入高亮库 CSS
            sb.Append(currentCssContent);

            // 2. 注入自定义基础样式
            sb.Append("html, body { margin: 0; min-height: 100%; }");
            sb.Append("body { ");
            sb.Append($"font-family: 'Segoe UI', sans-serif; line-height: {lineHeight}; padding: {containerPadding}; ");
            sb.Append($"font-size: {baseFontSize};");
            sb.Append("color: " + bodyColor + "; background-color: " + bgColor + "; ");
            sb.Append("word-wrap: break-word; overflow-wrap: break-word; box-sizing: border-box; min-height: 100vh;");
#if WINDOWS_PHONE_APP
            // WP: Enable hyphenation for better text flow
            sb.Append("-ms-hyphens: auto; hyphens: auto;");
#endif
            sb.Append("}");
            sb.Append("#content { min-height: calc(100vh - 48px); box-sizing: border-box; padding-bottom: 44px; }");

#if WINDOWS_PHONE_APP
            // WP: Advanced IE text justification + hyphenation for paragraphs
            sb.Append("p { ");
            sb.Append("text-align: justify; ");
            sb.Append("-ms-text-justify: inter-ideograph; "); // IE's advanced alignment algorithm
            sb.Append("text-justify: inter-ideograph; ");
            sb.Append("-ms-hyphens: auto; hyphens: auto; "); // Enable word breaking with hyphens
            sb.Append("-ms-word-break: break-all; "); // Allow breaking long words
            sb.Append("margin: 0.8em 0; ");
            sb.Append("}");
#else
            sb.Append("p { text-align: justify; -ms-text-justify: inter-word; text-justify: inter-word; margin: 0.8em 0; }");
#endif
            sb.Append("h1, h2, h3, h4, h5, h6 { font-family: 'Segoe UI', sans-serif; font-weight: 600; margin-top: 1.2em; margin-bottom: 0.5em; }");
#if WINDOWS_PHONE_APP
            // WP: Custom link color
            sb.Append("a { color: #63b0f2; text-decoration: none; }");
            sb.Append("a:hover, a:active { text-decoration: underline; }");
#else
            sb.Append("a { color: #0969da; text-decoration: none; }");
            sb.Append("a:hover { text-decoration: underline; }");
#endif
            sb.Append("img { max-width: 100%; height: auto; display: block; margin: 10px 0; border-radius: 4px; }");
            sb.Append("pre { margin: 1em 0; padding: 0; text-align: left; }");
            sb.Append($"code {{ font-family: 'Consolas', 'Courier New', monospace; font-size: {codeFontSize}; }}");
            sb.Append(".hljs { border-radius: 0; padding: 0.8em; overflow-x: auto; display: block; white-space: " + codeWhiteSpace + "; word-break: " + codeWordBreak + "; }");
            sb.Append("pre code { white-space: " + codeWhiteSpace + "; word-break: " + codeWordBreak + "; }");
            if (markdownSettings.DisplayLineNumbersForCodeFences)
            {
                sb.Append("pre.line-numbered code { display: table; width: 100%; counter-reset: code-line; }");
                sb.Append("pre.line-numbered .code-line { display: table-row; }");
                sb.Append("pre.line-numbered .line-no { display: table-cell; width: 3em; padding-right: 1em; text-align: right; color: #8b949e; user-select: none; opacity: .7; }");
                sb.Append("pre.line-numbered .line-code { display: table-cell; }");
            }
            if (markdownSettings.IndentFirstLineOfParagraphs)
            {
                sb.Append("p { text-indent: 2em; }");
            }
            if (markdownSettings.VisibleLineBreaks)
            {
                sb.Append("br:after { content: '\\21B5'; color: #8b949e; opacity: .7; font-size: .85em; }");
            }

            // 表格与引用
            sb.Append(".table-wrapper { overflow-x: auto; margin: 16px 0; border: none; }");
            sb.Append("table { border-collapse: collapse; width: 100%; border-style: hidden; font-size: 0.9em; }");
            sb.Append("th, td { border: none; padding: 8px 12px; white-space: nowrap; }");
            sb.Append("th { border-bottom: 1px solid " + borderColor + "; font-weight: 600; text-align: left; }");
            sb.Append("tr:nth-child(2n) { background-color: rgba(127,127,127,0.1); }");
            sb.Append("blockquote { border-left: 4px solid " + borderColor + "; padding: 0 1em; color: #6a737d; margin: 10px 0; }");
            sb.Append(".alert { padding: .65em 1em; border-radius: 4px; }");
            sb.Append(".alert-title { margin: 0 0 .35em 0; font-weight: 600; text-transform: uppercase; letter-spacing: 0; }");
            sb.Append(".alert-note { border-left-color: #0969da; background-color: rgba(9, 105, 218, 0.1); color: inherit; }");
            sb.Append(".alert-tip, .alert-important { border-left-color: #1a7f37; background-color: rgba(26, 127, 55, 0.1); color: inherit; }");
            sb.Append(".alert-warning { border-left-color: #9a6700; background-color: rgba(154, 103, 0, 0.1); color: inherit; }");
            sb.Append(".alert-caution { border-left-color: #cf222e; background-color: rgba(207, 34, 46, 0.1); color: inherit; }");
            sb.Append(".code-lang-label { position: absolute; top: 0; right: 0; padding: 4px 8px; font-family: 'Segoe UI', sans-serif; font-size: 11px; color: #abb2bf; background-color: rgba(0,0,0,0.3); border-bottom-left-radius: 4px; text-transform: lowercase; user-select: none; }");
            sb.Append(".math { overflow-x: auto; }");

#if WINDOWS_PHONE_APP
            // WP: Metro Hub style horizontal columns (optional, use .metro-columns class)
            sb.Append(".metro-columns { ");
            sb.Append("-ms-column-count: 2; column-count: 2; "); // Two columns
            sb.Append("-ms-column-gap: 24px; column-gap: 24px; "); // Gap between columns
            sb.Append("-ms-column-rule: 1px solid rgba(127,127,127,0.3); column-rule: 1px solid rgba(127,127,127,0.3); "); // Separator line
            sb.Append("text-align: justify; ");
            sb.Append("-ms-text-justify: inter-ideograph; ");
            sb.Append("}");
            
            // Alternative: full-width horizontal scrolling layout
            sb.Append(".metro-hub { ");
            sb.Append("height: 100%; ");
            sb.Append("-ms-overflow-style: -ms-autohiding-scrollbar; "); // Auto-hide scrollbar
            sb.Append("overflow-x: auto; overflow-y: hidden; ");
            sb.Append("-ms-column-width: 300px; column-width: 300px; "); // Each column ~300px
            sb.Append("-ms-column-gap: 32px; column-gap: 32px; ");
            sb.Append("-ms-column-fill: auto; column-fill: auto; ");
            sb.Append("}");
#endif

            // ===============================================
            // ===============================================
            // ===============================================
            // Mermaid 7.x 样式覆盖 - GitHub 风格 (深色/浅色)
            // ===============================================
            // 基础容器
            sb.Append(".mermaid { margin: 20px 0; text-align: center; display: block; overflow-x: auto; }");
            
            // 1. 移除 height: auto，防止 IE11 塌陷
            // 2. 移除 width: 100%，改由 JS 控制，防止 flex 布局下宽度计算错误
            sb.Append(".mermaid svg { display: block; margin: 0 auto; }");
            
            // 全局字体
            sb.Append(".mermaid text, .mermaid tspan { font-family: -apple-system,BlinkMacSystemFont,'Segoe UI','Noto Sans',Helvetica,Arial,sans-serif !important; font-size: 16px !important; }");

            if (theme == ElementTheme.Dark)
            {
                // --- GitHub Dark Theme ---
                var ghBg = "#0d1117";       // GitHub Dark Canvas
                var ghNodeBg = "#161b22";   // Node Background
                var ghBorder = "#30363d";   // Borders
                var ghText = "#c9d1d9";     // Primary Text
                var ghLine = "#8b949e";     // Lines & Icons
                var ghBlue = "#58a6ff";     // Blue accents
                
                // 1. 流程图 (Flowchart)
                sb.Append($"g.node rect, g.node circle, g.node ellipse, g.node polygon {{ fill: {ghNodeBg} !important; stroke: {ghBorder} !important; stroke-width: 1.5px !important; }}");
                sb.Append($"g.node text, g.node tspan {{ fill: {ghText} !important; }}");
                
                // 连线
                sb.Append($"g.edgePath path {{ stroke: {ghLine} !important; stroke-width: 1.5px !important; fill: none; }}");
                sb.Append($"g.edgePath marker path, marker path, marker {{ fill: {ghLine} !important; stroke: {ghLine} !important; }}");
                sb.Append($"#arrowhead path {{ fill: {ghLine} !important; }}");
                
                // 标签背景
                sb.Append($"g.label {{ color: {ghText} !important; }}");
                sb.Append($".edgeLabel {{ background-color: {ghBg} !important; color: {ghText} !important; }}");
                sb.Append($".edgeLabel rect {{ fill: {ghBg} !important; opacity: 1; }}");

                // 2. 时序图 (Sequence)
                sb.Append($".mermaid .actor {{ fill: {ghNodeBg} !important; stroke: {ghBorder} !important; stroke-width: 1.5px !important; }}");
                sb.Append($".mermaid text.actor, .mermaid tspan.actor {{ fill: {ghText} !important; font-weight: 600 !important; }}");
                sb.Append($".mermaid .actor-line {{ stroke: {ghLine} !important; stroke-width: 1px !important; }}");
                sb.Append($".mermaid .messageLine0, .mermaid .messageLine1 {{ stroke: {ghText} !important; stroke-width: 1.5px !important; }}");
                sb.Append($".mermaid .messageText, .mermaid .messageText tspan {{ fill: {ghText} !important; stroke: none !important; }}");
                // Loop & Notes
                sb.Append($".mermaid .loopText, .mermaid .loopText tspan, .mermaid .noteText, .mermaid .noteText tspan {{ fill: {ghText} !important; stroke: none !important; }}");
                sb.Append($".mermaid .loopLine {{ stroke: {ghBlue} !important; stroke-width: 2px !important; }}");
                sb.Append($".mermaid .note {{ fill: {ghNodeBg} !important; stroke: {ghBlue} !important; }}");
                sb.Append($".mermaid .labelBox {{ fill: {ghNodeBg} !important; stroke: {ghBorder} !important; }}");
                
                // 3. 甘特图 (Gantt)
                sb.Append($".mermaid .section {{ stroke: none !important; opacity: 0.2; }}");
                sb.Append($".mermaid .section0, .mermaid .section1 {{ fill: {ghNodeBg} !important; }}");
                sb.Append($".mermaid .task {{ fill: #1f6feb !important; stroke: none !important; }}"); // GitHub Blue
                sb.Append($".mermaid .taskText, .mermaid .taskText tspan {{ fill: {ghText} !important; }}");
                sb.Append($".mermaid .grid .tick text, .mermaid .grid .tick tspan {{ fill: {ghText} !important; }}");
                sb.Append($".mermaid .grid .tick line {{ stroke: {ghBorder} !important; stroke-opacity: 0.5; }}");
            }
            else
            {
                // --- GitHub Light Theme ---
                var ghBg = "#ffffff";
                var ghNodeBg = "#ffffff";
                var ghBorder = "#d0d7de";
                var ghText = "#24292f";
                var ghLine = "#8c959f";
                
                sb.Append($"g.node rect, g.node circle, g.node ellipse, g.node polygon {{ fill: {ghNodeBg} !important; stroke: {ghBorder} !important; stroke-width: 1.5px !important; }}");
                sb.Append($"g.node text, g.node tspan {{ fill: {ghText} !important; }}");
                
                sb.Append($"g.edgePath path {{ stroke: {ghLine} !important; stroke-width: 1.5px !important; fill: none; }}");
                sb.Append($"g.edgePath marker path, marker path, marker {{ fill: {ghLine} !important; stroke: {ghLine} !important; }}");
                
                sb.Append($".mermaid .actor {{ fill: {ghNodeBg} !important; stroke: {ghBorder} !important; }}");
                sb.Append($".mermaid text.actor, .mermaid tspan.actor {{ fill: {ghText} !important; }}");
                sb.Append($".mermaid .actor-line {{ stroke: {ghLine} !important; }}");
                sb.Append($".mermaid .messageLine0, .mermaid .messageLine1 {{ stroke: {ghText} !important; }}");
                sb.Append($".mermaid .messageText, .mermaid .messageText tspan {{ fill: {ghText} !important; }}");
                sb.Append($".mermaid .labelBox {{ fill: {ghBg} !important; stroke: {ghBorder} !important; }}");
            }
            // ===============================================

            sb.Append("</style>");

            if (!string.IsNullOrWhiteSpace(inlineCss)) sb.Append(inlineCss);

            // === 本地 KaTeX CSS ===
            sb.Append("<style>");
            if (needMath) sb.Append(_cachedKatexCss);
            sb.Append("</style>");

            sb.Append("</head><body>");
            sb.Append("<div id='content'></div>");

            // === 1. Polyfills 注入 (IE11 核心修复) ===
            // 移除 core-js 和 regenerator-runtime 以避免 "Function.prototype.toString" 冲突
            // sb.Append("<script>" + _cachedCoreJs + "</script>"); // REMOVED: Conflicts with IE11
            // sb.Append("<script>" + _cachedRegeneratorRuntime + "</script>"); // REMOVED: Not needed for v7

            // === 2. SVG getBBox Polyfill for IE11 ===
            sb.Append(@"<script>
                (function() {
                    if (typeof SVGElement !== 'undefined' && SVGElement.prototype) {
                        var originalGetBBox = SVGElement.prototype.getBBox;
                        SVGElement.prototype.getBBox = function() {
                            try {
                                if (originalGetBBox) {
                                    var result = originalGetBBox.call(this);
                                    if (result && (result.width !== 0 || result.height !== 0)) return result;
                                }
                            } catch(e) {}
                            try {
                                var rect = this.getBoundingClientRect();
                                var svg = this.ownerSVGElement || this;
                                var ctm = svg.getScreenCTM ? svg.getScreenCTM() : null;
                                var scale = ctm ? ctm.a : 1;
                                return {
                                    x: rect.left / scale, y: rect.top / scale,
                                    width: rect.width / scale || 100, height: rect.height / scale || 20
                                };
                            } catch(e2) { return { x: 0, y: 0, width: 100, height: 20 }; }
                        };
                    }
                    if (typeof SVGTextElement !== 'undefined' && SVGTextElement.prototype && !SVGTextElement.prototype.getComputedTextLength) {
                        SVGTextElement.prototype.getComputedTextLength = function() {
                            var text = this.textContent || '';
                            var width = 0;
                            for (var i = 0; i < text.length; i++) width += (text.charCodeAt(i) > 255) ? 18 : 9;
                            return width + 10;
                        };
                    }
                })();
            </script>");

            // === 3. ES6 Polyfills for IE11 (必须在 Mermaid 之前) ===
            // 内联关键 polyfills（甘特图需要）
            sb.Append(@"<script>
                // ========================================================================
                // 【关键】IE11 SVG classList 修复 (救活甘特图的唯一方法)
                // IE11 不支持在 SVG 元素上使用 classList，导致 Mermaid/D3 渲染甘特图时失败
                // ========================================================================
                if (!('classList' in document.createElementNS('http://www.w3.org/2000/svg', 'g'))) {
                    try {
                        var descr = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'classList');
                        if (descr) Object.defineProperty(SVGElement.prototype, 'classList', descr);
                    } catch(e) {}
                }
                
                // 双重保险：如果上面的 shim 不起作用，手动模拟 classList
                (function() {
                    if (typeof SVGElement === 'undefined') return;
                    try {
                        if ('classList' in document.createElementNS('http://www.w3.org/2000/svg', 'g')) return;
                    } catch(e) {}
                    
                    Object.defineProperty(SVGElement.prototype, 'classList', {
                        get: function() {
                            var self = this;
                            function update(fn) {
                                return function(value) {
                                    var classes = self.getAttribute('class') || '';
                                    var list = classes.split(/\s+/).filter(Boolean);
                                    var index = list.indexOf(value);
                                    fn(list, index, value);
                                    self.setAttribute('class', list.join(' '));
                                }
                            }
                            return {
                                add: update(function(list, index, value) { if (!~index) list.push(value); }),
                                remove: update(function(list, index) { if (~index) list.splice(index, 1); }),
                                toggle: update(function(list, index, value) { if (~index) list.splice(index, 1); else list.push(value); }),
                                contains: function(value) { return !!~(self.getAttribute('class') || '').split(/\s+/).indexOf(value); },
                                item: function(i) { return (self.getAttribute('class') || '').split(/\s+/)[i] || null; }
                            };
                        }
                    });
                })();
                // ========================================================================
                
                // Array.from polyfill
                if (!Array.from) {
                    Array.from = function(arrayLike) {
                        var arr = [];
                        for (var i = 0; i < arrayLike.length; i++) arr.push(arrayLike[i]);
                        return arr;
                    };
                }
                // Array.prototype.find polyfill
                if (!Array.prototype.find) {
                    Array.prototype.find = function(callback) {
                        for (var i = 0; i < this.length; i++) {
                            if (callback(this[i], i, this)) return this[i];
                        }
                        return undefined;
                    };
                }
                // Array.prototype.findIndex polyfill
                if (!Array.prototype.findIndex) {
                    Array.prototype.findIndex = function(callback) {
                        for (var i = 0; i < this.length; i++) {
                            if (callback(this[i], i, this)) return i;
                        }
                        return -1;
                    };
                }
                // Array.prototype.includes polyfill
                if (!Array.prototype.includes) {
                    Array.prototype.includes = function(item) {
                        for (var i = 0; i < this.length; i++) {
                            if (this[i] === item) return true;
                        }
                        return false;
                    };
                }
                // String.prototype.includes polyfill
                if (!String.prototype.includes) {
                    String.prototype.includes = function(search, start) {
                        if (typeof start !== 'number') start = 0;
                        return this.indexOf(search, start) !== -1;
                    };
                }
                // String.prototype.startsWith polyfill
                if (!String.prototype.startsWith) {
                    String.prototype.startsWith = function(search, pos) {
                        pos = pos || 0;
                        return this.substr(pos, search.length) === search;
                    };
                }
                // String.prototype.endsWith polyfill
                if (!String.prototype.endsWith) {
                    String.prototype.endsWith = function(search, len) {
                        if (len === undefined || len > this.length) len = this.length;
                        return this.substring(len - search.length, len) === search;
                    };
                }
                // Object.assign polyfill
                if (typeof Object.assign !== 'function') {
                    Object.assign = function(target) {
                        if (target == null) throw new TypeError('Cannot convert undefined or null to object');
                        var to = Object(target);
                        for (var i = 1; i < arguments.length; i++) {
                            var source = arguments[i];
                            if (source != null) {
                                for (var key in source) {
                                    if (Object.prototype.hasOwnProperty.call(source, key)) to[key] = source[key];
                                }
                            }
                        }
                        return to;
                    };
                }
                // Object.keys polyfill
                if (!Object.keys) {
                    Object.keys = function(obj) {
                        var keys = [];
                        for (var key in obj) {
                            if (Object.prototype.hasOwnProperty.call(obj, key)) keys.push(key);
                        }
                        return keys;
                    };
                }
                // Object.values polyfill
                if (!Object.values) {
                    Object.values = function(obj) {
                        var values = [];
                        for (var key in obj) {
                            if (Object.prototype.hasOwnProperty.call(obj, key)) values.push(obj[key]);
                        }
                        return values;
                    };
                }
                // Object.entries polyfill
                if (!Object.entries) {
                    Object.entries = function(obj) {
                        var entries = [];
                        for (var key in obj) {
                            if (Object.prototype.hasOwnProperty.call(obj, key)) entries.push([key, obj[key]]);
                        }
                        return entries;
                    };
                }
                // Number.isNaN polyfill
                Number.isNaN = Number.isNaN || function(value) {
                    return typeof value === 'number' && isNaN(value);
                };
                // Number.isFinite polyfill
                Number.isFinite = Number.isFinite || function(value) {
                    return typeof value === 'number' && isFinite(value);
                };
            </script>");
            
            // 加载外部 polyfills（如果有）
            if (!string.IsNullOrEmpty(_cachedEs6Promise))
                if (needDiagrams) sb.Append("<script>" + _cachedEs6Promise + "</script>");
            if (!string.IsNullOrEmpty(_cachedUrlPolyfill))
                if (needDiagrams) sb.Append("<script>" + _cachedUrlPolyfill + "</script>");
            
            // === 4. Mermaid & Libraries ===
            if (needDiagrams) sb.Append("<script>" + _cachedMermaidJs + "</script>");
            if (needMath) sb.Append("<script>" + _cachedKatexJs + "</script>");
            if (needMath) sb.Append("<script>" + _cachedKatexAutoRender + "</script>");
            if (needCode) sb.Append("<script>" + _cachedJs + "</script>");

            // Stable native scroll entry point; avoids allocating eval scripts during scroll sync.
            sb.Append(@"<script>
                window.__mdScrollToRatio = function(ratioText) {
                    var ratio = parseFloat(ratioText);
                    if (isNaN(ratio)) ratio = 0;
                    if (ratio < 0) ratio = 0;
                    if (ratio > 1) ratio = 1;

                    var doc = document.documentElement || document.body;
                    var body = document.body;
                    var scrollHeight = Math.max(
                        (doc && doc.scrollHeight) || 0,
                        (body && body.scrollHeight) || 0
                    );
                    var max = scrollHeight - (window.innerHeight || 0);
                    if (max < 0) max = 0;
                    window.scrollTo(0, max * ratio);
                };
            </script>");

            // === 4. Mermaid 初始化 (v7 专用配置) ===
            sb.Append(@"<script>
                if (typeof mermaid !== 'undefined') {
                    mermaid.initialize({ 
                        startOnLoad: false, 
                        theme: 'default',   // v7 忽略 theme，全靠 CSS
                        logLevel: 3,
                        flowchart: {
                            htmlLabels: false, // IE11 不支持 foreignObject
                            useMaxWidth: false // 【关键】关闭自动缩放，防止图表变小
                        },
                        sequence: {
                            useMaxWidth: false,
                            diagramMarginX: 50, diagramMarginY: 10, boxMargin: 10,
                            mirrorActors: true
                        },
                        gantt: {
                            useMaxWidth: false,
                            numberSectionStyles: 4,
                            axisFormat: '%Y-%m-%d'
                        }
                    });
                    // 添加错误回调以捕获解析错误
                    if (mermaid.parseError) {
                        var origParseError = mermaid.parseError;
                        mermaid.parseError = function(err, hash) {
                            console.error('Mermaid parseError:', err, hash);
                            var errDiv = document.createElement('div');
                            errDiv.style.cssText = 'background:#f00;color:#fff;padding:10px;font-family:monospace;margin:10px 0;';
                            errDiv.textContent = 'Mermaid Error: ' + err;
                            document.body.insertBefore(errDiv, document.body.firstChild);
                            if (origParseError) origParseError(err, hash);
                        };
                    }
                }
            </script>");

#if WINDOWS_PHONE_APP
            sb.Append(@"<script>
                (function(){
                    var lastY = 0;
                    window.addEventListener('scroll', function(){
                        var y = window.pageYOffset || document.documentElement.scrollTop || document.body.scrollTop || 0;
                        if(window.external && window.external.notify){ window.external.notify('scroll:' + y); }
                        lastY = y;
                    });
                })();
            </script>");
#endif
            sb.Append("</body></html>");
            var bridge = new StringBuilder("(function(){var container=document.getElementById('content');");
            // Install helpers against an empty fragment. Visible work is scheduled
            // only after preview-patches overrides the legacy full-DOM scanner.
            bridge.Append("var empty=document.createElement('div');");
            AppendVisibleBlockProcessing(bridge, "empty", false);
            bridge.Append("window.__mdPrepareFragment=function(block){");
            AppendFragmentEnhancements(bridge, "block");
            bridge.Append("var tables=block.getElementsByTagName('table');for(var i=tables.length-1;i>=0;i--){var table=tables[i];if(table.parentNode.className.indexOf('table-wrapper')<0){var wrapper=document.createElement('div');wrapper.className='table-wrapper';table.parentNode.insertBefore(wrapper,table);wrapper.appendChild(table);}}};})();");
            var patchScript = await ReadAssetFileAsync("Assets/preview-patches.js");
            // Submit document content only after native NavigationCompleted.
            // A sent navigation payload is not an acknowledgement of DOM display.
            sb.Insert(sb.Length - "</body></html>".Length, "<script>" + bridge + patchScript + "</script>");
            PreviewResourceResolver.Navigate(webView, sb.ToString());
            await Task.FromResult(0);
        }

        public async Task<bool> UpdateBlocksAsync(WebView webView, IReadOnlyList<MarkdownBlock> previous, IReadOnlyList<MarkdownBlock> next)
        {
            if (webView == null || next == null) return false;
            var script = await Task.Run(() => BuildPatchScript(previous, next));
            try
            {
                var ack = await webView.InvokeScriptAsync("eval", new[] { script });
                if (ack == "ok") return true;
                if (previous != null)
                {
                    script = await Task.Run(() => BuildPatchScript(null, next));
                    return await webView.InvokeScriptAsync("eval", new[] { script }) == "ok";
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Preview patch failed: " + ex.Message); }
            return false;
        }

        private static string BuildPatchScript(IReadOnlyList<MarkdownBlock> previous, IReadOnlyList<MarkdownBlock> next)
        {
            var patches = PreviewPatchBuilder.Build(previous, next);
            var script = new StringBuilder("window.__mdApplyPatches(");
            script.Append(PreviewPatchBuilder.Serialize(patches)).Append(',').Append(previous == null ? 0 : previous.Count).Append(",[");
            var reindexFrom = int.MaxValue;
            foreach (var patch in patches)
                if (patch.RemoveCount != patch.InsertCount) reindexFrom = Math.Min(reindexFrom, patch.Start);
            var separator = false;
            // Initial/replaced wrappers already carry their metadata. For a small
            // edit, do not send or touch every unchanged DOM node just to renumber it.
            for (var i = 0; previous != null && i < next.Count; i++)
            {
                if (i < reindexFrom && i < previous.Count && previous[i].StartLine == next[i].StartLine && previous[i].EndLine == next[i].EndLine) continue;
                if (separator) script.Append(',');
                separator = true;
                script.Append('[').Append(i).Append(',').Append(next[i].StartLine).Append(',').Append(next[i].EndLine).Append(']');
            }
            return script.Append("],").Append(previous == null ? "true" : "false").Append(',').Append(next.Count).Append(");").ToString();
        }

        // A superseded preview returns null, never a partial document. Cancellation
        // is routine during typing; do not throw on this hot path (including under
        // the VS debugger). Actual rendering failures still fault the task.
        public Task<MarkdownRenderResult> RenderMarkdownAsync(string markdown, Func<string, string> normalizeImages, CancellationToken cancellationToken = default(CancellationToken))
        {
            // Capture mutable settings and the pipeline on the UI thread once.
            var pipeline = GetPipeline();
            var settings = MarkdownSettingsService.Instance;
            var alerts = settings.GithubStyleAlert;
            var language = settings.ApplyDefaultCodeLanguageWhen == DefaultCodeLanguageApplyMode.WhenAddCodeFencesViaMenubar
                ? string.Empty : SanitizeCodeLanguage(settings.DefaultCodeLanguage);
            return Task.Run(() => RenderMarkdownCore(markdown, pipeline,
                html => normalizeImages(EnhanceHtmlFragment(html, alerts, language)), cancellationToken));
        }

        public MarkdownRenderResult RenderMarkdown(string markdown)
        {
            var result = RenderMarkdownCore(markdown, GetPipeline(), EnhanceHtmlFragment, CancellationToken.None);
            result.Html = BuildHtmlFromBlocks(result.Blocks);
            return result;
        }

        private MarkdownRenderResult RenderMarkdownCore(string markdown, MarkdownPipeline pipeline, Func<string, string> enhance, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return null;
            var normalized = NormalizeNewLines(markdown ?? string.Empty);
            if (cancellationToken.IsCancellationRequested) return null;
            if (normalized.Length == 0)
            {
                return new MarkdownRenderResult
                {
                    Html = string.Empty,
                    Blocks = new List<MarkdownBlock>(),
                    Outline = new List<MarkdownOutlineItem>()
                };
            }

            // Parse once for the whole document so reference links, footnotes and
            // loose lists retain document-wide semantics. Rendering the already
            // resolved top-level AST nodes separately gives the WebView stable DOM
            // blocks without reparsing fragments on the UI thread.
            var document = Markdown.Parse(normalized, pipeline);
            if (cancellationToken.IsCancellationRequested) return null;
            var blocks = BuildBlocksFromDocument(document, normalized, pipeline, enhance, cancellationToken);
            if (blocks == null || cancellationToken.IsCancellationRequested) return null;
            var outline = ExtractOutline(document, normalized, blocks);
            if (cancellationToken.IsCancellationRequested) return null;
            return new MarkdownRenderResult { Blocks = blocks, Outline = outline };
        }

        // UpdateContentAsync：针对 Mermaid v7 简化渲染逻辑
        public async Task UpdateContentAsync(WebView webView, string content, bool isMarkdown = true)
        {
            if (webView == null) return;

            var html = isMarkdown
                ? EnhanceHtmlFragment(Markdown.ToHtml(content ?? string.Empty, GetPipeline()))
                : (content ?? string.Empty);
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            var script = new StringBuilder();

            script.Append("(function(){");
            script.Append("  var container = document.getElementById('content');");
            script.Append("  if (!container) return;");
            script.Append("  var doc = document.documentElement; var body = document.body;");
            script.Append("  var scrollTop = (doc && doc.scrollTop) || (body && body.scrollTop);");
            script.Append("  container.style.minHeight = container.clientHeight + 'px';");
            script.Append("  try {");
            script.Append("    container.innerHTML = decodeURIComponent(escape(window.atob('").Append(payload).Append("')));");
            script.Append("  } catch(e) { console.error('Base64 decode failed'); }");
            script.Append("  var tables = container.getElementsByTagName('table');");
            script.Append("  for (var i = tables.length - 1; i >= 0; i--) {");
            script.Append("    var table = tables[i];");
            script.Append("    if (table.parentNode.className.indexOf('table-wrapper') === -1) {");
            script.Append("      var wrapper = document.createElement('div'); wrapper.className = 'table-wrapper';");
            script.Append("      table.parentNode.insertBefore(wrapper, table); wrapper.appendChild(table);");
            script.Append("    }");
            script.Append("  }");

            AppendFragmentEnhancements(script, "container");
            AppendVisibleBlockProcessing(script, "container", true);
            script.Append("  if(doc) doc.scrollTop = scrollTop;");
            script.Append("  if(body) body.scrollTop = scrollTop;");
            script.Append("  container.style.minHeight = '';");
            script.Append("})();");

            try
            {
                await webView.InvokeScriptAsync("eval", new[] { script.ToString() });
            }
            catch
            {
            }
        }

        // ... UpdateBlockAsync 等辅助方法保持原样 (如果你需要我也改 UpdateBlockAsync 请告诉我，逻辑同上) ...
        
        // 为了完整性，这里包含其余的辅助方法
        public async Task UpdateBlockAsync(WebView webView, MarkdownBlock block)
        {
             if (webView == null || block == null) return;
            // Html is produced by the background render pass. Never parse Markdown
            // here: this method resumes on the UI thread and must remain DOM-only.
            var blockHtml = block.Html ?? string.Empty;
            if (blockHtml.Length == 0) return;
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(blockHtml));
            var requiresHeavyProcessing = BlockNeedsHeavyProcessing(block);
            var script = new StringBuilder();
            script.Append("(function(){");
            script.Append("  var container = document.getElementById('content');");
            script.Append("  if(!container) return;");
            script.Append("  var target = container.querySelector('[data-block=\"").Append(block.Index).Append("\"]');");
            script.Append("  if(!target) return;");
            script.Append("  var doc = document.documentElement; var body = document.body;");
            script.Append("  var scrollTop = (doc && doc.scrollTop) || (body && body.scrollTop);");
            script.Append("  var tmp = document.createElement('div');");
            script.Append("  tmp.innerHTML = decodeURIComponent(escape(window.atob('").Append(payload).Append("')));");
            script.Append("  var next = tmp.firstElementChild;");
            script.Append("  if(!next) return;");
            script.Append("  target.parentNode.replaceChild(next, target);");

            if (requiresHeavyProcessing)
            {
                AppendFragmentEnhancements(script, "next");
                AppendVisibleBlockProcessing(script, "next", false);
            }

            script.Append("  if(doc) doc.scrollTop = scrollTop;");
            script.Append("  if(body) body.scrollTop = scrollTop;");
            script.Append("  return;");

            // 简单处理新块中的 mermaid
            script.Append("})();");
            try { await webView.InvokeScriptAsync("eval", new[] { script.ToString() }); } catch { }
        }

        public string ToHtml(string markdown) => Markdig.Markdown.ToHtml(markdown ?? string.Empty, GetPipeline());

        private IReadOnlyList<MarkdownOutlineItem> ExtractOutline(
            MarkdownDocument document,
            string markdown,
            IReadOnlyList<MarkdownBlock> blocks)
        {
            var outline = new List<MarkdownOutlineItem>();
            try
            {
                var slugCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var heading in document.Descendants<HeadingBlock>())
                {
                    var title = ExtractInlineText(heading.Inline).Trim();
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        continue;
                    }

                    var anchor = GetHeadingAnchor(heading, title, slugCounts);
                    outline.Add(new MarkdownOutlineItem
                    {
                        Title = title,
                        Level = heading.Level,
                        Line = Math.Max(0, heading.Line),
                        AnchorId = anchor,
                        BlockIndex = FindBlockIndexForLine(blocks, Math.Max(0, heading.Line))
                    });
                }
            }
            catch
            {
                outline.Clear();
                outline.AddRange(ExtractOutlineFallback(markdown, blocks));
            }

            return outline;
        }

        private static string ExtractInlineText(ContainerInline container)
        {
            if (container == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            foreach (var inline in container)
            {
                AppendInlineText(sb, inline);
            }
            return sb.ToString();
        }

        private static void AppendInlineText(StringBuilder sb, Inline inline)
        {
            if (inline == null)
            {
                return;
            }

            var literal = inline as LiteralInline;
            if (literal != null)
            {
                sb.Append(literal.Content.ToString());
                return;
            }

            var code = inline as CodeInline;
            if (code != null)
            {
                sb.Append(code.Content);
                return;
            }

            var lineBreak = inline as LineBreakInline;
            if (lineBreak != null)
            {
                sb.Append(" ");
                return;
            }

            var container = inline as ContainerInline;
            if (container != null)
            {
                foreach (var child in container)
                {
                    AppendInlineText(sb, child);
                }
            }
        }

        private static string GetHeadingAnchor(HeadingBlock heading, string title, IDictionary<string, int> slugCounts)
        {
            try
            {
                var attributes = heading.GetAttributes();
                if (attributes != null && !string.IsNullOrWhiteSpace(attributes.Id))
                {
                    return attributes.Id;
                }
            }
            catch
            {
            }

            return BuildGithubLikeSlug(title, slugCounts);
        }

        private static string BuildGithubLikeSlug(string title, IDictionary<string, int> slugCounts)
        {
            var sb = new StringBuilder();
            bool previousDash = false;
            var lower = (title ?? string.Empty).Trim().ToLowerInvariant();

            foreach (var c in lower)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    sb.Append(c);
                    previousDash = c == '-';
                }
                else if (char.IsWhiteSpace(c))
                {
                    if (!previousDash && sb.Length > 0)
                    {
                        sb.Append('-');
                        previousDash = true;
                    }
                }
            }

            var slug = sb.ToString().Trim('-');
            if (string.IsNullOrEmpty(slug))
            {
                slug = "section";
            }

            int count;
            if (!slugCounts.TryGetValue(slug, out count))
            {
                slugCounts[slug] = 1;
                return slug;
            }

            slugCounts[slug] = count + 1;
            return slug + "-" + count.ToString();
        }

        private static int FindBlockIndexForLine(IReadOnlyList<MarkdownBlock> blocks, int line)
        {
            if (blocks == null) return -1;
            var low = 0;
            var high = blocks.Count - 1;
            while (low <= high)
            {
                var mid = low + (high - low) / 2;
                var block = blocks[mid];
                if (line < block.StartLine) high = mid - 1;
                else if (line > block.EndLine) low = mid + 1;
                else return block.Index;
            }
            return -1;
        }

        private static IEnumerable<MarkdownOutlineItem> ExtractOutlineFallback(string markdown, IReadOnlyList<MarkdownBlock> blocks)
        {
            var result = new List<MarkdownOutlineItem>();
            var lines = NormalizeNewLines(markdown).Split('\n');
            var slugCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool inFence = false;
            string fenceMarker = null;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i] ?? string.Empty;
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("```") || trimmed.StartsWith("~~~"))
                {
                    if (!inFence)
                    {
                        inFence = true;
                        fenceMarker = trimmed.Substring(0, 3);
                    }
                    else if (!string.IsNullOrEmpty(fenceMarker) && trimmed.StartsWith(fenceMarker))
                    {
                        inFence = false;
                        fenceMarker = null;
                    }
                    continue;
                }

                if (inFence)
                {
                    continue;
                }

                var match = Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$");
                if (!match.Success)
                {
                    continue;
                }

                var title = Regex.Replace(match.Groups[2].Value, @"[`*_~\[\]\(\)!]", string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                result.Add(new MarkdownOutlineItem
                {
                    Title = title,
                    Level = match.Groups[1].Value.Length,
                    Line = i,
                    AnchorId = BuildGithubLikeSlug(title, slugCounts),
                    BlockIndex = FindBlockIndexForLine(blocks, i)
                });
            }

            return result;
        }

        private void AppendFragmentEnhancements(StringBuilder script, string scopeVariable)
        {
            script.Append("  var images = ").Append(scopeVariable).Append(".getElementsByTagName('img');");
            script.Append("  for (var imgIndex = 0; imgIndex < images.length; imgIndex++) {");
            script.Append("    var img = images[imgIndex];");
            script.Append("    img.setAttribute('loading', 'lazy');");
            script.Append("    img.setAttribute('decoding', 'async');");
            script.Append("    img.style.maxHeight = '70vh';");
            script.Append("    if (!img.getAttribute('data-placeholder-src')) { img.setAttribute('data-placeholder-src', 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw=='); }");
            script.Append("    if (img.getAttribute('data-src') && img.getAttribute('src') !== img.getAttribute('data-placeholder-src')) { img.setAttribute('src', img.getAttribute('data-placeholder-src')); }");
            script.Append("  }");
        }

        private void AppendVisibleBlockProcessing(StringBuilder script, string scopeVariable, bool includeDeferredPass)
        {
            script.Append("  window.__mdIsNearViewport = window.__mdIsNearViewport || function(el) {");
            script.Append("    if (!el || !el.getBoundingClientRect) return false;");
            script.Append("    var rect = el.getBoundingClientRect();");
            script.Append("    var margin = Math.max(window.innerHeight || 0, 600);");
            script.Append("    return rect.bottom >= -margin && rect.top <= (window.innerHeight || 0) + margin;");
            script.Append("  };");

            script.Append("  window.__mdActivateImage = window.__mdActivateImage || function(img) {");
            script.Append("    if (!img) return;");
            script.Append("    var dataSrc = img.getAttribute('data-src') || img.getAttribute('data-original-src');");
            script.Append("    if (!dataSrc) return;");
            script.Append("    img.setAttribute('src', dataSrc);");
            script.Append("    img.setAttribute('data-original-src', dataSrc);");
            script.Append("    img.removeAttribute('data-src');");
            script.Append("  };");

            script.Append("  window.__mdReleaseFarImage = window.__mdReleaseFarImage || function(img) {");
            script.Append("    if (!img) return;");
            script.Append("    var currentSrc = img.getAttribute('src');");
            script.Append("    var placeholderSrc = img.getAttribute('data-placeholder-src');");
            script.Append("    if (!placeholderSrc || !currentSrc || currentSrc === placeholderSrc) return;");
            script.Append("    if (!img.getAttribute('data-original-src')) img.setAttribute('data-original-src', currentSrc);");
            script.Append("    img.setAttribute('data-src', img.getAttribute('data-original-src'));");
            script.Append("    img.setAttribute('src', placeholderSrc);");
            script.Append("  };");

            script.Append("  window.__mdProcessVisibleImages = window.__mdProcessVisibleImages || function(root) {");
            script.Append("    var host = root || document.getElementById('content');");
            script.Append("    if (!host || !host.querySelectorAll) return;");
            script.Append("    var images = host.querySelectorAll('img');");
            script.Append("    var viewportHeight = window.innerHeight || 0;");
            script.Append("    var releaseMargin = Math.max(viewportHeight * 3, 1800);");
            script.Append("    for (var imageIndex = 0; imageIndex < images.length; imageIndex++) {");
            script.Append("      var img = images[imageIndex];");
            script.Append("      if (!img || !img.getBoundingClientRect) continue;");
            script.Append("      var rect = img.getBoundingClientRect();");
            script.Append("      if (rect.bottom < -releaseMargin || rect.top > viewportHeight + releaseMargin) {");
            script.Append("        window.__mdReleaseFarImage(img);");
            script.Append("        continue;");
            script.Append("      }");
            script.Append("      if ((img.getAttribute('data-src') || img.getAttribute('data-original-src')) && window.__mdIsNearViewport(img)) {");
            script.Append("        window.__mdActivateImage(img);");
            script.Append("      }");
            script.Append("    }");
            script.Append("  };");

            script.Append("  window.__mdResizeMermaid = window.__mdResizeMermaid || function(scope) {");
            script.Append("    if (!scope || !scope.querySelectorAll) return;");
            script.Append("    var svgs = scope.querySelectorAll('.mermaid svg');");
            script.Append("    for (var s = 0; s < svgs.length; s++) {");
            script.Append("      var svg = svgs[s];");
            script.Append("      svg.style.width = '100%';");
            script.Append("      svg.style.height = 'auto';");
            script.Append("      svg.style.maxWidth = 'none';");
            script.Append("    }");
            script.Append("  };");

            script.Append("  window.__mdProcessBlockHeavy = window.__mdProcessBlockHeavy || function(block) {");
            script.Append("    if (!block || block.getAttribute('data-heavy-ready') === 'true') return;");
            script.Append("    block.setAttribute('data-heavy-ready', 'true');");
            script.Append("    if (typeof katex !== 'undefined') {");
            script.Append("      var mathElements = block.querySelectorAll('.math');");
            script.Append("      for (var i = 0; i < mathElements.length; i++) {");
            script.Append("        var el = mathElements[i];");
            script.Append("        if (el.getAttribute('data-rendered')) continue;");
            script.Append("        var tex = el.textContent || el.innerText;");
            script.Append("        var displayMode = el.tagName === 'DIV';");
            script.Append("        try { katex.render(tex, el, { displayMode: displayMode, throwOnError: false }); el.setAttribute('data-rendered', 'true'); } catch (e) {}");
            script.Append("      }");
            script.Append("    }");
            script.Append("    if (typeof hljs !== 'undefined') {");
            script.Append("      var codeBlocks = block.querySelectorAll('pre code');");
            script.Append("      for (var c = 0; c < codeBlocks.length; c++) {");
            script.Append("        var code = codeBlocks[c];");
            script.Append("        if (code.getAttribute('data-hljs-ready')) continue;");
            script.Append("        if ((code.textContent || '').length <= 50000) hljs.highlightBlock(code);");
            script.Append("        code.setAttribute('data-hljs-ready', 'true');");
            script.Append("      }");
            script.Append("    }");
            script.Append("    if (window.__mdEnhanceCodeFences) window.__mdEnhanceCodeFences(block);");
            script.Append("    if (typeof mermaid !== 'undefined') {");
            script.Append("      var mermaidTargets = block.querySelectorAll('code.language-mermaid, pre.mermaid code, div.mermaid');");
            script.Append("      var nodesToInit = [];");
            script.Append("      for (var m = 0; m < mermaidTargets.length; m++) {");
            script.Append("        var target = mermaidTargets[m];");
            script.Append("        if (target.className && (target.className.indexOf('language-flow') !== -1 || target.className.indexOf('language-sequence') !== -1)) continue;");
            script.Append("        if (target.tagName.toLowerCase() === 'div' && target.className === 'mermaid') {");
            script.Append("          if (!target.getAttribute('data-processed')) nodesToInit.push(target);");
            script.Append("          continue;");
            script.Append("        }");
            script.Append("        var graphDef = target.innerText || target.textContent || '';");
            script.Append("        graphDef = graphDef.replace(/^\\s*flowchart\\s+/m, 'graph ');");
            script.Append("        var newDiv = document.createElement('div');");
            script.Append("        newDiv.className = 'mermaid';");
            script.Append("        newDiv.textContent = graphDef;");
            script.Append("        var parentPre = target.parentNode;");
            script.Append("        if (parentPre && parentPre.tagName.toLowerCase() === 'pre' && parentPre.parentNode) {");
            script.Append("          parentPre.parentNode.replaceChild(newDiv, parentPre);");
            script.Append("        } else if (target.parentNode) {");
            script.Append("          target.parentNode.replaceChild(newDiv, target);");
            script.Append("        }");
            script.Append("        nodesToInit.push(newDiv);");
            script.Append("      }");
            script.Append("      if (nodesToInit.length > 0) {");
            script.Append("        setTimeout(function() {");
            script.Append("          for (var n = 0; n < nodesToInit.length; n++) {");
            script.Append("            try { mermaid.init(undefined, nodesToInit[n]); } catch (e) {}");
            script.Append("          }");
            script.Append("          window.__mdResizeMermaid(block);");
            script.Append("        }, 0);");
            script.Append("      } else {");
            script.Append("        window.__mdResizeMermaid(block);");
            script.Append("      }");
            script.Append("    }");
            script.Append("  };");

            script.Append("  window.__mdCollectBlocks = window.__mdCollectBlocks || function(root) {");
            script.Append("    var host = root || document.getElementById('content');");
            script.Append("    if (!host || !host.querySelectorAll) return [];");
            script.Append("    var blocks = [];");
            script.Append("    if (host.className && host.className.indexOf('md-block') !== -1) blocks.push(host);");
            script.Append("    var nestedBlocks = host.querySelectorAll('.md-block');");
            script.Append("    for (var nestedIndex = 0; nestedIndex < nestedBlocks.length; nestedIndex++) { blocks.push(nestedBlocks[nestedIndex]); }");
            script.Append("    return blocks;");
            script.Append("  };");

            script.Append("  window.__mdBlockBudget = window.__mdBlockBudget || 2;");
            script.Append("  window.__mdProcessVisibleBlocks = window.__mdProcessVisibleBlocks || function(root, budget) {");
            script.Append("    var blocks = window.__mdCollectBlocks(root);");
            script.Append("    if (!blocks.length) return 0;");
            script.Append("    var remaining = typeof budget === 'number' ? budget : window.__mdBlockBudget;");
            script.Append("    var processed = 0; var started = Date.now();");
            script.Append("    for (var b = 0; b < blocks.length; b++) {");
            script.Append("      if (remaining <= 0 || (processed > 0 && Date.now() - started >= 8)) break;");
            script.Append("      var block = blocks[b];");
            script.Append("      if (block.getAttribute('data-heavy-ready') === 'true') continue;");
            script.Append("      if (!window.__mdIsNearViewport(block)) continue;");
            script.Append("      window.__mdProcessBlockHeavy(block);");
            script.Append("      processed++;");
            script.Append("      remaining--;");
            script.Append("    }");
            script.Append("    return processed;");
            script.Append("  };");

            script.Append("  window.__mdScheduleVisibleBlockPass = window.__mdScheduleVisibleBlockPass || function(root) {");
            script.Append("    if (window.__mdVisibleBlockBudgetTimer) return;");
            script.Append("    window.__mdVisibleBlockBudgetTimer = setTimeout(function() {");
            script.Append("      window.__mdVisibleBlockBudgetTimer = null;");
            script.Append("      var host = root || document.getElementById('content');");
            script.Append("      window.__mdProcessVisibleImages(host);");
            script.Append("      var processed = window.__mdProcessVisibleBlocks(host, window.__mdBlockBudget);");
            script.Append("      if (processed > 0) window.__mdScheduleVisibleBlockPass(host);");
            script.Append("    }, 80);");
            script.Append("  };");

            script.Append("  var hostBlocks = [];");
            script.Append("  if (").Append(scopeVariable).Append(".className && ").Append(scopeVariable).Append(".className.indexOf('md-block') !== -1) hostBlocks.push(").Append(scopeVariable).Append(");");
            script.Append("  var nestedHostBlocks = ").Append(scopeVariable).Append(".querySelectorAll('.md-block');");
            script.Append("  for (var nestedHostIndex = 0; nestedHostIndex < nestedHostBlocks.length; nestedHostIndex++) { hostBlocks.push(nestedHostBlocks[nestedHostIndex]); }");
            script.Append("  for (var hostIndex = 0; hostIndex < hostBlocks.length; hostIndex++) {");
            script.Append("    hostBlocks[hostIndex].removeAttribute('data-heavy-ready');");
            script.Append("  }");

            script.Append("  if (!window.__mdVisibleBlocksBound) {");
            script.Append("    window.__mdVisibleBlocksBound = true;");
            script.Append("    var scheduled = false;");
            script.Append("    var trigger = function() {");
            script.Append("      if (scheduled) return;");
            script.Append("      scheduled = true;");
            script.Append("      setTimeout(function() {");
            script.Append("        scheduled = false;");
            script.Append("        var content = document.getElementById('content');");
            script.Append("        window.__mdProcessVisibleImages(content);");
            script.Append("        var processed = window.__mdProcessVisibleBlocks(content, window.__mdBlockBudget);");
            script.Append("        if (processed > 0) window.__mdScheduleVisibleBlockPass(content);");
            script.Append("      }, 60);");
            script.Append("    };");
            script.Append("    window.addEventListener('scroll', trigger);");
            script.Append("    window.addEventListener('resize', trigger);");
            script.Append("  }");

            script.Append("  window.__mdProcessVisibleImages(").Append(scopeVariable).Append(");");

            var showCodeLineNumbers = MarkdownSettingsService.Instance.DisplayLineNumbersForCodeFences ? "true" : "false";
            script.Append("  window.__mdEnhanceCodeFences = function(root) {");
            script.Append("    var host = root || document.getElementById('content');");
            script.Append("    if (!host || !host.querySelectorAll) return;");
            script.Append("    var codes = host.querySelectorAll('pre code');");
            script.Append("    for (var codeIndex = 0; codeIndex < codes.length; codeIndex++) {");
            script.Append("      var code = codes[codeIndex];");
            script.Append("      var pre = code.parentNode;");
            script.Append("      if (!pre) continue;");
            script.Append("      if (").Append(showCodeLineNumbers).Append(") {");
            script.Append("        if (code.getAttribute('data-line-numbered') === 'true') continue;");
            script.Append("        if ((code.textContent || '').length > 50000) continue;");
            script.Append("        var html = code.innerHTML || '';");
            script.Append("        var lines = html.replace(/\\n$/, '').split('\\n');");
            script.Append("        var out = '';");
            script.Append("        for (var lineIndex = 0; lineIndex < lines.length; lineIndex++) {");
            script.Append("          out += '<span class=\"code-line\"><span class=\"line-no\">' + (lineIndex + 1) + '</span><span class=\"line-code\">' + lines[lineIndex] + '</span></span>';");
            script.Append("        }");
            script.Append("        pre.className = (pre.className ? pre.className + ' ' : '') + 'line-numbered';");
            script.Append("        code.innerHTML = out;");
            script.Append("        code.setAttribute('data-line-numbered', 'true');");
            script.Append("      } else {");
            script.Append("        pre.className = (pre.className || '').replace(/\\bline-numbered\\b/g, '');");
            script.Append("      }");
            script.Append("    }");
            script.Append("  };");

            script.Append("  if (window.__mdProcessVisibleBlocks(").Append(scopeVariable).Append(", window.__mdBlockBudget) >= window.__mdBlockBudget) window.__mdScheduleVisibleBlockPass(").Append(scopeVariable).Append(");");

            if (includeDeferredPass)
            {
                script.Append("  setTimeout(function(){");
                script.Append("    window.__mdProcessVisibleImages(").Append(scopeVariable).Append(");");
                script.Append("    if (window.__mdProcessVisibleBlocks(").Append(scopeVariable).Append(", window.__mdBlockBudget) >= window.__mdBlockBudget) window.__mdScheduleVisibleBlockPass(").Append(scopeVariable).Append(");");
                script.Append("  }, 120);");
            }
        }

        private bool BlockNeedsHeavyProcessing(MarkdownBlock block)
        {
            return block != null && BlockNeedsHeavyProcessing(block.Text);
        }

        private bool BlockNeedsHeavyProcessing(string markdown)
        {
            var text = markdown ?? string.Empty;
            if (text.Length == 0) return false;

            return text.IndexOf("![", StringComparison.Ordinal) >= 0
                || text.IndexOf("<img", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("```", StringComparison.Ordinal) >= 0
                || text.IndexOf("~~~", StringComparison.Ordinal) >= 0
                || text.IndexOf("$$", StringComparison.Ordinal) >= 0
                || text.IndexOf("\\(", StringComparison.Ordinal) >= 0
                || text.IndexOf("\\[", StringComparison.Ordinal) >= 0
                || text.IndexOf("```mermaid", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("```math", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string EnhanceHtmlFragment(string html)
        {
            var settings = MarkdownSettingsService.Instance;
            return EnhanceHtmlFragment(html, settings.GithubStyleAlert,
                settings.ApplyDefaultCodeLanguageWhen == DefaultCodeLanguageApplyMode.WhenAddCodeFencesViaMenubar
                ? string.Empty : SanitizeCodeLanguage(settings.DefaultCodeLanguage));
        }

        private static string EnhanceHtmlFragment(string html, bool alerts, string language)
        {
            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            var enhanced = html;

            if (alerts)
            {
                enhanced = EnhanceGithubAlerts(enhanced);
            }

            if (!string.IsNullOrEmpty(language) && enhanced.IndexOf("<pre", StringComparison.Ordinal) >= 0)
                enhanced = Regex.Replace(enhanced, "<pre><code(?![^>]*class=)([^>]*)>", "<pre><code class=\"language-" + language + "\"$1>", RegexOptions.IgnoreCase);

            if (enhanced.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0) return enhanced;

            return Regex.Replace(enhanced, "<img([^>]*?)src=\"([^\"]*)\"([^>]*)>", m =>
            {
                var before = m.Groups[1].Value;
                var src = m.Groups[2].Value;
                var after = m.Groups[3].Value;
                return "<img" + before + " loading=\"lazy\" decoding=\"async\" data-src=\"" + src + "\" data-placeholder-src=\"data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==\" src=\"data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==\"" + after + ">";
            });
        }

        private static string EnhanceGithubAlerts(string html)
        {
            return Regex.Replace(
                html,
                "<blockquote>\\s*<p>\\s*\\[!(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\\]\\s*(.*?)</p>",
                m =>
                {
                    var kind = m.Groups[1].Value.ToLowerInvariant();
                    var title = m.Groups[1].Value.ToUpperInvariant();
                    var rest = m.Groups[2].Value;
                    return "<blockquote class=\"alert alert-" + kind + "\"><p class=\"alert-title\">" + title + "</p>" + (string.IsNullOrWhiteSpace(rest) ? string.Empty : "<p>" + rest + "</p>");
                },
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }

        private static string ApplyDefaultCodeLanguage(string html, MarkdownSettingsService settings)
        {
            if (settings.ApplyDefaultCodeLanguageWhen == DefaultCodeLanguageApplyMode.WhenAddCodeFencesViaMenubar)
            {
                return html;
            }

            var language = SanitizeCodeLanguage(settings.DefaultCodeLanguage);
            if (string.IsNullOrEmpty(language))
            {
                return html;
            }

            return Regex.Replace(html, "<pre><code(?![^>]*class=)([^>]*)>", "<pre><code class=\"language-" + language + "\"$1>", RegexOptions.IgnoreCase);
        }

        private static string SanitizeCodeLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) return string.Empty;
            var sb = new StringBuilder();
            foreach (var c in language.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '+' || c == '#')
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static string NormalizeNewLines(string value) => (value ?? string.Empty).Replace("\r\n", "\n");

        private IReadOnlyList<MarkdownBlock> SplitIntoBlocks(string markdown)
        {
            var result = new List<MarkdownBlock>();
            var lines = NormalizeNewLines(markdown).Split('\n');
            var buffer = new StringBuilder();
            bool inFence = false;
            string fenceMarker = null;
            int startLine = 0;
            Action<int> flush = endLine =>
            {
                if (buffer.Length == 0) { startLine = endLine + 1; return; }
                result.Add(new MarkdownBlock { Index = result.Count, StartLine = startLine, EndLine = endLine, Text = buffer.ToString() });
                buffer.Clear();
                startLine = endLine + 1;
            };
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.TrimStart();
                bool isFenceLine = trimmed.StartsWith("```") || trimmed.StartsWith("~~~");
                if (isFenceLine)
                {
                    if (!inFence) { inFence = true; fenceMarker = trimmed.Substring(0, 3); }
                    else if (!string.IsNullOrEmpty(fenceMarker) && trimmed.StartsWith(fenceMarker)) { inFence = false; fenceMarker = null; }
                }
                bool isBlank = string.IsNullOrWhiteSpace(line);
                bool isLastLine = i == lines.Length - 1;
                if (!inFence && isBlank) { flush(i - 1); continue; }
                buffer.AppendLine(line);
                if (isLastLine) { flush(i); }
            }
            return result;
        }

        private IReadOnlyList<MarkdownBlock> BuildBlocksFromDocument(
            MarkdownDocument document,
            string markdown,
            MarkdownPipeline pipeline,
            Func<string, string> enhance,
            CancellationToken cancellationToken)
        {
            var blocks = new List<MarkdownBlock>();
            if (document == null || document.Count == 0)
            {
                return blocks;
            }

            var rendered = new StringBuilder();
            using (var writer = new StringWriter(rendered))
            {
                var renderer = new HtmlRenderer(writer);
                pipeline.Setup(renderer);

                for (var i = 0; i < document.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested) return null;
                    var syntaxBlock = document[i];
                    rendered.Clear();
                    renderer.Render(syntaxBlock);
                    writer.Flush();

                    var innerHtml = enhance(rendered.ToString());
                    if (cancellationToken.IsCancellationRequested) return null;

                    var sourceStart = Math.Max(0, Math.Min(markdown.Length, syntaxBlock.Span.Start));
                    var sourceEnd = Math.Max(sourceStart, Math.Min(markdown.Length - 1, syntaxBlock.Span.End));
                    var sourceLength = Math.Max(0, Math.Min(markdown.Length - sourceStart, sourceEnd - sourceStart + 1));

                    var startLine = Math.Max(0, syntaxBlock.Line);

                    var block = new MarkdownBlock
                    {
                        Index = blocks.Count,
                        StartLine = startLine,
                        EndLine = startLine + CountNewLines(markdown, sourceStart, sourceLength)
                    };
                    block.LibraryFeatures = (innerHtml.IndexOf("<code", StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : 0)
                        | (innerHtml.IndexOf("class=\"math", StringComparison.OrdinalIgnoreCase) >= 0 ? 2 : 0)
                        | (innerHtml.IndexOf("mermaid", StringComparison.OrdinalIgnoreCase) >= 0 ? 4 : 0);
                    block.InnerHtml = innerHtml;
                    block.Html = WrapBlockHtml(block, innerHtml);
                    blocks.Add(block);
                }
            }

            return blocks;
        }

        private static int CountNewLines(string text, int start, int length)
        {
            var count = 0;
            var value = text ?? string.Empty;
            for (var i = start; i < start + length; i++)
            {
                if (value[i] == '\n') count++;
            }
            return count;
        }

        private string BuildHtmlFromBlocks(IReadOnlyList<MarkdownBlock> blocks)
        {
            var sb = new StringBuilder();
            foreach (var block in blocks) sb.Append(block.Html ?? BuildBlockHtml(block));
            return sb.ToString();
        }

        private string BuildBlockHtml(MarkdownBlock block)
        {
            var inner = EnhanceHtmlFragment(Markdown.ToHtml(block.Text ?? string.Empty, GetPipeline()));
            return WrapBlockHtml(block, inner);
        }

        private static string WrapBlockHtml(MarkdownBlock block, string inner)
        {
            var sb = new StringBuilder();
            sb.Append("<div class=\"md-block\" data-block=\"").Append(block.Index).Append("\" data-start=\"").Append(block.StartLine).Append("\" data-end=\"").Append(block.EndLine).Append("\">");
            sb.Append(inner);
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}
