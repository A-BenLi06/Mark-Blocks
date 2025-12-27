using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage; 
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Markdig;

namespace MetroMarkdownEditor.Services
{
    public class MarkdownRenderResult
    {
        public string Html { get; set; }
        public IReadOnlyList<MarkdownBlock> Blocks { get; set; }
    }

    public class MarkdownBlock
    {
        public int Index { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public string Text { get; set; }
    }

    public class MarkdownRenderService
    {
        private readonly MarkdownPipeline _pipeline;

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
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseSoftlineBreakAsHardlineBreak()
                .UseEmojiAndSmiley()          // 表情符号 :smile:
                .UseMathematics()              // LaTeX 数学公式
                .UseDiagrams()                 // Mermaid 图表
                .Build();
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

        public async Task LoadSkeletonAsync(WebView webView, string inlineCss, ElementTheme theme)
        {
            if (webView == null) return;

            if (_cachedJs == null)
                _cachedJs = await ReadAssetFileAsync("Assets/highlight.js");

            if (theme == ElementTheme.Dark && _cachedCssDark == null)
                _cachedCssDark = await ReadAssetFileAsync("Assets/atom-one-dark.css");

            if (theme == ElementTheme.Light && _cachedCssLight == null)
                _cachedCssLight = await ReadAssetFileAsync("Assets/atom-one-light.css");

            // 加载本地 KaTeX
            if (_cachedKatexCss == null)
                _cachedKatexCss = await ReadAssetFileAsync("Assets/KaTex/katex.min.css");
            if (_cachedKatexJs == null)
                _cachedKatexJs = await ReadAssetFileAsync("Assets/KaTex/katex.min.js");
            if (_cachedKatexAutoRender == null)
                _cachedKatexAutoRender = await ReadAssetFileAsync("Assets/KaTex/contrib/auto-render.min.js");

            // 加载本地 Mermaid (with ES6 polyfills for IE11)
            if (_cachedEs6Promise == null)
                _cachedEs6Promise = await ReadAssetFileAsync("Assets/Mermaid/es6-promise.auto.min.js");
            // REMOVED: core-js causes "Function.prototype.toString" errors in IE11
            // if (_cachedCoreJs == null)
            //     _cachedCoreJs = await ReadAssetFileAsync("Assets/Mermaid/core.min.js");
            if (_cachedUrlPolyfill == null)
                _cachedUrlPolyfill = await ReadAssetFileAsync("Assets/Mermaid/url-polyfill.min.js");
            // REMOVED: regenerator-runtime not needed for Mermaid v7
            // if (_cachedRegeneratorRuntime == null)
            //     _cachedRegeneratorRuntime = await ReadAssetFileAsync("Assets/Mermaid/regenerator-runtime.js");
            if (_cachedMermaidJs == null)
                _cachedMermaidJs = await ReadAssetFileAsync("Assets/Mermaid/mermaid7.min.js"); // 确保这里是 v7 版本

            string currentCssContent = (theme == ElementTheme.Dark) ? _cachedCssDark : _cachedCssLight;

            // 颜色变量
            var bodyColor = theme == ElementTheme.Dark ? "#e6e6e6" : "#24292f";
            var bgColor = theme == ElementTheme.Dark ? "#1e1e1e" : "#ffffff";
            var borderColor = "#dfe2e5"; 

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
            sb.Append("body { ");
            sb.Append($"font-family: 'Segoe UI', sans-serif; line-height: {lineHeight}; padding: {containerPadding}; ");
            sb.Append($"font-size: {baseFontSize};");
            sb.Append("color: " + bodyColor + "; background-color: " + bgColor + "; ");
            sb.Append("word-wrap: break-word; overflow-wrap: break-word;");
#if WINDOWS_PHONE_APP
            // WP: Enable hyphenation for better text flow
            sb.Append("-ms-hyphens: auto; hyphens: auto;");
#endif
            sb.Append("}");

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
            sb.Append(".hljs { border-radius: 0; padding: 0.8em; overflow-x: auto; display: block; }");

            // 表格与引用
            sb.Append(".table-wrapper { overflow-x: auto; margin: 16px 0; border: none; }");
            sb.Append("table { border-collapse: collapse; width: 100%; border-style: hidden; font-size: 0.9em; }");
            sb.Append("th, td { border: none; padding: 8px 12px; white-space: nowrap; }");
            sb.Append("th { border-bottom: 1px solid " + borderColor + "; font-weight: 600; text-align: left; }");
            sb.Append("tr:nth-child(2n) { background-color: rgba(127,127,127,0.1); }");
            sb.Append("blockquote { border-left: 4px solid " + borderColor + "; padding: 0 1em; color: #6a737d; margin: 10px 0; }");
            sb.Append(".alert-note { border-left-color: #0969da; background-color: rgba(9, 105, 218, 0.1); color: inherit; }");
            sb.Append(".alert-warning { border-left-color: #9a6700; background-color: rgba(154, 103, 0, 0.1); color: inherit; }");
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
            sb.Append(_cachedKatexCss);
            sb.Append("</style>");

            sb.Append("</head><body>");
            sb.Append("<div id='content'></div>");

            // === 1. Polyfills 注入 (IE11 核心修复) ===
            // 移除 core-js 和 regenerator-runtime 以避免 "Function.prototype.toString" 冲突
            sb.Append("<script>" + _cachedEs6Promise + "</script>");
            // sb.Append("<script>" + _cachedCoreJs + "</script>"); // REMOVED: Conflicts with IE11
            sb.Append("<script>" + _cachedUrlPolyfill + "</script>");
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
                sb.Append("<script>" + _cachedEs6Promise + "</script>");
            if (!string.IsNullOrEmpty(_cachedUrlPolyfill))
                sb.Append("<script>" + _cachedUrlPolyfill + "</script>");
            
            // === 4. Mermaid & Libraries ===
            sb.Append("<script>" + _cachedMermaidJs + "</script>");
            sb.Append("<script>" + _cachedKatexJs + "</script>");
            sb.Append("<script>" + _cachedKatexAutoRender + "</script>");
            sb.Append("<script>" + _cachedJs + "</script>");

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
            webView.NavigateToString(sb.ToString());
            await Task.FromResult(0);
        }

