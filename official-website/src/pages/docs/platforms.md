---
layout: ../../layouts/DocsLayout.astro
title: 平台支持
eyebrow: 入门 · PLATFORMS
lead: Mark::Blocks 基于 Metro UI / Modern Design，覆盖一个时代的 Windows 设备。
---

## 支持矩阵

| 平台 | 最低版本 | 编写 | 预览 | Split View 双分屏 | 沉浸模式 |
| --- | --- | :-: | :-: | :-: | :-: |
| Windows | 8.1+ | ✓ | ✓ | ✓（独占） | — |
| Windows RT | 8.1+ | ✓ | ✓ | ✓ | — |
| Windows Phone | 8.1+ | ✓ | ✓ | — | ✓（独占） |

## Windows 8.1+

桌面、平板、二合一设备上的主战场。提供完整的 **Split View** 编写 / 预览双分屏体验，是体验 Mark::Blocks 的最佳平台。

- 支持宽屏下的双分屏同步滚动。
- 支持文件标签页平滑跳转。
- Accent 颜色可跟随系统。

## Windows RT 8.1+

ARM 架构设备（如 Surface RT、Surface 2、Nokia Lumia 2520）。由于架构一致，可直接复用 Windows 项目的全部功能，仅需将构建平台切换为 `ARM`。

> **注意**：Windows RT 已无法通过商店分发新应用，需使用开发者许可证侧载部署。

## Windows Phone 8.1+

手机端为小屏做了专门设计：

- **纯净编写 / 纯净预览**：在小屏上独立切换，避免双分屏拥挤。
- **沉浸模式**：隐藏文件标签页，最大化编辑空间。
- 主页以磁贴形式展示最近文件，符合磁贴交互直觉。

## 渲染层一致性

三个平台共享同一套 Markdown 渲染管线：MarkDig 解析 → IE11 WebView 渲染预览 → highlight.js / KaTeX / Mermaid 处理代码、公式与图表。差异仅在布局与交互层。
