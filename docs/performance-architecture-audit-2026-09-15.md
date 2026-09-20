# 大文件、输入响应与渲染架构盘查

日期：2026-09-15。代码基线：`a0085a8`（深度优化编辑器输入与预览性能）。

结论：在继续支持 Windows 8.1 Store / Windows Phone 8.1、保留现有 Markdown 功能的前提下，建议保留 RichEditBox + Markdig + WebView，局部重构版本管理、调度和 DOM 更新。当前代码尚未触及可以证明必须更换控件的边界。不建议直接投入“新输入控件 + 自建 XAML Markdown 引擎”的双重重写。

这是源码盘查和平台资料核对，未取得当前版本在目标设备上的输入延迟、帧时间或内存曲线。因此下文的瓶颈指可证实的工作量和风险，优先级是工程判断；不把它们冒充 CPU 热点排名，也不声称已经测得某种架构绝对最快。

仓库有 2026-08-10 的 `.vspx` 采样文件，但本次没有解析这些二进制报告，也不能把旧报告视为当前提交的性能证据。本次没有修改运行时代码，没有运行应用构建或目标设备交互测试。

## 1. 当前链路和已经做对的事情

平台为 Windows 8.1 / WP8.1 的 C#/XAML Universal 工程，使用 Markdig 0.15.5。不是 WinUI / WebView2 项目。

```text
打开文件
  FileIO.ReadTextAsync → Document.Content → RichEditBox.SetText（UI）
  → 当前可视区高亮 → 后台全文 Markdig → UI 提交结果 → WebView

输入
  RichEditBox.TextChanged：标记版本、脏缓冲、重启计时器
  → 停顿 160ms：UI GetText + 换行规范化 + Content 快照
  → Task.Run：全文 AST、各块 HTML、全文 HTML、大纲
  → UI：图片路径处理、再次拼全文 HTML、比较所有块
  → 少量块替换，或整篇 innerHTML 替换

高亮
  停顿 650ms → 可视范围及余量文本 → 正则 → 原生文本颜色变更
```

已经存在的优化不应重复开发：

- Windows 和 Phone 均把解析放入后台，并用文档身份、编辑版本排除部分过期结果。
- TextChanged 已经没有常规逐键全文 GetText；使用 160ms 防抖。
- 高亮已经按视口取文本，并使用 BatchDisplayUpdates；`forceFullDocument` 参数实际已不再表示全文高亮。
- 已经使用原生撤销，取代历史上全文字符串快照式撤销。
- Windows 已有同块数、最多四个变化块的 DOM 更新；WebView 骨架复用。
- 公式、代码高亮、Mermaid 延迟到附近视口；图片有激活和远离视口释放逻辑。
- 挂起、导航离开和若干保存入口已经主动刷新编辑缓冲。不能笼统判定为“挂起没有保存最新输入”。

## 2. 主要发现

下文 `Shared/`、`Windows/`、`WindowsPhone/` 分别简写仓库中的 `MetroMarkdownEditor/MetroMarkdownEditor.Shared/`、`MetroMarkdownEditor/MetroMarkdownEditor.Windows/`、`MetroMarkdownEditor/MetroMarkdownEditor.WindowsPhone/`。行号对应上述基线。

### A. 大文件打开仍是全文驻留，异步读取没有消除 UI 装载

证据：`Shared/ViewModels/DocumentViewModel.cs:83`，`Windows/EditorPage.xaml.cs:1840` 的 SyncEditorText，以及 `:1866` 的 SetEditorTextWithoutNotification。

ReadTextAsync 解决的是等待文件读取时不阻塞调用线程；随后 SetText 仍把整篇交给原生富文本模型。文本长度、换行密度、超长行折行、字体和原生布局都会影响首次可编辑时间。

同时存在模型字符串、规范化字符串、RichEdit 内部文本、后台 AST、分块源文本与 HTML、汇总 HTML 和 DOM。部分字段可能共享同一字符串引用，不能按字段数直接算复制倍数；但 Substring、HTML 拼接和桥接编码确实会新增存储。

