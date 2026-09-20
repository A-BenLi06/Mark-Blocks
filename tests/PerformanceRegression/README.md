# Performance regression checks

Run from the repository root:

```powershell
dotnet run --project tests/PerformanceRegression/PerformanceRegression.csproj -c Release
node tests/PerformanceRegression/preview-browser.cjs
```

The console harness requires the .NET 10 SDK and the repository's restored `packages/Markdig.0.15.5` package. It links production source files and replaces only WinRT storage/XAML boundaries. The application itself remains a Windows 8.1 / Phone 8.1 project.

The browser check requires an existing Playwright installation and Microsoft Edge. If Playwright is not installed in this repository, set `PLAYWRIGHT_MODULE` to the absolute path of an existing Playwright module. Do not install or upgrade application dependencies to run this harness.

Generated fixtures and screenshots are in the ignored `artifacts/` directory. The test uses the production renderer's HTML/JavaScript, production patches, and local packaged fonts. The intentionally untitled preview document is identified by its fixture URL and `#content` DOM.

These checks validate document semantics, change reconstruction, save versioning, cancellation, lazy script inclusion, DOM identity preservation and nearby-block lookup. They do **not** measure native RichEdit latency, IME behavior, 8.1 WebView compatibility or target-device memory. Those require device testing in addition to the Windows/Phone MSBuild builds.
