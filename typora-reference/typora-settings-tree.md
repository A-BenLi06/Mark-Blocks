# Typora Preferences Settings Tree

Source: screenshots in `typora-reference` captured on 2026-06-06.

Note: this tree contains the settings and submenu/dropdown values visible in the provided screenshots. `Files` and `General` are visible in the sidebar, but their page contents are not included in the screenshots.

```text
Typora
└─ Preferences
   ├─ Sidebar
   │  ├─ Search for...
   │  ├─ Files
   │  │  └─ Page content not shown in screenshots
   │  ├─ Editor
   │  ├─ Image
   │  ├─ Markdown
   │  ├─ Export
   │  ├─ Appearance
   │  └─ General
   │     └─ Page content not shown in screenshots
   │
   ├─ Editor
   │  ├─ Note
   │  │  └─ Some options will be applied after restart.
   │  ├─ Indent Size on Save
   │  │  ├─ Description: only apply to quotes and lists created from menu bar or hybrid view
   │  │  ├─ Option: Use the preferred indentation
   │  │  └─ Dropdown values
   │  │     ├─ Auto
   │  │     ├─ 2
   │  │     ├─ 3
   │  │     ├─ 4
   │  │     ├─ 5
   │  │     └─ Tab
   │  ├─ Auto Pair
   │  │  ├─ Auto pair brackets and quotes
   │  │  └─ Auto pair common Markdown syntax
   │  ├─ Auto Complete
   │  │  └─ Enable autocomplete for Emojis
   │  ├─ Live Rendering
   │  │  └─ Display source for simple blocks (including headings, etc.) on focus
   │  ├─ Default Copy Behavior (ctrl+c)
   │  │  ├─ Copy Markdown source as plain text
   │  │  └─ Copy or cut the whole lines that have cursors on them, if there is no selection when doing copy / cut.
   │  ├─ Default Line Ending
   │  │  ├─ Description: Line ending for new file
   │  │  ├─ LF (Unix Style)
   │  │  └─ CRLF (Windows Style)
   │  ├─ Spell Check
   │  │  ├─ Description: Default spell check option
   │  │  └─ Dropdown values visible across screenshots
   │  │     ├─ Auto Detect Language
   │  │     ├─ Disable Spell Check
   │  │     ├─ English (US)
   │  │     ├─ English (UK)
   │  │     ├─ Čeština
   │  │     ├─ Dansk
   │  │     ├─ Deutsch
   │  │     ├─ Español
   │  │     ├─ Ελληνικά
   │  │     ├─ Français
   │  │     ├─ Galego
   │  │     ├─ Hrvatski
   │  │     ├─ Italiano
   │  │     ├─ Limba Română
   │  │     ├─ Nederlands
   │  │     ├─ Magyar
   │  │     ├─ Orang Melayu
   │  │     ├─ Polski
   │  │     ├─ Português (Brasil)
   │  │     ├─ Português (Portugal)
   │  │     ├─ Русский
   │  │     ├─ Schweizerdeutsch
   │  │     ├─ Slovenčina
   │  │     ├─ Slovenščina
   │  │     ├─ Svenska
   │  │     ├─ Tiếng Việt
   │  │     ├─ Türkçe
   │  │     ├─ Українська мова
   │  │     ├─ فارسی
   │  │     ├─ العربية
   │  │     ├─ 한국어
   │  │     ├─ 中文
   │  │     ├─ 日本語
   │  │     └─ עברית
   │  └─ Typewriter Mode
   │     ├─ Always keep caret in middle of screen when typewriter mode is enabled
   │     └─ Button: Turn off Typewriter / Focus Mode
   │
   ├─ Image
   │  ├─ When Insert...
   │  │  ├─ Learn More...
   │  │  ├─ Dropdown values
   │  │  │  ├─ No special action
   │  │  │  ├─ Copy image to current folder (./)
   │  │  │  ├─ Copy image to ./assets
   │  │  │  ├─ Copy image to ./${filename}.assets
   │  │  │  ├─ Upload image
   │  │  │  └─ Copy image to custom folder
   │  │  ├─ Apply above rules to local images
   │  │  ├─ Apply above rules to online images
   │  │  └─ Allow upload images automatically based on YAML settings
   │  ├─ Preferred Image Syntax
   │  │  ├─ Learn More...
   │  │  ├─ Use relative path if possible
   │  │  ├─ Add ./ for relative path
   │  │  └─ Auto escape image URL when insert
   │  └─ Image Upload Setting
   │     ├─ Learn More...
   │     ├─ Image Uploader
   │     │  └─ Dropdown values
   │     │     ├─ None
   │     │     ├─ PicGo-Core (command line)
   │     │     ├─ PicList
   │     │     └─ Custom Command
   │     ├─ PicList Path
   │     │  └─ C:\Program Files\PicList\PicList.exe
   │     ├─ Button: Test Uploader
   │     ├─ Button: Download PicList
   │     └─ Link: Instructions
   │
   ├─ Markdown
   │  ├─ Note
   │  │  └─ Following preferences will be applied after restart.
   │  ├─ Syntax Preference
   │  │  ├─ Description: only apply to quotes and lists created from menu bar or hybrid view
   │  │  ├─ Strict Mode
   │  │  ├─ Learn More...
   │  │  ├─ Heading Style
   │  │  │  └─ Dropdown values
   │  │  │     ├─ atx (#)
   │  │  │     ├─ setext (===)
   │  │  │     ├─ atx with suffix (# ... #)
   │  │  │     └─ variable-width setext (=====)
   │  │  ├─ Unordered List
   │  │  │  └─ Dropdown values
   │  │  │     ├─ -
   │  │  │     ├─ +
   │  │  │     └─ *
   │  │  └─ Ordered List
   │  │     └─ Dropdown values
   │  │        ├─ 1. ... 2. ... 3. ...
   │  │        └─ 1. ... 1. ... 1. ...
   │  ├─ Syntax Support
   │  │  ├─ Auto Links (e.g: https://typora.io)
   │  │  ├─ Inline Math (e.g: $LaTeX$)
   │  │  ├─ Subscript (e.g: H~2~O)
   │  │  ├─ Superscript (e.g: X^2^)
   │  │  ├─ Highlight (e.g: ==key==)
   │  │  ├─ Github Style Alert
   │  │  └─ Diagrams (Sequence, Flowchart and Mermaid)
   │  │     └─ Button: Diagram Options
   │  ├─ Smart Punctuation
   │  │  ├─ Learn More...
   │  │  ├─ Dropdown values
   │  │  │  ├─ Convert on Input
   │  │  │  └─ Convert on Rendering
   │  │  ├─ Smart Quotes
   │  │  │  ├─ Double-quote style dropdown: “abc”
   │  │  │  └─ Single-quote style dropdown: ‘abc’
   │  │  ├─ Smart Dashes
   │  │  └─ Remap Unicode Punctuation on Parse
   │  ├─ Code Fences
   │  │  ├─ Display line numbers for code fences
   │  │  ├─ Auto wrap long lines
   │  │  ├─ Use Shift+Tab to auto indent selected code
   │  │  ├─ Indent Size for Code
   │  │  │  └─ Dropdown values
   │  │  │     ├─ 2
   │  │  │     ├─ 3
   │  │  │     ├─ 4
   │  │  │     └─ 5
   │  │  ├─ Default Code Language
   │  │  │  └─ Dropdown values visible in screenshot
   │  │  │     ├─ (None)
   │  │  │     ├─ Last Used
   │  │  │     ├─ ABAP
   │  │  │     ├─ apl
   │  │  │     ├─ asciiarmor
   │  │  │     └─ ASN.1
   │  │  └─ Apply Default Code Language When
   │  │     └─ Dropdown values
   │  │        ├─ When add code fences via Markdown
   │  │        ├─ When add code fences via Menubar
   │  │        └─ Both
   │  ├─ Math
   │  │  ├─ Learn More...
   │  │  ├─ Inline Math (e.g: $LaTeX$)
   │  │  ├─ LaTeX Math Delimiter \( \) \[ \]
   │  │  ├─ Code Block Math (```math code block)
   │  │  ├─ Enable physics package
   │  │  ├─ Auto Numbering Math Equations
   │  │  │  └─ Dropdown values
   │  │  │     ├─ No Automatic Equation Numbering
   │  │  │     ├─ Use AMS Numbering Rules
   │  │  │     └─ Auto Numbering for All Math Equations
   │  │  ├─ When copy / export as HTML (without style)
   │  │  │  └─ Dropdown values
   │  │  │     ├─ Use Rendered SVG
   │  │  │     └─ Use LaTeX Source
   │  │  └─ Button: Enable inline math or code block math
   │  └─ Whitespace / Line Break
   │     ├─ Learn More...
   │     ├─ Indent first line of paragraphs
   │     ├─ Visible <br/>
   │     ├─ When Writing
   │     │  └─ Dropdown values
   │     │     ├─ Preserve sequential whitespace and single line break
   │     │     └─ Ignore sequential whitespace and single line break
   │     └─ Export / Print
   │        └─ Dropdown values
   │           ├─ Preserve sequential whitespace and single line break
   │           └─ Ignore sequential whitespace and single line break
   │
   ├─ Export
   │  ├─ Export format sections
   │  │  ├─ General
   │  │  ├─ PDF
   │  │  ├─ HTML
   │  │  ├─ HTML (without Styles)
   │  │  ├─ Image
   │  │  ├─ Word (.docx)
   │  │  ├─ OpenOffice
   │  │  ├─ RTF
   │  │  ├─ Epub
   │  │  ├─ LaTeX
   │  │  ├─ Media Wiki
   │  │  ├─ reStructuredText
   │  │  ├─ Textile
   │  │  └─ OPML
   │  └─ General
   │     ├─ Default Folder for Exported File
   │     │  └─ Dropdown values
   │     │     ├─ Auto
   │     │     ├─ Same folder with current file
   │     │     └─ Custom location
   │     ├─ Pandoc Path
   │     │  ├─ Learn More...
   │     │  └─ Folder picker button
   │     └─ After Export
   │        └─ Open exported file location
   │
   └─ Appearance
      ├─ Window Style
      │  ├─ Note: applied after restart
      │  ├─ Classic
      │  └─ Unibody
      ├─ Font Size
      │  ├─ Auto (Recommended)
      │  └─ Customized
      ├─ Zoom
      │  ├─ Dropdown value shown: 100%
      │  ├─ Button: Reset to Default
      │  └─ Zoom when using mouse wheel and holding Ctrl key
      ├─ Status Bar
      │  └─ Show status bar
      ├─ Word Count
      │  ├─ Reading Speed: 382 words / min
      │  └─ Button: Reset to Default
      └─ Themes
         ├─ Learn More...
         ├─ Light Theme: Github
         ├─ Dark Theme: Github Dark Default
         ├─ Use separate theme in dark mode
         ├─ Button: Open Theme Folder
         └─ Button: Get Themes
```