不要先把 ReadTextAsync 改成很多小块反复 SetText。它不能获得真正的编辑器虚拟化，反而可能重复布局、触发事件和干扰撤销。流式读取只有在实测 I/O、解码、峰值内存或需要取消/进度时才值得实施，最后一次全文原生装载仍需单独解决。

### B. 输入热路径变轻了，但每次短暂停顿仍触发全文 UI 工作

证据：`MetroMarkdownEditor/MetroMarkdownEditor.Windows/EditorPage.xaml.cs:1521`、`:1631`、`:1649`。

160ms 到期后先 FlushPendingEditorText，再判断是否隐藏预览。因此纯写作模式虽然省去了常规解析，仍会在短暂停顿后 GetText 和规范化全文。对大文件，用户会感受到“正在打字还好，稍停一下继续打就卡”。这是待实机验证的表现推断。

后台任务完成后若还有请求，finally 直接投递下一轮 TypingTimer_Tick，不重新核验最后输入距今是否已经达到防抖时间。慢解析可能演变成连续追赶全文版本。现有版本检查能拒收旧结果，却不能收回已经做完的解析工作。

建议把编辑版本、模型快照版本、解析版本、DOM 版本分开；只保留一个运行任务和一个最新请求。输入后 UI 只标记 dirty/revision；纯写作模式采用独立快照策略，保存和切换时强制刷新。下一轮任务必须重新核验输入静默期与负载预算。

### C. DOM 的增量更新容易退化为整篇替换

证据：`Windows/EditorPage.xaml.cs:1105`、`:1601`、`:1608`；`Shared/Services/MarkdownRenderService.cs:1500` 的 WrapBlockHtml。

1. 块数改变即拒绝 diff：新增/删除段落很容易整篇替换。
2. 块相等判断比较整个 Html，其中包含 `data-block`、`data-start`、`data-end`。在文章前面增加一行，后面段落即使正文不变，行号变化也会让 HTML 不相等。
3. 超过四块变化即整篇替换。上述元数据连锁变化正好容易跨过阈值。
4. UpdateContentAsync 用 UTF-8 字节数组、Base64、脚本字符串传全文，再执行 `container.innerHTML = ...`。原有 DOM 和已增强的公式/图表可能被重新建立。
5. Phone 的 RenderPreviewAsync（`:999` 起）直接使用 UpdateContentAsync，没有 Windows 的分块 diff 路径。

应先把渲染正文、稳定块标识和源位置元数据分离；支持 insert/remove/replace 的补丁集合，一次桥接提交多个补丁。源行号变化不应使正文失效。稳定标识需要处理重复段落、块拆分和合并，不能只用内容 hash 当唯一 ID。

先继续全文解析以保住 Markdown 语义，优先省掉无谓的全文 DOM 重建。这比直接研发增量 Markdown 解析器更稳妥。

### D. 当前预览是延迟增强，并非 DOM 虚拟化

证据：`Shared/Services/MarkdownRenderService.cs:1100` 附近图片遍历、`:1195` 的 __mdCollectBlocks、`:1206` 的 __mdProcessVisibleBlocks。

所有普通段落仍在 DOM。滚动时 querySelectorAll 枚举图片和块，图片逐个读几何位置；块遍历跳过已处理项，但仍可能扫描大量离屏项。远离视口的代码和公式生成节点没有通用回收机制。

“每轮两个块”只限制块数，不限制处理时长。一个很长的代码块、巨大表格或复杂 Mermaid 就可能超过一帧；只把同步工作包进 setTimeout 也不会使该工作内部可抢占。

近期：缓存块/图片索引，定位视口候选，使用实际耗时预算，按内容复杂度限制高亮、行号和图表自动执行。长期大文件模式：窗口化挂载段落、占位高度、局部高度缓存、锚点纠偏；超大单块另设展开/原文降级，不将整篇列表当成不可分割单位。

