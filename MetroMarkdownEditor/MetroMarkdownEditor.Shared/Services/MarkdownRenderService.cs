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
        private static string _cachedCoreJs = null;
        private static string _cachedUrlPolyfill = null;
        private static string _cachedRegeneratorRuntime = null;

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
            if (_cachedCoreJs == null)
                _cachedCoreJs = await ReadAssetFileAsync("Assets/Mermaid/core.min.js");
            if (_cachedUrlPolyfill == null)
                _cachedUrlPolyfill = await ReadAssetFileAsync("Assets/Mermaid/url-polyfill.min.js");
            if (_cachedRegeneratorRuntime == null)
                _cachedRegeneratorRuntime = await ReadAssetFileAsync("Assets/Mermaid/regenerator-runtime.js");
            if (_cachedMermaidJs == null)
                _cachedMermaidJs = await ReadAssetFileAsync("Assets/Mermaid/mermaid7.min.js");

            string currentCssContent = (theme == ElementTheme.Dark) ? _cachedCssDark : _cachedCssLight;

            // 颜色变量
            var bodyColor = theme == ElementTheme.Dark ? "#e6e6e6" : "#24292f";
            var bgColor = theme == ElementTheme.Dark ? "#1e1e1e" : "#ffffff";
            var borderColor = "#dfe2e5"; // 仅用于表头底线和引用块

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
#endif

            var sb = new StringBuilder();

            sb.Append("<!DOCTYPE html><html><head>");
            sb.Append("<meta http-equiv='Content-Type' content='text/html; charset=utf-8' />");
            sb.Append("<meta http-equiv='X-UA-Compatible' content='IE=edge' />");
            sb.Append("<meta name='viewport' content='width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no' />");
#if WINDOWS_PHONE_APP
            // Allow external content loading in WP WebView
            sb.Append("<meta http-equiv='Content-Security-Policy' content=\"default-src * 'unsafe-inline' 'unsafe-eval'; img-src * data: blob:;\" />");
