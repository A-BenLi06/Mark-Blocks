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

        public MarkdownRenderService()
        {
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseSoftlineBreakAsHardlineBreak()
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

            // --- 样式注入 ---
            sb.Append("<style>");
            
            // 1. 注入高亮库 CSS (Atom One)
            sb.Append(currentCssContent);

            // 2. 注入自定义基础样式
            sb.Append("body { ");
            sb.Append($"font-family: 'Segoe UI', sans-serif; line-height: {lineHeight}; padding: {containerPadding}; ");
            sb.Append($"font-size: {baseFontSize};");
            sb.Append("color: " + bodyColor + "; background-color: " + bgColor + "; ");
            sb.Append("word-wrap: break-word; overflow-wrap: break-word; word-break: break-word;");
            sb.Append("}");

            // 图片去圆角 (如果你也想把图片变直角，把 border-radius 改为 0)
            sb.Append("img { max-width: 100%; height: auto; display: block; margin: 10px 0; border-radius: 4px; }");

            // === 代码块样式修改 ===
            sb.Append("pre { margin: 1em 0; padding: 0; }");
            sb.Append($"code {{ font-family: 'Consolas', 'Courier New', monospace; font-size: {codeFontSize}; }}");
            // 【关键修改】border-radius: 0; 去掉圆角
            sb.Append(".hljs { border-radius: 0; padding: 0.8em; overflow-x: auto; display: block; }");

            // === 表格样式修改 (无框沉浸式) ===
            // 移除外框 border: none
            sb.Append(".table-wrapper { overflow-x: auto; margin: 16px 0; border: none; }");
            sb.Append("table { border-collapse: collapse; width: 100%; border-style: hidden; font-size: 0.9em; }");
            
            // 移除单元格边框
            sb.Append("th, td { border: none; padding: 8px 12px; white-space: nowrap; }");
            
            // 仅保留表头底线，作为视觉分隔
            sb.Append("th { border-bottom: 1px solid " + borderColor + "; font-weight: 600; text-align: left; }");
            
            // 隔行变色 (可选，保留为了可读性)
            sb.Append("tr:nth-child(2n) { background-color: rgba(127,127,127,0.1); }");

            // 引用块
            sb.Append("blockquote { border-left: 4px solid " + borderColor + "; padding: 0 1em; color: #6a737d; margin: 10px 0; }");
            sb.Append(".alert-note { border-left-color: #0969da; background-color: rgba(9, 105, 218, 0.1); color: inherit; }");
            sb.Append(".alert-warning { border-left-color: #9a6700; background-color: rgba(154, 103, 0, 0.1); color: inherit; }");

            sb.Append("</style>");

            // 3. 【关键】最后注入 inlineCss (手机专用补丁)
            if (!string.IsNullOrWhiteSpace(inlineCss))
            {
                sb.Append(inlineCss);
            }

            sb.Append("</head><body>");
            sb.Append("<div id='content'></div>");

            sb.Append("<script>");
            sb.Append(_cachedJs);
            sb.Append("</script>");

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