虚拟化需要补齐全文搜索、复制、目录跳转和导出。不能让这些功能只看到当前挂载的 DOM。

### E. 高亮存在超出视口的工作，且撤销补偿成本很高

证据：`Shared/Services/MarkdownHighlightingHelper.cs:49`、`:78`、`:101`；`Windows/EditorPage.xaml.cs:1703`。

- 高亮最后对 `doc.Selection.CharacterFormat.ForegroundColor` 赋色。若用户 Ctrl+A 或选中极长范围，这个调用针对整个选区，突破“只处理视口”的目标。
- GetRangeFromPoint 失败时回退使用选区起止，没有硬字符上限；随后还扩展到行边界。大选区和长行都应专门测试。
- 高亮进入 BeginUndoGroup。代码自身已考虑格式变更进入原生撤销历史的情况，撤销时最多尝试 16 个历史项，每次 GetNormalizedEditorText 做全文提取比较；成功后还可能再 Flush 一次。
- Windows 的滚动高亮刷新挂在 Split 模式和 WebView ready 检查之后。Write 模式滚动不会通过这一路刷新高亮；Phone 有独立滚动处理。这是功能与预览调度不必要耦合的例子。
- 未发现显式 IME composition 状态管理。自动配对依赖 CharacterReceived + TextChanged，格式刷新又会恢复选区。是否打断中文组合输入不能靠静态审查定论，需要目标机用微软拼音、注音、触摸键盘验证。

近期：非空选区不对整体赋色；限制范围长度和本轮着色次数；只更新格式差异；滚动高亮独立于预览模式。输入法组合期间延期自动配对、着色和主动定位，具体事件/API 必须针对 8.1 验证，不能套用较新 UWP 的输入 API。

大文件优先提供无着色编辑降级，保住原生 IME 和撤销。不要把 UndoLimit 临时设为 0 当作常规高亮解决办法，那会破坏用户历史。也不要把“连续跳过 16 次”当长期撤销设计。

### F. 全文解析之后，UI 上还有全文输出加工

证据：`Shared/Services/MarkdownRenderService.cs:725`、`:1426`、`:1487`；`Shared/ViewModels/EditorViewModel.cs:477`。

RenderMarkdown 每次解析整篇，渲染全部顶层 AST，保存每块源文本，拼一份完整 HTML。ApplyPreviewResult 又遍历块做图片规范化，并再次拼接 PreviewContent；随后 UI 再比较整篇块列表。

对有大量图片的文档，还有图片相关正则和路径计算。BuildBlocksFromDocument 的 StringBuilder 累积整篇原始渲染结果，再逐块提取；峰值时有多种 HTML 表示并存。

建议后台产出不可变 RenderResult：块内容、源区间、大纲、内容签名与补丁；UI 只核验版本和投递 DOM。图片基路径与渲染设置作为任务启动时的快照传入；HTML 全文仅全量重置和导出时按需拼接。分块 Text 在不需要时换成源区间，避免每次 Substring。

完整 AST 的增量解析属于后续阶段。引用链接、脚注、列表紧松状态、围栏、重复标题 ID 等都有跨块依赖，不能简单“只重新解析当前行”而保证等价。

### G. 首屏骨架加载了没有用到的重资源

证据：`Shared/Services/MarkdownRenderService.cs:175` 起读取多个资源；`:639` 起内联 Mermaid、KaTeX、高亮脚本；`:720` NavigateToString。

即使纯文字文档也会读取和装载这些库。静态字符串缓存避免部分重复文件读取，但骨架重建仍需拼接、传输和执行脚本。部分 polyfill 还在骨架中重复插入。

建议骨架成为包内静态 HTML/JS/CSS，常驻定义补丁函数；文档/主题变化只发送数据与样式，按首次使用懒加载 KaTeX/Mermaid/高亮，保留离线资源。当前每次更新重复拼接较长脚本定义也可由此去除。