#endif

            // --- 样式注入 ---
            sb.Append("<style>");
            
            // 1. 注入高亮库 CSS (Atom One)
            sb.Append(currentCssContent);

            // 2. 注入自定义基础样式
            sb.Append("body { ");
            sb.Append($"font-family: 'Segoe UI', sans-serif; line-height: {lineHeight}; padding: {containerPadding}; ");
            sb.Append($"font-size: {baseFontSize};");
            sb.Append("color: " + bodyColor + "; background-color: " + bgColor + "; ");
            sb.Append("word-wrap: break-word; overflow-wrap: break-word;");
            sb.Append("}");

            // 【段落排版优化】仅对 p 标签应用两端对齐
            sb.Append("p { ");
            sb.Append("text-align: justify; ");
            // inter-word: 仅调整单词间距，不拉伸字符间距，避免字距过大
            sb.Append("-ms-text-justify: inter-word; text-justify: inter-word; ");
            // 限制最大字距，防止撑满时间距过大
            sb.Append("letter-spacing: normal; word-spacing: normal; ");
            // 英文断词：自动连字符，避免单词过长时强制换行
            sb.Append("-ms-hyphens: auto; hyphens: auto; ");
            // 保持单词完整，尽量不断词
            sb.Append("-ms-word-break: normal; word-break: normal; ");
            sb.Append("margin: 0.8em 0; ");
            sb.Append("}");

            // 【标题样式】
            sb.Append("h1, h2, h3, h4, h5, h6 { ");
            sb.Append("font-family: 'Segoe UI', sans-serif; ");
            sb.Append("font-weight: 600; ");
            sb.Append("margin-top: 1.2em; margin-bottom: 0.5em; ");
            sb.Append("}");

            // 图片
            sb.Append("img { max-width: 100%; height: auto; display: block; margin: 10px 0; border-radius: 4px; }");

            // === 代码块样式 ===
            sb.Append("pre { margin: 1em 0; padding: 0; text-align: left; }");
            sb.Append($"code {{ font-family: 'Consolas', 'Courier New', monospace; font-size: {codeFontSize}; }}");
            sb.Append(".hljs { border-radius: 0; padding: 0.8em; overflow-x: auto; display: block; }");

            // === 表格样式 ===
            sb.Append(".table-wrapper { overflow-x: auto; margin: 16px 0; border: none; }");
            sb.Append("table { border-collapse: collapse; width: 100%; border-style: hidden; font-size: 0.9em; }");
            sb.Append("th, td { border: none; padding: 8px 12px; white-space: nowrap; }");
            sb.Append("th { border-bottom: 1px solid " + borderColor + "; font-weight: 600; text-align: left; }");
            sb.Append("tr:nth-child(2n) { background-color: rgba(127,127,127,0.1); }");

            // 引用块
            sb.Append("blockquote { border-left: 4px solid " + borderColor + "; padding: 0 1em; color: #6a737d; margin: 10px 0; }");
            sb.Append(".alert-note { border-left-color: #0969da; background-color: rgba(9, 105, 218, 0.1); color: inherit; }");
            sb.Append(".alert-warning { border-left-color: #9a6700; background-color: rgba(154, 103, 0, 0.1); color: inherit; }");

            // === 代码语言标签 - Metro 风格 </JAVA> ===
            sb.Append(".code-lang-label { position: absolute; top: 4px; right: 8px; font-family: 'Segoe UI', sans-serif; font-size: 12px; color: #569cd6; opacity: 0.9; }");

            // === 脚注样式 ===
            sb.Append(".footnotes { margin-top: 2em; padding-top: 1em; border-top: 1px solid " + borderColor + "; font-size: 0.85em; }");
            sb.Append(".footnote-ref { font-size: 0.75em; vertical-align: super; text-decoration: none; }");
            sb.Append(".footnote-ref a { color: #569cd6; text-decoration: none; }");
            sb.Append(".footnote-backref { text-decoration: none; color: #569cd6; }");
            sb.Append("sup { font-size: 0.75em; }");
            sb.Append("a.footnote-ref { color: #569cd6; }");

            // === 数学公式样式 ===
            sb.Append(".math { overflow-x: auto; }");

            // === Mermaid 7.x 图表样式 - IE11 静态 CSS ===
            // Mermaid 容器
            sb.Append(".mermaid { text-align: center !important; margin: 1em 0 !important; background: transparent !important; }");
            sb.Append(".mermaid svg { max-width: 100% !important; background: transparent !important; }");
            
            // 所有 SVG 基础元素 - 强制颜色
            sb.Append(".mermaid rect { fill: #3b4252 !important; stroke: #81a1c1 !important; stroke-width: 2px !important; }");
            sb.Append(".mermaid polygon { fill: #3b4252 !important; stroke: #81a1c1 !important; stroke-width: 2px !important; }");
            sb.Append(".mermaid circle { fill: #3b4252 !important; stroke: #81a1c1 !important; stroke-width: 2px !important; }");
            sb.Append(".mermaid ellipse { fill: #3b4252 !important; stroke: #81a1c1 !important; stroke-width: 2px !important; }");
            sb.Append(".mermaid path { stroke: #81a1c1 !important; fill: none !important; }");
            sb.Append(".mermaid line { stroke: #81a1c1 !important; stroke-width: 2px !important; }");
            
            // 所有文本元素 - 强制白色
            sb.Append(".mermaid text { fill: #eceff4 !important; font-family: 'Segoe UI', Arial, sans-serif !important; font-size: 14px !important; stroke: none !important; }");
            sb.Append(".mermaid tspan { fill: #eceff4 !important; stroke: none !important; }");
            sb.Append(".mermaid .label text { fill: #eceff4 !important; }");
            sb.Append(".mermaid .label { color: #eceff4 !important; fill: #eceff4 !important; }");
            sb.Append(".mermaid .nodeLabel { color: #eceff4 !important; fill: #eceff4 !important; }");
            sb.Append(".mermaid foreignObject { color: #eceff4 !important; }");
            sb.Append(".mermaid foreignObject div { color: #eceff4 !important; }");
            
            // 流程图节点 (Mermaid 7.x 类名)
            sb.Append(".mermaid .node rect { fill: #4c566a !important; stroke: #88c0d0 !important; rx: 5 !important; ry: 5 !important; }");
            sb.Append(".mermaid .node polygon { fill: #5e81ac !important; stroke: #88c0d0 !important; }");
            sb.Append(".mermaid .node circle { fill: #4c566a !important; stroke: #88c0d0 !important; }");
            sb.Append(".mermaid .node ellipse { fill: #4c566a !important; stroke: #88c0d0 !important; }");
            
            // 边线和箭头
            sb.Append(".mermaid .edgePath path { stroke: #88c0d0 !important; stroke-width: 2px !important; fill: none !important; }");
            sb.Append(".mermaid .edgePath marker path { fill: #88c0d0 !important; stroke: #88c0d0 !important; }");
            sb.Append(".mermaid marker path { fill: #88c0d0 !important; stroke: #88c0d0 !important; }");
            sb.Append(".mermaid #arrowhead path { fill: #88c0d0 !important; }");
            sb.Append(".mermaid .arrowheadPath { fill: #88c0d0 !important; }");
            
            // 边标签
            sb.Append(".mermaid .edgeLabel { background-color: #2e3440 !important; }");
            sb.Append(".mermaid .edgeLabel rect { fill: #2e3440 !important; stroke: none !important; }");
            sb.Append(".mermaid .edgeLabel span { color: #eceff4 !important; background: #2e3440 !important; }");
            sb.Append(".mermaid .edgeLabel .label { color: #eceff4 !important; }");
            
            // 子图/集群
            sb.Append(".mermaid .cluster rect { fill: #2e3440 !important; stroke: #4c566a !important; }");
            sb.Append(".mermaid .cluster text { fill: #eceff4 !important; }");
            
            // 特殊形状修复
            sb.Append(".mermaid .diamond { fill: #5e81ac !important; stroke: #88c0d0 !important; }");
            sb.Append(".mermaid .rhombus { fill: #5e81ac !important; stroke: #88c0d0 !important; }");
            
            // 时序图
            sb.Append(".mermaid .actor { fill: #3b4252 !important; stroke: #88c0d0 !important; }");
            sb.Append(".mermaid .actor-line { stroke: #4c566a !important; stroke-dasharray: 3,3 !important; }");
            sb.Append(".mermaid .messageLine0 { stroke: #88c0d0 !important; stroke-width: 1.5px !important; }");
            sb.Append(".mermaid .messageLine1 { stroke: #88c0d0 !important; stroke-width: 1.5px !important; stroke-dasharray: 3,3 !important; }");
            sb.Append(".mermaid .messageText { fill: #eceff4 !important; }");
            sb.Append(".mermaid .note { fill: #3b4252 !important; stroke: #5c6370 !important; }");
            sb.Append(".mermaid .noteText { fill: #eceff4 !important; }");
            
            // 背景透明
            sb.Append(".mermaid .background { fill: transparent !important; }");

            sb.Append("</style>");

            // 3. 【关键】最后注入 inlineCss (手机专用补丁)
            if (!string.IsNullOrWhiteSpace(inlineCss))
            {
                sb.Append(inlineCss);
            }

            // === 本地 KaTeX CSS ===
            sb.Append("<style>");
            sb.Append(_cachedKatexCss);
            sb.Append("</style>");

            sb.Append("</head><body>");
            sb.Append("<div id='content'></div>");

            // === ES6 Promise polyfill for IE11 ===
            sb.Append("<script>");
            sb.Append(_cachedEs6Promise);
            sb.Append("</script>");

            // === Core-JS polyfill for ES6 syntax (arrow functions, etc.) ===
            sb.Append("<script>");
            sb.Append(_cachedCoreJs);
            sb.Append("</script>");

            // === URL Polyfill for IE11 ===
            sb.Append("<script>");
            sb.Append(_cachedUrlPolyfill);
            sb.Append("</script>");

            // === Regenerator Runtime for async/await ===
            sb.Append("<script>");
            sb.Append(_cachedRegeneratorRuntime);
            sb.Append("</script>");

            // === SVG getBBox Polyfill for IE11 ===
            sb.Append(@"<script>
                (function() {
                    // IE11 SVG getBBox polyfill
                    if (typeof SVGElement !== 'undefined' && SVGElement.prototype) {
                        var originalGetBBox = SVGElement.prototype.getBBox;
                        SVGElement.prototype.getBBox = function() {
                            try {
                                if (originalGetBBox) {
                                    var result = originalGetBBox.call(this);
                                    if (result && (result.width !== 0 || result.height !== 0)) {
                                        return result;
                                    }
                                }
                            } catch(e) {}
                            
                            // Fallback using getBoundingClientRect
                            try {
                                var rect = this.getBoundingClientRect();
                                var svg = this.ownerSVGElement || this;
                                var ctm = svg.getScreenCTM ? svg.getScreenCTM() : null;
                                var scale = ctm ? ctm.a : 1;
                                return {
                                    x: rect.left / scale,
                                    y: rect.top / scale,
                                    width: rect.width / scale || 100,
                                    height: rect.height / scale || 20,
                                    toString: function() { return '[object SVGRect]'; }
                                };
                            } catch(e2) {
                                // Ultimate fallback
                                return { x: 0, y: 0, width: 100, height: 20 };
                            }
                        };
                    }
                    
                    // Also polyfill getComputedTextLength for text elements
                    if (typeof SVGTextElement !== 'undefined' && SVGTextElement.prototype && !SVGTextElement.prototype.getComputedTextLength) {
                        SVGTextElement.prototype.getComputedTextLength = function() {
                            var text = this.textContent || '';
                            return text.length * 8; // Approximate 8px per character
                        };
                    }
                })();
            </script>");

            // === 本地 Mermaid JS ===
            sb.Append("<script>");
            sb.Append(_cachedMermaidJs);
            sb.Append("</script>");

            // === 本地 KaTeX JS ===
            sb.Append("<script>");
            sb.Append(_cachedKatexJs);
            sb.Append("</script>");
            sb.Append("<script>");
            sb.Append(_cachedKatexAutoRender);
            sb.Append("</script>");

            // === Highlight.js ===
            sb.Append("<script>");
            sb.Append(_cachedJs);
            sb.Append("</script>");

            // Mermaid 初始化 - IE11 兼容配置
            // theme: null = 禁用 CSS 变量
            // htmlLabels: false = 使用纯 SVG text 而非 foreignObject（IE11 不支持）
            sb.Append(@"<script>
                if (typeof mermaid !== 'undefined') {
                    mermaid.initialize({ 
                        startOnLoad: false, 
                        theme: null,
                        securityLevel: 'loose',
                        flowchart: {
                            curve: 'basis',
                            htmlLabels: false,
                            useMaxWidth: false
                        },
                        sequence: {
                            diagramMarginX: 50,
                            diagramMarginY: 10,
                            actorMargin: 50,
                            width: 150,
                            height: 65,
                            boxMargin: 10,
                            boxTextMargin: 5,
                            noteMargin: 10,
                            messageMargin: 35,
                            mirrorActors: true,
                            useMaxWidth: false
                        },
                        gantt: {
                            titleTopMargin: 25,
                            barHeight: 20,
                            barGap: 4,
                            topPadding: 50,
                            leftPadding: 75,
                            gridLineStartPadding: 35,
                            fontSize: 11,
                            numberSectionStyles: 4,
                            axisFormat: '%Y-%m-%d'
                        }
                    });
                }
            </script>");

#if WINDOWS_PHONE_APP
            // Windows Phone: 添加滚动检测脚本，通过 ScriptNotify 发送滚动位置
            sb.Append(@"<script>
                (function(){
                    var lastY = 0;
                    window.addEventListener('scroll', function(){
                        var y = window.pageYOffset || document.documentElement.scrollTop || document.body.scrollTop || 0;
                        if(window.external && window.external.notify){
                            window.external.notify('scroll:' + y);
                        }
                        lastY = y;
                    });
                })();
            </script>");
#endif

            sb.Append("</body></html>");

            webView.NavigateToString(sb.ToString());

            await Task.FromResult(0);
        }

        // ... RenderMarkdown 和 UpdateBlockAsync 方法不需要改动，保持原样即可 ...
        // (为了节省篇幅，你可以直接保留文件里原有的这两个方法，逻辑不用动)
        
        public MarkdownRenderResult RenderMarkdown(string markdown)
        {
            var normalized = NormalizeNewLines(markdown ?? string.Empty);
            var blocks = SplitIntoBlocks(normalized);
            var html = BuildHtmlFromBlocks(blocks);
            return new MarkdownRenderResult { Html = html, Blocks = blocks };
        }

        public async Task UpdateContentAsync(WebView webView, string content, bool isMarkdown = true)
        {
            if (webView == null) return;
            string html = isMarkdown ? Markdown.ToHtml(content ?? string.Empty, _pipeline) : (content ?? string.Empty);
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            var script = new StringBuilder();
            script.Append("(function(){");
            script.Append("  var container = document.getElementById('content');");
            script.Append("  if (!container) return;");
            script.Append("  var doc = document.documentElement;");
            script.Append("  var body = document.body;");
            script.Append("  var scrollTop = (doc && doc.scrollTop) || (body && body.scrollTop);");
            script.Append("  container.style.minHeight = container.clientHeight + 'px';");
            script.Append("  try {");
            script.Append("    container.innerHTML = decodeURIComponent(escape(window.atob('").Append(payload).Append("')));");
            script.Append("  } catch(e) { console.error('Base64 decode failed'); }");

            // 表格容器处理
            script.Append("  var tables = container.getElementsByTagName('table');");
            script.Append("  for (var i = tables.length - 1; i >= 0; i--) {");
            script.Append("    var table = tables[i];");
            script.Append("    if (table.parentNode.className.indexOf('table-wrapper') === -1) {");
            script.Append("      var wrapper = document.createElement('div');");
            script.Append("      wrapper.className = 'table-wrapper';");
            script.Append("      table.parentNode.insertBefore(wrapper, table);");
            script.Append("      wrapper.appendChild(table);");
            script.Append("    }");
            script.Append("  }");

            // 触发高亮
            script.Append("  if (typeof hljs !== 'undefined') {");
            script.Append("    var blocks = container.querySelectorAll('pre code');");
            script.Append("    for(var i=0; i<blocks.length; i++){ hljs.highlightBlock(blocks[i]); }");
            script.Append("  }");

            // 引用块处理
            script.Append("  var quotes = container.getElementsByTagName('blockquote');");
            script.Append("  for (var i = 0; i < quotes.length; i++) {");
            script.Append("    var c = quotes[i].innerHTML;");
            script.Append("    if (c.indexOf('[!NOTE]') !== -1) { ");
            script.Append("       quotes[i].className += ' alert-note'; ");
            script.Append("       quotes[i].innerHTML = c.replace(/\\[!NOTE\\]/i, '<strong>NOTE</strong><br/>'); ");
            script.Append("    }");
            script.Append("    else if (c.indexOf('[!WARNING]') !== -1) { ");
            script.Append("       quotes[i].className += ' alert-warning'; ");
            script.Append("       quotes[i].innerHTML = c.replace(/\\[!WARNING\\]/i, '<strong>WARNING</strong><br/>'); ");
            script.Append("    }");
            script.Append("  }");

            // 代码块语言标签 - Metro 风格 </JAVA>
            script.Append("  var pres = container.querySelectorAll('pre');");
            script.Append("  for (var i = 0; i < pres.length; i++) {");
            script.Append("    var code = pres[i].querySelector('code');");
            script.Append("    if (code && !pres[i].querySelector('.code-lang-label')) {");
            script.Append("      var cls = code.className || '';");
            script.Append("      var match = cls.match(/language-(\\w+)/);");
            script.Append("      if (match && match[1]) {");
            script.Append("        var label = document.createElement('span');");
            script.Append("        label.className = 'code-lang-label';");
            script.Append("        label.textContent = '</' + match[1].toUpperCase() + '>';");
            script.Append("        pres[i].style.position = 'relative';");
            script.Append("        pres[i].insertBefore(label, pres[i].firstChild);");
            script.Append("      }");
            script.Append("    }");
            script.Append("  }");

            // KaTeX 数学公式渲染 - 处理 Markdig 生成的 .math 元素
            script.Append("  if (typeof katex !== 'undefined') {");
            script.Append("    var mathElements = container.querySelectorAll('.math');");
            script.Append("    for (var i = 0; i < mathElements.length; i++) {");
            script.Append("      var el = mathElements[i];");
            script.Append("      if (el.getAttribute('data-rendered')) continue;");
            script.Append("      var tex = el.textContent || el.innerText;");
            script.Append("      var displayMode = el.tagName === 'DIV';");
            script.Append("      try {");
            script.Append("        katex.render(tex, el, { displayMode: displayMode, throwOnError: false });");
            script.Append("        el.setAttribute('data-rendered', 'true');");
            script.Append("      } catch(e) { console.log('KaTeX error:', e); }");
            script.Append("    }");
            script.Append("  }");

            // Mermaid 图表渲染 - ES5 桥接脚本 + 调试
            // DEBUG: 检查 Mermaid 是否加载
            script.Append("  var debugInfo = 'Mermaid loaded: ' + (typeof mermaid !== 'undefined');");
            
            // 步骤1: 查找所有可能的 mermaid 代码块 (多种选择器)
            script.Append("  var codeBlocks = container.querySelectorAll('code.language-mermaid, code[class*=\"mermaid\"], pre.mermaid code, pre > code.mermaid');");
            script.Append("  debugInfo += ', codeBlocks: ' + codeBlocks.length;");
            script.Append("  var m, block, graphDef, newDiv, parentPre;");
            script.Append("  for (m = 0; m < codeBlocks.length; m++) {");
            script.Append("    block = codeBlocks[m];");
            script.Append("    graphDef = block.innerText || block.textContent;");
            script.Append("    newDiv = document.createElement('div');");
            script.Append("    newDiv.className = 'mermaid';");
            script.Append("    newDiv.textContent = graphDef;");
            script.Append("    parentPre = block.parentNode;");
            script.Append("    if (parentPre && parentPre.tagName && parentPre.tagName.toLowerCase() === 'pre') {");
            script.Append("      parentPre.parentNode.replaceChild(newDiv, parentPre);");
            script.Append("    } else {");
            script.Append("      block.parentNode.replaceChild(newDiv, block);");
            script.Append("    }");
            script.Append("  }");
            
            // 步骤2: 查找 Markdig 可能直接生成的 div.mermaid
            script.Append("  var existingDivs = container.querySelectorAll('div.mermaid:not([data-processed])');");
            script.Append("  debugInfo += ', divs: ' + existingDivs.length;");
            script.Append("  var n, mermaidArr = [];");
            script.Append("  for (n = 0; n < existingDivs.length; n++) {");
            script.Append("    existingDivs[n].setAttribute('data-processed', 'true');");
            script.Append("    mermaidArr.push(existingDivs[n]);");
            script.Append("  }");
            
            // 步骤3: 检查是否有 pre 包含 mermaid 关键字但没有正确 class 的情况
            script.Append("  var allPres = container.querySelectorAll('pre');");
            script.Append("  for (var p = 0; p < allPres.length; p++) {");
            script.Append("    var preText = allPres[p].innerText || allPres[p].textContent || '';");
            script.Append("    if (preText.indexOf('flowchart') === 0 || preText.indexOf('graph') === 0 || preText.indexOf('sequenceDiagram') === 0 || preText.indexOf('gantt') === 0) {");
            script.Append("      if (!allPres[p].getAttribute('data-processed')) {");
            script.Append("        var mDiv = document.createElement('div');");
            script.Append("        mDiv.className = 'mermaid';");
            script.Append("        mDiv.textContent = preText;");
            script.Append("        mDiv.setAttribute('data-processed', 'true');");
            script.Append("        allPres[p].parentNode.replaceChild(mDiv, allPres[p]);");
            script.Append("        mermaidArr.push(mDiv);");
            script.Append("      }");
            script.Append("    }");
            script.Append("  }");
            
            // 步骤4: 使用 mermaid.render() 逐个渲染并捕获详细错误
            script.Append("  debugInfo += ', toRender: ' + mermaidArr.length;");
            script.Append("  if (typeof mermaid !== 'undefined' && mermaidArr.length > 0) {");
            script.Append("    var renderErrors = [];");
            script.Append("    for (var r = 0; r < mermaidArr.length; r++) {");
            script.Append("      try {");
            script.Append("        var graphDef = mermaidArr[r].textContent || mermaidArr[r].innerText;");
            script.Append("        var graphId = 'mermaid-graph-' + r;");
            // 使用 mermaid.render() - v8 API
            script.Append("        if (typeof mermaid.render === 'function') {");
            script.Append("          mermaid.render(graphId, graphDef, function(svgCode) {");
            script.Append("            mermaidArr[r].innerHTML = svgCode;");
            script.Append("          });");
            script.Append("        } else if (typeof mermaid.mermaidAPI !== 'undefined' && typeof mermaid.mermaidAPI.render === 'function') {");
            script.Append("          mermaid.mermaidAPI.render(graphId, graphDef, function(svgCode) {");
            script.Append("            mermaidArr[r].innerHTML = svgCode;");
            script.Append("          });");
            script.Append("        } else {");
            script.Append("          renderErrors.push('No render API found');");
            script.Append("        }");
            script.Append("      } catch(e) {");
            script.Append("        renderErrors.push(e.message || e.toString());");
            script.Append("        mermaidArr[r].innerHTML = '<pre style=\"color:#ff6b6b;background:#2e3440;padding:10px;\">Render Error: ' + (e.message || e) + '</pre>';");
            script.Append("      }");
            script.Append("    }");
            script.Append("    if (renderErrors.length > 0) {");
            script.Append("      debugInfo += ', errors: ' + renderErrors.join('; ');");
            script.Append("    } else {");
            script.Append("      debugInfo += ', render: OK';");
            script.Append("    }");
            script.Append("  }");
            // DEBUG: 检查 SVG 元素数量
            script.Append("  var svgCount = container.querySelectorAll('.mermaid svg').length;");
            script.Append("  debugInfo += ', svgs: ' + svgCount;");
            // DEBUG: 显示调试信息在页面顶部
            script.Append("  var debugDiv = document.createElement('div');");
            script.Append("  debugDiv.style.cssText = 'background:#333;color:#0f0;padding:5px;font-size:10px;position:fixed;top:0;left:0;right:0;z-index:9999;';");
            script.Append("  debugDiv.textContent = debugInfo;");
            script.Append("  document.body.insertBefore(debugDiv, document.body.firstChild);");

            script.Append("  if(doc) doc.scrollTop = scrollTop;");
            script.Append("  if(body) body.scrollTop = scrollTop;");
            script.Append("  container.style.minHeight = '';");
            script.Append("})();");
            try { await webView.InvokeScriptAsync("eval", new[] { script.ToString() }); } catch { }
        }

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

            // 新增块表格处理
            script.Append("  var tablesInBlock = next.getElementsByTagName('table');");
            script.Append("  for (var i = tablesInBlock.length - 1; i >= 0; i--) {");
            script.Append("    var table = tablesInBlock[i];");
            script.Append("    if (table.parentNode.className.indexOf('table-wrapper') === -1) {");
            script.Append("      var wrapper = document.createElement('div');");
            script.Append("      wrapper.className = 'table-wrapper';");
            script.Append("      table.parentNode.insertBefore(wrapper, table);");
            script.Append("      wrapper.appendChild(table);");
            script.Append("    }");
            script.Append("  }");

            // 触发高亮
            script.Append("  if (typeof hljs !== 'undefined') {");
            script.Append("    var blocks = next.querySelectorAll('pre code');");
            script.Append("    for(var i=0;i<blocks.length;i++){ hljs.highlightBlock(blocks[i]); }");
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