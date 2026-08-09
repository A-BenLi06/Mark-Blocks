export interface DocsNavItem {
  slug: string; // e.g. "/docs/getting-started/"
  title: string;
  desc?: string;
}

export interface DocsNavGroup {
  group: string;
  items: DocsNavItem[];
}

export const docsNav: DocsNavGroup[] = [
  {
    group: "入门",
    items: [
      { slug: "/docs/getting-started/", title: "构建与运行", desc: "从源码构建 Mark::Blocks。" },
      { slug: "/docs/platforms/", title: "平台支持", desc: "Windows / RT / Phone 8.1+。" },
    ],
  },
  {
    group: "使用",
    items: [
      { slug: "/docs/features/", title: "功能总览", desc: "核心能力一览。" },
      { slug: "/docs/usage/", title: "使用指南", desc: "编写、预览、分屏、沉浸模式。" },
      { slug: "/docs/customization/", title: "自定义设置", desc: "主题色、预览底色、自动保存。" },
    ],
  },
  {
    group: "参考",
    items: [
      { slug: "/docs/tech-stack/", title: "技术选型", desc: "依赖与版本说明。" },
      { slug: "/docs/shortcuts/", title: "快捷键与操作", desc: "键盘快捷键与手势。" },
      { slug: "/docs/faq/", title: "常见问题", desc: "FAQ 与已知限制。" },
    ],
  },
];

export const docsFlat = docsNav.flatMap((g) => g.items);

export function getNeighbor(slug: string) {
  const i = docsFlat.findIndex((it) => it.slug === slug);
  return {
    prev: i > 0 ? docsFlat[i - 1] : null,
    next: i >= 0 && i < docsFlat.length - 1 ? docsFlat[i + 1] : null,
  };
}