        // ... RenderMarkdown 不需要改动 ...
        public MarkdownRenderResult RenderMarkdown(string markdown)
        {
            var normalized = NormalizeNewLines(markdown ?? string.Empty);
            var blocks = SplitIntoBlocks(normalized);
            var html = BuildHtmlFromBlocks(blocks);
            return new MarkdownRenderResult { Html = html, Blocks = blocks };
        }

        // UpdateContentAsync：针对 Mermaid v7 简化渲染逻辑
        public async Task UpdateContentAsync(WebView webView, string content, bool isMarkdown = true)
        {
            if (webView == null) return;
            string html = isMarkdown ? Markdown.ToHtml(content ?? string.Empty, _pipeline) : (content ?? string.Empty);
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            var script = new StringBuilder();
            
            script.Append("(function(){");
            script.Append("  var container = document.getElementById('content');");
            script.Append("  if (!container) return;");
            
            // 恢复滚动位置
            script.Append("  var doc = document.documentElement; var body = document.body;");
            script.Append("  var scrollTop = (doc && doc.scrollTop) || (body && body.scrollTop);");
            
            // 更新 HTML
            script.Append("  container.style.minHeight = container.clientHeight + 'px';");
            script.Append("  try {");
            script.Append("    container.innerHTML = decodeURIComponent(escape(window.atob('").Append(payload).Append("')));");
            script.Append("  } catch(e) { console.error('Base64 decode failed'); }");

            // 表格容器处理
            script.Append("  var tables = container.getElementsByTagName('table');");
            script.Append("  for (var i = tables.length - 1; i >= 0; i--) {");
            script.Append("    var table = tables[i];");
            script.Append("    if (table.parentNode.className.indexOf('table-wrapper') === -1) {");
            script.Append("      var wrapper = document.createElement('div'); wrapper.className = 'table-wrapper';");
            script.Append("      table.parentNode.insertBefore(wrapper, table); wrapper.appendChild(table);");
            script.Append("    }");
            script.Append("  }");

            // KaTeX 渲染
            script.Append("  if (typeof katex !== 'undefined') {");
            script.Append("    var mathElements = container.querySelectorAll('.math');");
            script.Append("    for (var i = 0; i < mathElements.length; i++) {");
            script.Append("      var el = mathElements[i];");
            script.Append("      if (el.getAttribute('data-rendered')) continue;");
            script.Append("      var tex = el.textContent || el.innerText;");
            script.Append("      var displayMode = el.tagName === 'DIV';");
            script.Append("      try { katex.render(tex, el, { displayMode: displayMode, throwOnError: false }); el.setAttribute('data-rendered', 'true'); } catch(e) {}");
            script.Append("    }");
            script.Append("  }");

            // Highlight.js
            script.Append("  if (typeof hljs !== 'undefined') {");
            script.Append("    var blocks = container.querySelectorAll('pre code');");
            script.Append("    for(var i=0; i<blocks.length; i++){ hljs.highlightBlock(blocks[i]); }");
            script.Append("  }");

            // -----------------------------------------------------------
            // Mermaid v7 渲染逻辑 (带语法兼容层)
            // -----------------------------------------------------------
            script.Append("  if (typeof mermaid !== 'undefined') {");
            // 1. 查找并转换所有 mermaid 代码块为 div
            script.Append("    var codeBlocks = container.querySelectorAll('code.language-mermaid, pre.mermaid code, div.mermaid');");
            script.Append("    var nodesToInit = [];");
            script.Append("    for (var m = 0; m < codeBlocks.length; m++) {");
            script.Append("      var block = codeBlocks[m];");
            // 【过滤】跳过属于其他库的语法（flow, sequence）
            script.Append("      if (block.className && (block.className.indexOf('language-flow') !== -1 || block.className.indexOf('language-sequence') !== -1)) {");
            script.Append("        continue;");
            script.Append("      }");
            // 如果已经是处理过的 div，直接加入列表；如果是 code，转换它
            script.Append("      if (block.tagName.toLowerCase() === 'div' && block.className === 'mermaid') {");
            script.Append("          if(!block.getAttribute('data-processed')) nodesToInit.push(block);");
            script.Append("      } else {");
            script.Append("          var graphDef = block.innerText || block.textContent;");
            // 【关键】v7 兼容性：将 'flowchart' 替换为 'graph'
            script.Append("          graphDef = graphDef.replace(/^\\s*flowchart\\s+/m, 'graph ');");
            script.Append("          var newDiv = document.createElement('div');");
            script.Append("          newDiv.className = 'mermaid';");
            script.Append("          newDiv.textContent = graphDef;");
            script.Append("          var parentPre = block.parentNode;");
            script.Append("          if (parentPre && parentPre.tagName.toLowerCase() === 'pre') {");
            script.Append("            parentPre.parentNode.replaceChild(newDiv, parentPre);");
            script.Append("          } else {");
            script.Append("            block.parentNode.replaceChild(newDiv, block);");
            script.Append("          }");
            script.Append("          nodesToInit.push(newDiv);");
            script.Append("      }");
            script.Append("    }");

            // 2. 调用 mermaid.init() - 使用延迟等待 SVG 生成
            script.Append("    if (nodesToInit.length > 0) {");

#if !WINDOWS_PHONE_APP
            // Windows: Add debug output if enabled in DevSettings
            bool showMermaidDebug = DevSettingsService.Instance.MermaidDebugEnabled;
            if (showMermaidDebug)
            {
                script.Append("      var initDebug = document.createElement('div');");
                script.Append("      initDebug.style.cssText = 'background:#333;color:#ff0;padding:10px;font-family:monospace;font-size:12px;margin-bottom:10px;';");
                script.Append("      initDebug.textContent = '[Mermaid Init] nodesToInit.length=' + nodesToInit.length;");
                script.Append("      container.insertBefore(initDebug, container.firstChild);");
            }
#endif

            script.Append("      setTimeout(function() {");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("        var initErrors = [];");
            }
#endif