Windows 8.1 官方支持包内内容以及本地流 URI；可用这些平台现有能力组织资源，无需假设 WebView2 能直接替换进来。[Windows 8.1 WebView 官方说明](https://blogs.windows.com/windowsdeveloper/2013/07/17/whats-new-in-webview-in-windows-8-1/)

### H. 保存、预览状态一致性要先于更激进的延迟优化

证据：`Shared/ViewModels/DocumentViewModel.cs:96`；`Windows/EditorPage.xaml.cs:1458`、`:1475`、`:527`；`Shared/ViewModels/EditorViewModel.cs:96`。

- SaveAsync 捕获旧 Content 写入，await 后无条件 IsDirty=false。如果写入期间新内容已被提交到模型，会错误清除新修改的脏标记。大文件更容易拉长这个窗口。应保存捕获的文档和版本，只更新 savedRevision；dirty 由当前版本与已保存版本决定。
- 手动保存、自动保存、挂起保存需要同一文档写入串行化，不能只用 IsSaving 做显示状态。另存为和文件选择器 await 之后也应继续操作最初捕获的文档。
- 自动保存计时器在每次输入后重启，语义接近“停笔 N 分钟后保存”；持续写作可能一直不保存。若产品期望周期性保护，应增加最长未保存时限，未命名文档另存恢复草稿。
- Windows 在 Write 模式编辑并等待 160ms 后，模型更新但解析请求被消费；模式切回 Split/Preview 时只调用 RenderPreviewAsync 展示已有结果。ViewMode setter 没有发出重新解析请求，因此可沿这条路径显示旧预览，直到下次刷新。应按 textRevision 与 renderedRevision 决定补解析。
- UpdateContentAsync/UpdateBlockAsync 吞异常，JS 在找不到容器/目标时也静默返回；上层可能仍推进 _lastBlocks。需要返回成功标志/版本 ACK，失败时失效缓存并受控重建，避免永久停留在不一致状态。
- 设置变化没有独立 settingsRevision。后台线程从可变设置单例读取，旧设置结果可能短暂提交。任务启动时捕获不可变设置快照，并共同校验文档、文本、设置、页面生命周期和骨架版本。

## 3. 保留架构时的推荐终态

不是继续给每个功能加一个计时器，而是统一文档会话：

```text
DocumentSession：documentId / editRevision / snapshotRevision / savedRevision
EditorAdapter：RichEditBox 访问、选区、IME、Undo、快照
PreviewScheduler：一个在跑 + 一个最新待处理任务；静默期及耗时反馈
MarkdownRenderer：后台 AST → blocks / outline / render signatures
PreviewPresenter：骨架版本 + 批量 patch + DOM ACK + 滚动锚点
```

共享调度、版本、补丁和保存机制，Windows/Phone 保留各自 UI 适配。避免两端继续复制同一套状态机。RichEditBox 依然可以作为当前活动文档的文本权威来源，保存/切换时获取一致快照；本阶段不需要为了“架构漂亮”立刻引入 piece table。

推荐实施顺序：

| 阶段 | 改动 | 收益与验收 |
|---|---|---|
| P0 正确性 | 保存版本/串行化、切模式补解析、DOM ACK、独立设置版本 | 保存期间再输入不会丢 dirty；切预览不会停在旧文档；桥接失败可恢复 |
| P1 输入隔离 | 纯写作独立快照、统一 latest-only 调度、高亮选区限制、长行降级、IME/撤销修复 | 暂停后续打、Ctrl+A、Ctrl+Z 的 UI 长任务减少；中文组合输入完整 |
| P1 预览补丁 | 内容与位置分离、稳定 ID、插删替换批量补丁、后台加工、懒拼全文 | 在文章首部新增段落不重建后文 DOM，未变图表不重跑 |
| P2 首屏和资源 | 静态骨架、按需加载脚本、首次可编辑优先、统一打开入口 | 纯文字文档不等待公式/图表库，编辑就绪与预览就绪分开记录 |
| P2 大文件模式 | 自适应节流、关闭高成本增强、预览窗口化、缓存淘汰 | 常驻节点有界；小修改传输量与变更量相关 |
| P3 条件研发 | 增量解析、替换编辑器原型 | 仅在前面完成后仍有实测瓶颈才投入 |

调度可先实验小文档 160–250ms、大文档 500–1000ms 停笔刷新；这些是待调参起点，不能直接当正式性能标准。更可靠的是看上一轮快照耗时、解析耗时、DOM 更新耗时、字符数、最大行长、块数和重内容数量。隐藏预览默认暂停解析，但保留 pending revision；重新显示时优先刷新最新快照。

对超大文件明确降级：先源文本编辑，默认暂停自动预览和语法着色；预览按需/按章节查看。仍使用整篇 RichEdit 时，它有全文装载的硬成本，不能承诺无限大文件。只有真正改成窗口化编辑模型，才可能从根本上改变这部分增长规律。

## 4. 是否更换输入和渲染方式

| 方案 | 潜在收益 | 主要成本/边界 | 判断 |
|---|---|---|---|
| RichEditBox + 优化后的 WebView | 保留原生输入、既有功能，减少 UI 工作和 DOM 重建 | 整篇 RichEdit 装载/GetText 仍是上限 | 当前首选 |
| TextBox + WebView | 去掉富文本格式与相关撤销污染，实现相对简单 | 无源码高亮；全文文本/布局成本仍在，速度必须 A/B 验证 | 值得做低成本对照实验 |
| CodeMirror 5 + WebView 预览 | 有成熟文本模型、变更范围、编辑区视口渲染，可减少原生全文快照依赖 | 8.1 WebView 中的中文/触摸/无障碍、焦点和桥接需真机验收；预览 JS 仍可能拖累输入 | 原生编辑器确认成为瓶颈后做候选原型 |
| RichEditBox + 虚拟化 XAML 阅读器 | 普通段落的原生外观、去掉部分 HTML 桥接与浏览器工作 | 完全不解决 RichEdit 输入问题；XAML UI 线程布局仍在 | 仅当主要瓶颈确在 WebView 且可缩减语法时考虑 |
| 自建编辑器 + XAML Markdown 渲染器 | 可从文本存储到视口排版全链路控制 | 同时承担文本模型、输入法、选择、撤销、排版与 Markdown 呈现 | 当前不推荐 |
| 自建窗口化输入 + 保留 WebView | 把重构集中在编辑瓶颈，复用预览功能 | 自建输入系统依旧昂贵 | 强制超大文件编辑且平台不能迁移时的后续路线 |
| 现代桌面平台 + 成熟编辑器/浏览器内核 | 能利用新的生态与运行时 | 改变 Win8.1/WP8.1 支持承诺与部署方式 | 产品允许迁移时另立方案，不视为原项目小改 |

CodeMirror 5 官网列出旧 IE 支持，所以它是可验证的候选，而非已验证的替换方案。现代编辑器也不能仅凭产品名判断兼容性，需锁定具体版本、构建产物、polyfill 和目标 WebView 测试。[CodeMirror 5 官方浏览器支持](https://codemirror.net/5/index.html)

InvokeScriptAsync 虽然返回异步操作，但长脚本仍可能造成应用无响应，不能把 await 当作 UI 不受影响的保证。[微软 InvokeScriptAsync 说明](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.controls.webview.invokescriptasync)

### 为什么“自建 XAML 引擎”不自动更快

应继续复用 Markdig 的 AST，替换的是呈现层，而不是重新发明 Markdown 解析器。只有从一开始按块虚拟化、复用元素、控制布局失效，才有机会更快；把所有内容变成一个巨大 RichTextBlock 或成千上万的 TextBlock/Run 同样会有 UI 布局和内存压力。

除了标题和段落，还需承接当前已有的管道/网格表格、任务列表、嵌套引用、脚注、图片、链接、代码行号、公式、图表、样式、全文选择复制、搜索和目录定位。虚拟化列表天然只保留局部元素，跨块复制和连续选择需要额外设计；超长表格/列表也不能只靠顶层列表容器解决。

KaTeX/Mermaid 目前是浏览器实现。转原生需要重做或另找兼容实现；转图片要处理缩放、尺寸和缓存，并失去部分选择能力；保留一个 WebView 作为复杂块回退则变成混合引擎，维护与同步更复杂。不建议为每个复杂块创建一个 WebView。

如果产品仅要“标题、段落、基础列表、代码、图片”的轻量阅读视图，XAML 是合理专项；如果要维持当前功能等价，它不是本轮最划算的性能方案。

## 5. 测量方案与重构触发条件

在实际 Windows 8.1 目标机、至少一台低配置/ARM 目标上运行 Release 版本，Windows 和 Phone 分开记录。现代 Windows 上的相同应用只能提供辅助数据，不能代替旧 WebView、原生输入和内存限制。

语料：100KiB、1MiB、5MiB、20MiB、50MiB；正常中文/英文文章、多短段落、超长单行、单个巨型代码块、深列表/大表格、图片/公式/Mermaid 密集文章。大尺寸样本按设备能力分阶段执行；这些是测试档位，不是承诺支持上限。

场景：冷打开、热打开、切回已打开标签；连续输入、停顿续打、中文组合输入；首段插行/插段、末尾追加、10 万字符粘贴、Ctrl+A 后输入、50 次撤销重做、Shift+Tab、搜索、快速滚动；保存中继续输入、自动保存中切标签、切模式、挂起恢复。

埋点应分开：

- 文件读取/解码；SetText；首个可编辑帧；首次完整预览。
- GetText 与规范化；Markdig 解析；分块 HTML 生成；UI ApplyPreviewResult；补丁计算。
- 桥接字节量与等待；JS DOM 修改与重内容增强；布局/呈现帧。JS ACK 并不等于已经绘制。
- 按键到可见字符的 p50/p95/p99、最长 UI 停顿；计时器 Tick 不能代表真实输入延迟。
- 私有内存/工作集、托管分配与 GC、DOM 节点数、挂载块数、图片/图表缓存；只看 GC 内存会漏掉原生 RichEdit 和 WebView。
- 解析启动次数/丢弃结果比例、整篇 DOM 替换次数、每次输入更新字节量。

建议实验门槛：目标语料上的按键到显示 p95 尽量不超过 50ms，常规连续操作不产生超过 100ms 的明显停顿；主线程可切片工作以约 8ms 为初始预算。这些是产品验收起点，需要结合目标硬件与用户期望确认，绝非本次实测结果。

对照组按顺序运行：①源编辑且关高亮/预览/拼写检查/打字机居中；②仅加高亮；③后台解析但不更新 WebView；④加 DOM 更新；⑤加公式图表。这样才能区分原生编辑、应用快照、高亮、解析竞争与浏览器工作。

重构判据：

1. **关掉应用附加工作后，RichEdit 的 SetText、输入、折行、Undo 仍无法满足实际目标**：先做 TextBox 与 CodeMirror 原型对照，必要时更换输入。只换 XAML 预览不会解决它。
2. **输入基线达标，只有预览更新/滚动不达标**：先完成补丁、资源懒加载和 DOM 窗口化；仍失败且业务接受功能收缩，再试虚拟化 XAML 阅读器。
3. **目标明确是几十/上百 MB 任意位置持续流畅编辑**：把独立文档存储、变更范围、行索引、窗口化排版作为核心设计需求。piece table/rope 本身不解决 RichEdit 全量同步，更不解决原生 IME 接入。
4. **必须同时保有全部 Markdown 功能和超大文件编辑，而老平台成为硬限制**：评估平台迁移的总成本，通常比同时自建两套成熟引擎更值得比较。

建议先交付 P0 + P1，并用上述对照组收集数据。当前最有价值的重构是文档会话和渲染调度层，而不是同时推翻两端控件。
