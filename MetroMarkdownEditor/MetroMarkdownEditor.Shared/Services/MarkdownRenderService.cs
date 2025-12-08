using System;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Markdig; // 引用 Markdig

namespace MetroMarkdownEditor.Services
{
    public class MarkdownRenderService
    {
        // 兼容 IE11 �?highlight.js 版本
        private const string HljsScript = "https://cdnjs.cloudflare.com/ajax/libs/highlight.js/9.18.5/highlight.min.js";
        // 使用 Atom One 风格 (近似 IDE 风格)
        private const string HljsCssDark = "https://cdnjs.cloudflare.com/ajax/libs/highlight.js/9.18.5/styles/atom-one-dark.min.css";
        private const string HljsCssLight = "https://cdnjs.cloudflare.com/ajax/libs/highlight.js/9.18.5/styles/atom-one-light.min.css";

        // Markdig 管道 (Pipeline)
        private MarkdownPipeline _pipeline;

        public MarkdownRenderService()
        {
            // 初始�?Markdig 管道，启用所有高级扩�?(GFM: 表格、任务列表等)
            _pipeline = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseSoftlineBreakAsHardlineBreak()
                .Build();
        }

        public async Task LoadSkeletonAsync(WebView webView, string inlineCss, ElementTheme theme)
        {
            if (webView == null) return;

            var cssLink = theme == ElementTheme.Dark ? HljsCssDark : HljsCssLight;
            var sb = new StringBuilder();
            
            sb.Append("<!DOCTYPE html><html><head><meta charset='utf-8'>");
            sb.Append("<link rel=\"stylesheet\" href=\"").Append(cssLink).Append("\" />");
            
            if (!string.IsNullOrWhiteSpace(inlineCss))
            {
                sb.Append(inlineCss);
            }
            
            // 基础样式修复
            sb.Append("<style>");
            sb.Append("body { font-family: 'Segoe UI', sans-serif; line-height: 1.6; padding: 20px; }");
            sb.Append("pre { border-radius: 4px; padding: 0; overflow-x: auto; }"); 
            sb.Append("code { font-family: 'Consolas', 'Courier New', monospace; }");
            sb.Append("table { border-collapse: collapse; width: 100%; margin: 10px 0; }");
            sb.Append("th, td { border: 1px solid #555; padding: 6px; }");
            sb.Append("blockquote { border-left: 4px solid #ddd; padding-left: 10px; color: #777; margin: 10px 0; }");
            // 多彩引用块样�?(配合下面�?JS 注入)
            sb.Append(".alert-note { border-left-color: #0969da; background-color: #f1f8ff; color: #24292f; }");
            sb.Append(".alert-warning { border-left-color: #9a6700; background-color: #fff8c5; color: #24292f; }");
            sb.Append("</style>");
            
            sb.Append("</head><body style=\"margin:0;\">");
            sb.Append("<div id='content'></div>");
            sb.Append("<script src=\"").Append(HljsScript).Append("\"></script>");
            sb.Append("</body></html>");

            webView.NavigateToString(sb.ToString());

            // 【修复错�?CS0117�?Win8.1 不支�?Task.CompletedTask
            await Task.FromResult(0);
        }

        public async Task UpdateContentAsync(WebView webView, string markdown)
        {
            if (webView == null) return;

            // 1. 使用 Markdig �?Markdown 转为 HTML
            string html = Markdown.ToHtml(markdown ?? string.Empty, _pipeline);

            // 2. �?Base64 准备传输
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(html));
            
            var script = new StringBuilder();
            script.Append("var container = document.getElementById('content');");
            script.Append("if (container) {");
            script.Append("  var currentScroll = document.documentElement.scrollTop || document.body.scrollTop;");
            
            // 3. 更新 HTML (使用 UTF-8 修复方案)
            script.Append("  container.innerHTML = decodeURIComponent(escape(window.atob('").Append(payload).Append("')));");

            // 4. 后处理：代码高亮
            script.Append("  if (typeof hljs !== 'undefined') {");
            script.Append("    var blocks = document.querySelectorAll('pre code');");
            script.Append("    for(var i=0; i<blocks.length; i++){ hljs.highlightBlock(blocks[i]); }");
            script.Append("  }");

            // 5. 后处理：多彩引用�?(GFM Alerts 模拟)
            // 这里�?JS 简单的模拟 Note �?Warning
            script.Append("  var quotes = document.getElementsByTagName('blockquote');");
            script.Append("  for (var i = 0; i < quotes.length; i++) {");
            script.Append("    var c = quotes[i].innerHTML;");
            script.Append("    if (c.indexOf('[!NOTE]') !== -1) { quotes[i].className += ' alert-note'; quotes[i].innerHTML = c.replace('[!NOTE]', '<strong>NOTE</strong><br/>'); }");
            script.Append("    else if (c.indexOf('[!WARNING]') !== -1) { quotes[i].className += ' alert-warning'; quotes[i].innerHTML = c.replace('[!WARNING]', '<strong>WARNING</strong><br/>'); }");
            script.Append("  }");

            // 6. 恢复滚动
            script.Append("  window.scrollTo(0, currentScroll);");
            script.Append("}");

            try
            {
                await webView.InvokeScriptAsync("eval", new[] { script.ToString() });
            }
            catch { /* 忽略未准备好的异�?*/ }
        }
        public string ToHtml(string markdown)
            {
                // 使用之前配置好的 GFM 管道进行转换
                return Markdig.Markdown.ToHtml(markdown ?? string.Empty, _pipeline);
            }
    }
}