            script.Append("        for (var ni = 0; ni < nodesToInit.length; ni++) {");
            script.Append("          var node = nodesToInit[ni];");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("          var graphDef = node.textContent || node.innerText || '';");
                script.Append("          var graphType = 'unknown';");
                script.Append("          if (graphDef.indexOf('gantt') !== -1) graphType = 'gantt';");
                script.Append("          else if (graphDef.indexOf('sequenceDiagram') !== -1) graphType = 'sequence';");
                script.Append("          else if (graphDef.indexOf('graph') !== -1 || graphDef.indexOf('flowchart') !== -1) graphType = 'flowchart';");
                script.Append("          try {");
                script.Append("            mermaid.init(undefined, node);");
                script.Append("          } catch(e) {");
                script.Append("            initErrors.push({ idx: ni, type: graphType, error: e.message, preview: graphDef.substring(0, 100) });");
                script.Append("            var errDiv = document.createElement('div');");
                script.Append("            errDiv.style.cssText = 'background:#f44;color:#fff;padding:10px;font-size:12px;margin:5px 0;';");
                script.Append("            errDiv.textContent = 'Mermaid Error [' + graphType + ']: ' + e.message;");
                script.Append("            node.parentNode.insertBefore(errDiv, node);");
                script.Append("          }");
            }
            else
            {
                script.Append("          try { mermaid.init(undefined, node); } catch(e) {}");
            }
#else
            script.Append("          try { mermaid.init(undefined, node); } catch(e) {}");
#endif

