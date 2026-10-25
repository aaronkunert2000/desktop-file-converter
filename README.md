# desktop-file-converter

Drag files in any place on your computer and convert them easily into suitable formats.

**Idea:** Pick up a file in Explorer or on the desktop and hold it still (or hold `Alt` while dragging).
A radial menu appears around the cursor with the formats the file can be converted to. Drop the file on a
segment: the converted file is saved next to the original.

## Status: v0.1 spike (Windows only)

Unbuilt/untested scaffold to validate the core interaction:

- Global low-level mouse hook detects "file dragged and held" (dwell 500 ms) or `Alt` while dragging
- Transparent overlay window under the cursor acts as OLE drop target and draws the ring
- Images: PNG, JPG, WebP, GIF, BMP, TIFF (input also HEIC, ICO) via Magick.NET
- Documents: MD, TXT, HTML, DOCX to each other and to PDF (Markdig, OpenXML SDK, ReverseMarkdown, WebView2 `PrintToPdf`).
  DOCX to PDF goes through HTML, so it is a simplified rendering (text, headings, lists, tables)

- PDF as source: to PNG/JPG (every page, Windows.Data.Pdf) and to TXT/MD/HTML/DOCX (text via PdfPig, no layout, no OCR)

Planned: second ring for options, OCR, settings, installer.

## Build

Requires the .NET 8 SDK on Windows.

```powershell
dotnet run --project src/FileConverter.App
```

Release build (standalone exe, no .NET needed): push a tag `vX.Y.Z`, the GitHub Action attaches `FileConverter.exe` to a release. Autostart: tray menu -> "Mit Windows starten".

Logs: `%APPDATA%\FileConverter\fileconverter.log`

## Known risks (to verify in the spike)

- Hook-based detection can false-trigger (e.g. moving an Explorer window); the overlay hides itself if no file drag arrives
- `Alt` during an Explorer drag normally means "create shortcut"; the ring forces the Copy effect
- Admin-elevated windows cannot drop onto a non-elevated app (UIPI)
- Files without a path (mail attachments, browser downloads) are not supported yet

## License

MIT, see [LICENSE](LICENSE).
