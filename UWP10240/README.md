# Mark::Blocks UWP (Windows 10 build 10240+)

This solution is the Microsoft Store-compatible UWP port of the existing
Windows 8.1 desktop project. It reuses the shared application code and keeps
the original Windows and Windows Phone projects unchanged.

- Store identity: `BenLi06.MarkBlocks`
- Package version: `2.3.0.0`
- Minimum Windows version: `10.0.10240.0`
- Build SDK: `10.0.26100.0`
- Main Store bundle architectures: `x86` and `x64`
- Separate Store upload architecture: `ARM` (ARM32)

The ARM32 package is built from `MetroMarkdownEditor.UWP10240.ARM.sln`. This
project links the same UWP pages, shared code, and assets instead of keeping a
second source copy. It produces a separate ARM Store upload while preserving
the requested Windows 10 build 10240 minimum. The installed Visual Studio 2015
ARM runtime is used for this Store-upload IL package; the current SDK supplies
the manifest and packaging tools.

UWP ARM64 requires minimum Windows build 16299 and is intentionally not part
of these 10240 packages. The existing Windows Phone ARM project remains
separate and unchanged.

Open `MetroMarkdownEditor.UWP10240.sln` in Visual Studio, select `Release`,
and use **Publish > Create App Packages** for a Store upload. The generated
`.appxupload` file is written below the project's `AppPackages\2.3.0` folder.

The Windows 8.1 Settings charm is not available to UWP applications, so the
same settings flyouts are exposed from the **Settings** button on the start
page.