            script.Append("        }");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("        if (initErrors.length > 0) {");
                script.Append("          initDebug.textContent += ' | ERRORS: ' + JSON.stringify(initErrors);");
                script.Append("        } else {");
                script.Append("          initDebug.textContent += ' | init() called for ' + nodesToInit.length + ' nodes';");
                script.Append("        }");
            }
#endif
            
            // 延迟处理 SVG 尺寸
            script.Append("        var pollCount = 0;");
            script.Append("        var pollInterval = setInterval(function() {");
            script.Append("          pollCount++;");
            script.Append("          var svgs = container.querySelectorAll('.mermaid svg');");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("          var allMermaids = container.querySelectorAll('.mermaid');");
                script.Append("          initDebug.textContent = '[Mermaid Poll #' + pollCount + '] .mermaid divs=' + allMermaids.length + ', SVGs=' + svgs.length;");
            }
#endif

            script.Append("          if (svgs.length > 0 || pollCount >= 10) {");
            script.Append("            clearInterval(pollInterval);");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("            initDebug.textContent += ' [Done] Found ' + svgs.length + ' SVGs after ' + pollCount + ' polls';");
                script.Append("            var debugInfo = [];");
            }
#endif
            
            // 处理 SVG 尺寸
            script.Append("            for(var s=0; s<svgs.length; s++) {");
            script.Append("              var svg = svgs[s];");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("              var logEntry = {idx: s};");
            }
#endif

            script.Append("              var origViewBox = svg.getAttribute('viewBox');");
            script.Append("              var origW = svg.getAttribute('width');");
            script.Append("              var origH = svg.getAttribute('height');");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("              logEntry.origViewBox = origViewBox;");
                script.Append("              logEntry.origW = origW; logEntry.origH = origH;");
            }
