---
layout: ../../layouts/DocsLayout.astro
title: 构建与运行
eyebrow: 入门 · GETTING STARTED
lead: 从源码构建 Mark::Blocks，部署到 Windows 8.1 / Windows RT 8.1 / Windows Phone 8.1 设备。
---

## 环境要求

Mark::Blocks 是一款面向 **Windows 8.1 时代** 的 Metro / Modern 风格 Markdown 编辑器，基于 WinRT 平台构建。请准备以下开发环境：

- **Visual Studio 2019 / 2022**（需安装「Windows 8.1 / Windows Phone 8.1 工具」与多平台共享项目支持）
- **Windows 8.1 SDK** 与 **Windows Phone 8.1 SDK**
- .NET Framework 4.5.1 及以上
- 操作系统建议 Windows 10 / 11，用于运行模拟器与部署

> **注意**：Windows 8.1 / Windows Phone 8.1 工具在现代 Visual Studio 安装器中需手动勾选「单个组件」。如安装器不再提供，可使用旧版 VS 2017/2015 完成构建。

## 获取源码

```bash
git clone https://github.com/A-BenLi06/MetroMarkdownEditor.git
cd MetroMarkdownEditor
```

## 解决方案结构

```
MetroMarkdownEditor.sln
└─ MetroMarkdownEditor/
   ├─ MetroMarkdownEditor.Shared/   # 共享代码（VM、Service、Converter）
   ├─ MetroMarkdownEditor.Windows/  # Windows 8.1+ 桌面/平板端
   └─ MetroMarkdownEditor.WindowsPhone/  # Windows Phone 8.1+ 端
```

三个项目共用 `Shared` 中的代码，平台特定 UI 在各自项目内实现。

## 还原依赖

解决方案依赖以下 NuGet 包，首次打开时 Visual Studio 会自动还原：

- **MarkDig** `0.15.5`（Windows Phone 8.1 支持的最后一个版本）
- **KaTeX** `0.11.1`
- **highlight.js**（前端资源，atom-one-min light/dark）
- **Mermaid** `7.x`

> **提示**：若还原失败，可在「工具 -> NuGet 包管理器 -> 程序包管理器设置」中确认 `nuget.org` 源可用，并对解决方案执行「还原 NuGet 包」。

## 构建与部署

1. 打开 `MetroMarkdownEditor.sln`。
2. 在配置管理器中选择目标平台：`x86`、`x64`、`ARM`（用于 RT / Phone 真机）。
3. 选择启动项目：
   - `MetroMarkdownEditor.Windows` 调试桌面 / 平板体验。
   - `MetroMarkdownEditor.WindowsPhone` 调试手机端与沉浸模式。
4. 按 <kbd>F5</kbd> 部署到本地机器或模拟器，<kbd>Ctrl</kbd>+<kbd>F5</kbd> 直接启动而不附加调试器。

## 侧载与真机部署

Windows 8.1 / Windows Phone 8.1 需开发者解锁后才能侧载应用：

- **Windows**：使用 `Windows Phone Developer Registration` / `Show Windows Developer License Status` 工具激活开发者许可证。
- **Windows Phone**：通过 `Windows Phone Developer Registration` 解锁手机。

部署到 ARM 设备（如 Surface RT、Lumia 系列）时，请将平台切换为 `ARM` 并连接真机。

## 下一步

- 浏览 [平台支持](/docs/platforms/) 了解各端差异。
- 阅读 [功能总览](/docs/features/) 与 [使用指南](/docs/usage/) 开始上手。
