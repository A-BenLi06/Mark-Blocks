# 图片上传器弃用与在线图片缓存

## 上传链路结论

原 Windows 粘贴路径只读取 StorageItems，不读取剪贴板 Bitmap；把本地路径组成 `list` JSON 发送给默认 `http://127.0.0.1:36677/upload`。PicGo-Core/PicList 选项实际共用此 HTTP 路径，并未运行 EXE。Custom Command 从未执行。失败被吞掉后退回本地 Markdown 路径，因此设置看似可用却未完成上传。

Windows 8.1 Store 的常规沙盒发行不能直接启动任意桌面命令，本机回环网络也受限制；VS 调试中的豁免不代表普通安装环境可以运行。Windows 8.1 Update 的特殊侧载互操作部署另有要求。本项目不为此引入桌面代理、系统回环豁免或特殊部署，按用户要求弃用现有 uploader。

- 删除 HTTP 上传及密钥请求构造代码，移除 uploader、EXE、命令、地址、密钥及 YAML 自动上传配置入口。
- 旧的 UploadImage 插入选项迁移到 NoSpecialAction，上传器迁移到 None；保留旧枚举/属性用于设置兼容，但没有上传执行路径。
- 图片文件粘贴仍生成本地 Markdown 引用；截图 Bitmap 自动上传没有提供。不能将本地上传器不可用解释为所有远程 HTTPS 图床 API 都不可实现，后者需要另选具体协议和认证方案。

参考：[Windows 8.1 侧载互操作说明](https://blogs.windows.com/windowsdeveloper/2014/04/03/new-features-for-side-loaded-apps-in-windows-8-1-update/)、[微软 Windows Store 编程指南（含回环限制）](https://download.microsoft.com/download/6/6/5/665AF7A6-2184-45DC-B9DA-C89185B01937/Microsoft_Press_eBook_Programming_Windows_8_Apps_HTML_CSS_JavaScript_2E_PDF.pdf)。

## 在线缓存

### 同步滚动修复后的加载路径

预览入口现已改为 Windows 8.1 `NavigateToLocalStreamUri`。缓存命中和新缓存写入后返回短的 `/image/<sha256>.cache.<extension>` 地址，原生 `IUriToStreamResolver` 在后台解码缓存内容并提供图片字节，不再通过 InvokeScriptAsync 传输整张 Base64。旧的 data URI 缓存文件仍可读取；no-store 或不能落盘的响应使用原网址。缓存文件若在浏览器读取前被淘汰，图片错误处理会回退原网址。

针对打开文件时报告的 `Specified cast is not valid`，资源解析器已将 `MemoryStream.AsInputStream()` 转接流替换为原生 `InMemoryRandomAccessStream`，避免旧 WebView 查询原生流接口时的兼容性问题。写入完成后分离 DataWriter 并将位置归零，保持返回流可供 WebView 读取；失败时释放流。Windows x86 / Phone ARM Release 重新构建通过，但该异常只有消息、没有调用栈，最终归因和运行效果仍需目标 WebView 验证；纯托管测试中的解析器是替身，不覆盖此接口边界。

Windows 原生同步不再受输入的 700ms 防抖阻挡，中间事件以 1px 而非全文 0.2% 过滤，仍合并为 33ms 单个在途调用。原生着色和新解析等待滚动静默，WebView 的图片回填及公式/图表等工作等待 180ms 静默。离屏图片保留显示高度直到真实图片重新加载，避免占位图瞬间收缩引起比例同步跳动。

浏览器用例验证滚动期间的回填/重处理暂停，以及停止后的恢复；一万段落场景在空闲处理时仍为 86 次几何读取。缓存核心用例新增确认命中和首次下载均可返回短资源地址。Windows/Phone 构建通过，原生资源解析器需要目标机检查，不以 Edge 文件页测试代替。

预览近屏图片通过 `ScriptNotify` 请求原生缓存服务，服务返回本地资源地址给相应图片节点。原文、HTML 的原始 URL 元数据均保留，因此保存和导出不会写入缓存路径。此桥接在 ms-local-stream 内容上受 Windows 8.1 支持：[ScriptNotify 文档](https://learn.microsoft.com/en-us/uwp/api/windows.ui.xaml.controls.webview.scriptnotify)。

- 缓存目录为应用 LocalFolder/OnlineImageCache，以完整 URL 的 SHA-256 命名，供不同文档和后续会话复用。
- 仅在图片接近视口时加载；同 URL 并发合并，同时最多下载两张。磁盘和编码工作不在 UI 线程执行。
- 默认上限 128 MB，Image 设置可输入 0–4096 MB，0 表示禁用并清空已有缓存。容量统计包含 base64 编码和文件字节开销，并非原图大小。
- 按写入时间从旧到新淘汰；应用上限会立即清理超额。清理与写入互斥，清理前开始的下载不会重新写回磁盘。
- 提供占用大小显示、刷新大小和手动清理。清理不删除 Markdown/本地原图，也不强制移除正在显示的图片。
- HTTP/HTTPS 图片按常见 image MIME 类型缓存；单图下载上限 16 MB，下载超时 20 秒，实际允许大小还受用户总上限约束。显式 no-store 响应只显示、不落盘。
- 下载失败、类型不支持、超大图片或缓存关闭时回退为 WebView 原网址加载。缓存不是离线下载器，只缓存已浏览到的图片；不会主动重新验证已缓存的同 URL 内容，需要更新时清理缓存再重开预览。
- 缓存加载器共享给 Windows 和 Phone；容量管理入口加入现有 Windows Image 设置页。

## 验证

Windows x86 / Phone ARM Release 构建通过。缓存核心直接进入现有 .NET 回归工程，使用模拟 HTTP 和存储边界，覆盖复用、新实例命中、超限淘汰、禁用、并发去重、下载中清理、非图片响应、no-store、超大图片、非法协议。另有 Edge 测试验证实际预览脚本的桥接去重、回填与滚动释放后的原 URL 保留，原预览检查也通过。

这些测试不替代目标机的 Windows.Storage 持久化和旧 WebView 渲染验收。没有连接用户的真实图床、上传用户图片或修改系统沙盒设置。
