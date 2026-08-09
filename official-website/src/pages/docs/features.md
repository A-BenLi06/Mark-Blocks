---
layout: ../../layouts/DocsLayout.astro
title: 功能总览
eyebrow: 使用 · FEATURES
lead: Mark::Blocks 的核心能力：为书写与预览而生。
---

## 核心功能

Mark::Blocks 围绕「书写 - 预览 - 输出」三件事构建，保持 Metro 的克制与专注。

### Markdown 书写

输入区基于 `RichEditBox`，提供：

- 流畅的纯文本输入。
- 完整的 **Undo / Redo** 支持。
- `Ctrl` + `F` 快速查找定位。

### Markdown 预览

预览区由 **IE11 WebView** 渲染，借助 IE11 的 `text-justify` 保证大块文本的整齐度。预览支持：

- 代码块高亮（highlight.js，atom-one-min light / dark）。
- 数学公式（KaTeX `0.11.1`）。
- 图表（Mermaid `7`，甘特图暂不可用）。

### 导出 HTML

一键将当前文档导出为独立 HTML 文件，包含完整样式，可离线分发。

## 特色小功能

| # | 功能 | 说明 |
| --- | --- | --- |
| 01 | 磁贴主页 | 主页以磁贴风格显示最近打开的文件。 |
| 02 | Metro 标签页 | 文件标签页支持平滑跳转（仍有较大优化空间）。 |
| 03 | Split View | Windows 独占编写 / 预览双分屏。 |
| 04 | 同步滚动 | 滚动 RichEditBox 同步，滚动 WebView 区域独立。 |
| 05 | 沉浸模式 | Windows Phone 独占，隐藏文件标签页。 |
| 06 | 三种预览底色 | 灰 `#1d1d1d`、AMOLED Black、Tokyo Night。 |
| 07 | 自动保存 | 可开关、可选择频率。 |
| 08 | Accent 颜色 | 可选与系统一致或默认青色。 |

## 渲染底色

预览 WebView 的底色支持三种风格，适配不同写作场景与 OLED 屏幕：

- **灰色** `#1d1d1d`：经典深灰，长时间阅读不刺眼。
- **AMOLED Black** `#000000`：纯黑，OLED 设备省电。
- **Tokyo Night**：复古夜色配色，氛围感强。

详见 [自定义设置](/docs/customization/)。
