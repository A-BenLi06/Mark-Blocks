---
layout: ../../layouts/DocsLayout.astro
title: 常见问题
eyebrow: 参考 · FAQ
lead: 关于构建、平台限制与渲染差异的常见问题与已知限制。
---

## 构建相关

### NuGet 包还原失败？

请确认 `nuget.org` 源可用，并在 Visual Studio 中对解决方案执行「还原 NuGet 包」。MarkDig `0.15.5` 等老版本包仍托管在官方源。

### 找不到 Windows 8.1 / Phone 8.1 工具？

新版 Visual Studio 安装器可能不再默认提供这些组件。可：

- 在「单个组件」中手动勾选相关工具。
- 或使用 Visual Studio 2015 / 2017 完成构建。

### 如何部署到 Surface RT / Lumia 真机？

将构建平台切换为 `ARM`，连接已开发者解锁的设备，按 <kbd>F5</kbd> 部署。RT 与 Phone 均需侧载。

## 平台限制

### 为什么不支持 Windows 10 / 11 UWP？

Mark::Blocks 是一款面向 Windows 8.1 时代 Metro 设计语言的应用，使用 WinRT 8.1 项目结构。在 Windows 10 / 11 上可通过兼容层运行，但未做 UWP 适配。

### Windows Phone 为什么没有 Split View？

手机屏幕宽度不足以承载双分屏，因此 Phone 端采用「纯净编写 / 纯净预览」的独立模式，并加入沉浸模式隐藏标签页。

## 渲染相关

### 甘特图为什么不可用？

受限于 Mermaid 7 与 IE11 WebView 的组合，甘特图组件在当前版本下暂不可用，其它图表（流程图、序列图等）正常。

### 公式渲染异常？

KaTeX `0.11.1` 在 IE11 下表现稳定。若出现排版错乱，请检查公式语法是否兼容该版本——较新的 KaTeX 语法可能不被支持。

### 预览与编写滚动不同步？

这是设计如此。请参考 [使用指南 - 同步滚动规则](/docs/usage/#同步滚动规则)：滚动编写区会带动预览区，滚动预览区不会带动编写区。

## 其它

### 项目由 AI 协作开发？

是的。Mark::Blocks 由以下模型辅助开发：GPT5-Codex、GPT5.1-Codex-Max、Gemini-2.5-Pro、Gemini-3.0-Pro、Claude Sonnet 4.5、Claude Opus 4.5。

### 在哪里反馈问题？

请前往 [GitHub Issues](https://github.com/A-BenLi06/MetroMarkdownEditor/issues) 提交问题或建议。
