---
layout: ../../layouts/DocsLayout.astro
title: 技术选型
eyebrow: 参考 · TECH STACK
lead: 为 Windows Phone 8.1 兼容性精心挑选的依赖，每一项都为那个时代的平台服务。
---

## 总览

| 用途 | 依赖 | 版本 | 说明 |
| --- | --- | --- | --- |
| Markdown 解析 | MarkDig | 0.15.5 | Windows Phone 8.1 支持的最后一个版本 |
| 代码高亮 | highlight.js | - | atom-one-min light / dark |
| 数学公式 | KaTeX | 0.11.1 | 渲染公式 |
| 图表 | Mermaid | 7.x | 图表支持（甘特图暂不可用） |
| 预览渲染 | IE11 WebView | 内置 | text-justify 保证文本整齐度 |
| 输入框 | RichEditBox | 内置 | WinRT 原生富文本控件 |

## 为什么是这些版本

Mark::Blocks 的目标平台锁定在 **Windows 8.1 / RT 8.1 / Phone 8.1**，这套依赖为兼容性精心选型：

- **MarkDig 0.15.5**：是仍能稳定面向 Windows Phone 8.1 的最后版本，后续版本逐步放弃了对老平台的支持。
- **KaTeX 0.11.1**：在 IE11 渲染环境下表现稳定，公式排版正确。
- **Mermaid 7**：兼容 IE11 的最后一个大版本；甘特图组件在当前组合下暂不可用。
- **highlight.js atom-one-min**：体积小、风格克制，契合 Metro 的扁平美学。

## IE11 WebView 与 text-justify

预览区使用 IE11 WebView 渲染。IE11 的 `text-justify` 实现能够在大段中文 / 英文混排时保持两端对齐的整齐度，这是 Mark::Blocks 选择 IE 内核而非其它渲染路径的关键原因之一。

## 富文本输入

`RichEditBox` 是 WinRT 平台的原生富文本控件，Mark::Blocks 将其用作 Markdown 纯文本输入框，同时利用其内置的 Undo / Redo 栈与文本操作能力，避免重复造轮子。

## 自定义渲染层

应用对前端资源（highlight.js、KaTeX、Mermaid）做了本地化打包，使其在无网络环境下也能完整渲染，符合 Metro 应用「自包含」的离线优先原则。
