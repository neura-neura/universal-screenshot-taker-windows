# Universal Screenshot Taker for Windows 1.1.5

Native WinUI 3 application with browser-based page capture, local OCR, and document export.

## Changes in 1.1.5

- Renamed **Start another** to **Clear data**.
- Made **Clear data** visible from startup without requiring an export. It remains disabled during ongoing operations.
- Clearing saves a recovery copy and removes the title, website address, three page numbers, and gallery while retaining the selected field, area, and preferences.

## Included features

- Browser DOM selection and navigation using your existing browser session.
- Resizable region with dashed borders, translucent highlights, and a countdown.
- Browser detection and a localized extension installation guide.
- Offline Tesseract OCR for Spanish, English, and Simplified Chinese.
- Searchable PDF and other export formats, persistent preferences, and recoverable projects.

## Installation

Run **UniversalScreenshotTaker-Setup-x64.exe** for a per-user installation, or extract **UniversalScreenshotTaker-Portable-x64.zip** and run `UniversalCapture.exe`.

Requires Windows 10 version 2004 or later, or Windows 11, on x64. Runtimes and local OCR are included. Install the browser extension through **Help > Install extension**, then click its icon in the reader tab to connect. The guide includes the manual browser steps; the app does not silently install the extension.

To upgrade, close the app and install into its existing folder. Projects and preferences are preserved. The portable build also stores session data in the Windows user profile.

The installer is not Authenticode-signed. Windows may show an unknown-publisher warning. Compare downloads with **SHA256SUMS.txt** using `Get-FileHash -Algorithm SHA256`.

## Validation and limitations

The 1.1.5 build passed 78 Core checks. The always-visible Clear data button was verified in the installed application. Browser regression tests and live Chromium reader navigation were verified during the preceding browser fixes.

Keep the reader tab active and verify that its editable page-number field accepts navigation events. Some websites restrict capture or reject synthetic events. The alternative Firefox extension is included but has not been tested in a real Firefox browser.
