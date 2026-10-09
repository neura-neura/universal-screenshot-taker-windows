# Universal Screenshot Taker for Windows

A native WinUI 3 application for capturing pages from web readers, recognizing text, and exporting documents. Windows port of [Universal Screenshot Taker for macOS](https://github.com/neura-neura/universal-screenshot-taker-macos).

## Download

Get the installer or portable ZIP from [GitHub Releases](https://github.com/neura-neura/universal-screenshot-taker-windows/releases/latest).

- **Installer:** run `UniversalScreenshotTaker-Setup-x64.exe`. Installation is per user, without administrator privileges.
- **Portable:** extract `UniversalScreenshotTaker-Portable-x64.zip` into a writable folder and run `UniversalCapture.exe`. Session data still lives in your Windows user profile.
- **Requirements:** Windows 10 version 2004 or later, or Windows 11, on x64. .NET, Windows App SDK runtimes, and local OCR are included. Python and development SDKs are not required to run the app.

The installer is not Authenticode-signed. Windows may show an unknown-publisher warning. SHA-256 checksums are provided with each release.

## First capture

1. Open **Help**, select your browser, and choose **Install extension**. The list identifies the Windows default browser and lets you browse for a portable browser executable. Follow the local installation guide in that browser.
2. Open your reader using your existing browser session. Click the extension icon on the reader tab to connect automatically. No connection address or key is needed.
3. In **Capture**, choose the browser DOM field method. Use **Select field**, then click the editable page-number component highlighted in the reader.
4. Use **Select area** and drag over the visible page content, or choose its two corners. Resize the area using its edges and corners.
5. Test **Go to page**, set the page range and wait time, and capture a short range to check that the reader loads each page.
6. Review the gallery, recognize text if needed, and export. **Open folder** appears after a successful export.

Green dashed borders mark the field; blue dashed borders and translucent fill mark the area. A small countdown shows the wait before each capture. The extension updates the DOM field and sends input, change, and Enter events. It captures the visible browser tab and crops the selected area without moving the mouse or capturing the desktop. Markers are hidden while taking the image.

Keep the reader tab active. Capture stops if the tab changes or the area no longer fits. Browser profiles are never cloned. Some sites reject synthetic navigation events, have non-editable controls, or restrict capture; compatibility with every website is not guaranteed. Canvas and video readers may need a longer wait.

## Browser extension

The guide directs Chromium browsers such as Chrome and Thorium to `chrome://extensions`, and Edge to `edge://extensions`. Enable Developer mode, choose **Load unpacked**, and select the extension folder shown in the guide. Browser rules require these manual steps; the app does not silently install the extension or change browser preferences.

The guide follows the app language and opens in the chosen browser. Choosing the Windows default follows each computer's HTTPS association. An explicit browser choice is remembered. After connecting, close the extension popup: capture continues in the background. The field and area are restored where possible after reconnecting; reselect them if the reader layout changes.

The extension inspects page elements and frames and communicates with the app through an authenticated loopback server at `127.0.0.1`. Editable fields inside permitted frames and open shadow roots can be selected. A CSS selector must identify exactly one editable element. Area selection is made in the top-level page and can cover an iframe.

An alternative Firefox manifest is included in `BrowserExtension/firefox` for temporary loading through `about:debugging`. It has not been verified in a real Firefox browser.

## Capture modes

- **Automatic:** captures a page range.
- **Semi-automatic:** captures and advances one page at a time.
- **Manual:** captures the current view.
- **Visual stability:** waits for visible content and images to settle. Use a longer fixed wait if the reader changes content in ways the extension cannot detect.
- **Windows accessibility compatibility mode:** identifies fields through UI Automation without an extension. Keep the reader window in front and preserve its position and size. DOM mode provides the browser-based workflow.

Shortcuts: **Ctrl+Shift+C** captures, **Ctrl+Shift+F12** stops, and **Esc** cancels selection. Area handles support arrow keys, with **Shift** for ten-pixel steps.

**Clear data** is always visible in the bottom bar and is disabled during ongoing operations. It saves a recovery copy, clears the title, website address, all three page numbers, and gallery, and opens Capture. It keeps the selected field, area, and other preferences. The gallery also has **Delete all**, with confirmation and a recovery copy.

## OCR and export

Bundled Tesseract OCR works offline with fast models for Spanish, English, and Simplified Chinese. Windows OCR is available when supported by the system and process identity; use the bundled engine if Windows OCR is unavailable. Optional Ollama requires a running server and vision model. External Tesseract and Surya require their own installations.

Export image PDF, searchable image-and-text PDF, text PDF, images ZIP, TXT, Markdown, HTML, EPUB, or DOCX. Text formats require OCR. When existing OCR lacks coordinates, searchable PDF export runs bundled local OCR to create its positioned text layer. Gallery text is preserved; the searchable layer uses local recognition of the image. Pages with no detected text retain their images. **Preserve images** includes original pages in supported text document formats.

Document titles accept Unicode and punctuation. The system save dialog suggests a valid Windows filename with invalid characters removed, while keeping the document title unchanged.

## Projects and preferences

Sessions are saved in `%LOCALAPPDATA%\UniversalScreenshotTaker\Current`. Preferences, export format, and language persist across restarts. The interface supports Spanish, English, and Simplified Chinese. **System language** is the default for new installations and falls back to English for other languages. Some help or diagnostic messages may still be in Spanish.

`.ustproject` files are portable ZIP archives containing metadata and PNG images. Opening or replacing a project saves a recovery copy. Exported documents replace their destination only after export succeeds. Uninstalling keeps projects. Windows projects use a separate schema and do not directly import macOS packages.

## Build from source

Build on Windows x64 with the .NET SDK specified in `global.json`, Windows SDK, and the Visual C++ x64 redistributable files from Visual Studio C++ build tools.

```powershell
./scripts/test.ps1
./scripts/build.ps1
```

The build creates the installer, portable ZIP, and checksums in `dist`. It downloads Inno Setup from its official source when missing and verifies its signature. OCR models use the revision and SHA-256 hashes in `scripts/dependencies.json`.

Optional browser regression tests require Node.js:

```powershell
cd tests/browser
npm ci
npm test
```

## License and credits

MIT license. See [LICENSE](LICENSE) and [third-party notices](THIRD-PARTY-NOTICES.md). The UI follows [Microsoft Windows design guidance](https://learn.microsoft.com/windows/apps/design/basics/).
