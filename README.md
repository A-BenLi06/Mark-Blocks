# Mark::Blocks(先前为MetroMarkdownEditor)
# 运行平台: 
Metro UI(Modern design), 支持windows phone 8.1+, windows 8.1+, windows RT 8.1+
由GPT5-Codex, GPT5.1-Codex-Max, Gemini-2.5-Pro, Gemini-3.0-Pro, Claude Sonnet 4.5, Claude Opus 4.5辅助开发
# 技术选型
1. highlight.js atom-one-min light/dark 代码块风格
2. MarkDig V0.15.5 Nuget包作为markdown渲染库(windows phone 8.1 支持的最后一个版本)
3. KaTex V0.11.1 渲染公式
4. IE11 Webview(废话)渲染markdown预览, 采用IE11的text-justify来保证大块文本的渲染整齐度
5. Mermaid 7 图标支持(甘特图暂时不可用)
6. 支持自定义主页背景
7. 较为丰富的磁贴样式支持
8. 使用RichEditBox作为Markdown输入框
# 功能
1. Markdown书写
2. Markdown预览查看
3. Ctrl+F查找
4. 导出为HTML
# 特色小功能
1. App主页以磁贴风格显示最近打开的文件
2. Metro风格文件标签页, 支持平滑跳转(有较大优化空间)
3. Windows/WindowsPhone端支持纯净编写/纯净预览 Windows独占支持编写预览双分屏(Split View)
4. SplitView(双分屏)支持Richeditbox与webview预览的同步滚动与分开滚动(滚动editbox是同步, 滚动webview区域则不会带动richeditbox
5. Windows Phone独占沉浸模式, 隐藏文件标签页
6. Markdown预览Webview底色支持灰色#1d1d1d, AMOLED Black, Tokyo Night三种风格
7. 可开关, 可选择频率的自动保存
8. accent color可选与系统保持一致或默认的青色
9. 有Undo/Redo功能
# 屏幕截图(windows)

<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/a69dbccf-3248-4c4d-b4c2-acb965868cdb" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/1e177168-0faa-43d5-b335-5bc4e12a03e9" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/c728b330-ef1a-4780-ab98-16ef0b7de65d" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/b603aa39-1f2c-44d9-81ac-1cd27aecaa46" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/59f61440-0fb6-4454-af7d-ac843604a52f" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/86f3a9d1-9309-4e16-91c8-f41c921dcac9" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/95e9c448-05d7-42ac-a637-6da8d6ecbc93" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/c06878df-6ec0-48f1-b326-c0c6cff2ea4a" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/6caaecdf-f906-4cfe-b782-7cd3c5762970" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/4118fe5e-5357-43b9-8763-d05d5ee63c9f" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/c494d8b4-07e4-4122-8ef1-4b9324f44494" />
<img width="1282" height="1002" alt="image" src="https://github.com/user-attachments/assets/a1d1b651-8493-4bdd-9fc5-ca6694426fb6" />
# 屏幕截图(Windows Phone)

<img width="551" height="1032" alt="image" src="https://github.com/user-attachments/assets/979f4712-b673-407b-a3bc-2a8238a94f35" />
<img width="514" height="969" alt="image" src="https://github.com/user-attachments/assets/55a585ed-b5d6-49ff-92d0-1f1e446fc0c2" />
<img width="454" height="808" alt="image" src="https://github.com/user-attachments/assets/ad9221be-99cd-4ae1-84b3-a42685296745" />
<img width="454" height="808" alt="image" src="https://github.com/user-attachments/assets/b29c52cf-483d-4a93-bebb-729e0adeafa0" />