#endif

            script.Append("              var origViewBoxParts = origViewBox ? origViewBox.split(' ') : [];");
            script.Append("              var vbW = 0, vbH = 0;");
            script.Append("              if (origViewBoxParts.length === 4) {");
            script.Append("                vbW = parseFloat(origViewBoxParts[2]);");
            script.Append("                vbH = parseFloat(origViewBoxParts[3]);");
            script.Append("              }");
            script.Append("              if (!vbW || !vbH) {");
            script.Append("                if (origW && origH && (origW+'').indexOf('%') === -1) {");
            script.Append("                  vbW = parseFloat(origW);");
            script.Append("                  vbH = parseFloat(origH);");
            script.Append("                }");
            script.Append("              }");
            script.Append("              if (!vbW || !vbH) {");
            script.Append("                try {");
            script.Append("                  var bbox = svg.getBBox();");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("                  logEntry.bboxResult = { x: bbox.x, y: bbox.y, w: bbox.width, h: bbox.height };");
            }
#endif

            script.Append("                  if (bbox && bbox.width > 0 && bbox.height > 0) {");
            script.Append("                    vbW = bbox.width; vbH = bbox.height;");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("                    logEntry.bboxFallback = true;");
            }
#endif

            script.Append("                    svg.setAttribute('viewBox', bbox.x + ' ' + bbox.y + ' ' + bbox.width + ' ' + bbox.height);");
            script.Append("                  }");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("                } catch(e) { logEntry.bboxError = e.message; }");
            }
            else
            {
                script.Append("                } catch(e) {}");
            }
#else
            script.Append("                } catch(e) {}");
#endif

            script.Append("              }");
            script.Append("              if (!vbW || !vbH) {");
            script.Append("                var mDiv = svg.parentNode;");
            script.Append("                var defaultW = mDiv ? mDiv.clientWidth - 40 : 600;");
            script.Append("                if (defaultW <= 0) defaultW = 600;");
            script.Append("                var defaultH = 400;");
            script.Append("                svg.style.cssText = 'width: ' + defaultW + 'px !important; height: ' + defaultH + 'px !important; max-width: none !important;';");
            script.Append("                svg.setAttribute('width', defaultW);");
            script.Append("                svg.setAttribute('height', defaultH);");
            script.Append("                if (!svg.getAttribute('viewBox')) svg.setAttribute('viewBox', '0 0 ' + defaultW + ' ' + defaultH);");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("                logEntry.defaultFallback = true;");
                script.Append("                logEntry.appliedW = defaultW;");
                script.Append("                logEntry.appliedH = defaultH;");
            }
#endif

            script.Append("              }");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("              logEntry.vbW = vbW; logEntry.vbH = vbH;");
            }
#endif

            script.Append("              if (vbW > 0 && vbH > 0) {");
            script.Append("                var mDiv = svg.parentNode;");
            script.Append("                var containerW = mDiv ? mDiv.clientWidth : 600;");
            script.Append("                if (containerW <= 0) containerW = 600;");
            script.Append("                var maxAllowedW = containerW - 40;");
            script.Append("                var aspectRatio = vbH / vbW;");
            script.Append("                var finalW = Math.min(vbW, maxAllowedW);");
            script.Append("                var finalH = finalW * aspectRatio;");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("                logEntry.containerW = containerW; logEntry.maxAllowedW = maxAllowedW; logEntry.finalW = finalW; logEntry.finalH = finalH;");
            }
#endif

            script.Append("                svg.style.cssText = 'width: ' + finalW + 'px !important; height: ' + finalH + 'px !important; max-width: none !important;';");
            script.Append("                svg.setAttribute('width', finalW);");
            script.Append("                svg.setAttribute('height', finalH);");
            script.Append("              }");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("              debugInfo.push(logEntry);");
            }
#endif

            script.Append("            }");

#if !WINDOWS_PHONE_APP
            if (showMermaidDebug)
            {
                script.Append("            initDebug.textContent += ' [SVG Info] ' + JSON.stringify(debugInfo);");
            }
