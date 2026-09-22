# Mark::Blocks UWP (Windows 10 build 10240+)

This solution is the Microsoft Store-compatible UWP port of the existing
Windows 8.1 desktop project. It reuses the shared application code and keeps
the original Windows and Windows Phone projects unchanged.

- Store identity: `BenLi06.MarkBlocks`
- Package version: `2.3.0.0`
- Minimum Windows version: `10.0.10240.0`
- Build SDK: `10.0.26100.0`
- Store bundle architectures: `x86` and `x64`

ARM32 cannot be compiled with the installed Windows 11 SDK, while UWP ARM64
requires minimum Windows build 16299. Keeping the requested 10240 minimum
therefore means the Store bundle must remain x86/x64. The existing Windows
Phone ARM project remains separate and unchanged.

Open `MetroMarkdownEditor.UWP10240.sln` in Visual Studio, select `Release`,
and use **Publish > Create App Packages** for a Store upload. The generated
`.appxupload` file is written below the project's `AppPackages\2.3.0` folder.

The Windows 8.1 Settings charm is not available to UWP applications, so the
same settings flyouts are exposed from the **Settings** button on the start
page.