#endif

            script.Append("          }");
            script.Append("        }, 200);");
            script.Append("      }, 50);");
            script.Append("    }");
            script.Append("  }");
            // -----------------------------------------------------------

            script.Append("  if(doc) doc.scrollTop = scrollTop;");
            script.Append("  if(body) body.scrollTop = scrollTop;");
            script.Append("  container.style.minHeight = '';");
            script.Append("})();");
            
            try { await webView.InvokeScriptAsync("eval", new[] { script.ToString() }); } catch { }
        }

        // ... UpdateBlockAsync 等辅助方法保持原样 (如果你需要我也改 UpdateBlockAsync 请告诉我，逻辑同上) ...
        
        // 为了完整性，这里包含其余的辅助方法
        public async Task UpdateBlockAsync(WebView webView, MarkdownBlock block)
        {
             if (webView == null || block == null) return;
            var blockHtml = BuildBlockHtml(block);
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(blockHtml));
            var script = new StringBuilder();
            script.Append("(function(){");
            script.Append("  var container = document.getElementById('content');");
            script.Append("  if(!container) return;");
            script.Append("  var target = container.querySelector('[data-block=\"").Append(block.Index).Append("\"]');");
            script.Append("  if(!target) return;");
            script.Append("  var tmp = document.createElement('div');");
            script.Append("  tmp.innerHTML = decodeURIComponent(escape(window.atob('").Append(payload).Append("')));");
            script.Append("  var next = tmp.firstElementChild;");
            script.Append("  if(!next) return;");
            script.Append("  target.parentNode.replaceChild(next, target);");

            // 简单处理新块中的 mermaid
            script.Append("  if (typeof mermaid !== 'undefined') {");
            script.Append("    var mCodes = next.querySelectorAll('code.language-mermaid');");
            script.Append("    var mNodes = [];");
            script.Append("    for(var i=0; i<mCodes.length; i++){");
            script.Append("       var d = document.createElement('div'); d.className='mermaid'; d.textContent = mCodes[i].innerText;");
            script.Append("       mCodes[i].parentNode.parentNode.replaceChild(d, mCodes[i].parentNode);");
            script.Append("       mNodes.push(d);");
            script.Append("    }");
            script.Append("    if(mNodes.length > 0) {");
            script.Append("       setTimeout(function(){");
            script.Append("         try {");
            script.Append("           mermaid.init(undefined, mNodes);");
            script.Append("           var svgs = next.querySelectorAll('.mermaid svg');");
            script.Append("           for(var s=0; s<svgs.length; s++) {");
            script.Append("             var svg = svgs[s];");
            script.Append("             svg.removeAttribute('height');");
            script.Append("             svg.removeAttribute('width');");
            script.Append("             svg.style.height = 'auto';");
            script.Append("             svg.style.width = '100%';");
            script.Append("             svg.style.maxWidth = 'none';");
            script.Append("           }");
            script.Append("         } catch(e) {}");
            script.Append("       }, 0);");
            script.Append("    }");
            script.Append("  }");
            
            script.Append("})();");
            try { await webView.InvokeScriptAsync("eval", new[] { script.ToString() }); } catch { }
        }

        public string ToHtml(string markdown) => Markdig.Markdown.ToHtml(markdown ?? string.Empty, _pipeline);

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

        private string BuildHtmlFromBlocks(IReadOnlyList<MarkdownBlock> blocks)
        {
            var sb = new StringBuilder();
            foreach (var block in blocks) sb.Append(BuildBlockHtml(block));
            return sb.ToString();
        }

        private string BuildBlockHtml(MarkdownBlock block)
        {
            var inner = Markdown.ToHtml(block.Text ?? string.Empty, _pipeline);
            var sb = new StringBuilder();
            sb.Append("<div class=\"md-block\" data-block=\"").Append(block.Index).Append("\" data-start=\"").Append(block.StartLine).Append("\" data-end=\"").Append(block.EndLine).Append("\">");
            sb.Append(inner);
            sb.Append("</div>");
            return sb.ToString();
        }
    }
}