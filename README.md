# One Click Converter

A Windows desktop converter that processes files locally. The app itself does not upload files or require an internet connection.

## Supported conversions

- **Images:** PNG, JPEG, BMP, GIF, and TIFF. Animated GIFs and multi-page TIFFs export their first frame. Transparent pixels are flattened onto white for JPEG.
- **Text and markup:** TXT, Markdown, and HTML.
- **Tables:** CSV and TSV, with quoted delimiters preserved during conversion.
- **Subtitles:** SRT and WebVTT timestamp conversion.
- **Archives:** create ZIP or GZIP files from selected files; extract ZIP and GZIP files.
- **PDF:** DOCX, TXT, Markdown, and EPUB targets using the bundled PDF reader. JPG/PNG page export appears when Poppler's `pdftoppm` is installed. PDF-to-DOCX keeps extracted text and does not preserve the original page layout. Scanned PDFs need OCR, which is not included yet.
- **Audio and video:** MP3, WAV, FLAC, OGG, M4A, AAC, MP4, MKV, WebM, MOV, and AVI when FFmpeg is installed.
- **Office documents:** Word, spreadsheet, and presentation conversions through LibreOffice when installed.

The output format list updates for the selected file types. For a mixed selection, it only offers formats shared by every selected file. Unsupported files are skipped.

One Click Converter stays available from the system tray. On first launch it registers to start with Windows; use the tray menu to disable that option, reopen the main window, or exit. Closing the main window keeps the converter running in the tray. Converted files save to the Desktop by default; the wheel shows the active output location.

Select a supported file in File Explorer and hold **Shift** to open the radial format wheel beside the pointer. Drag the selected file onto a format and release to convert it. The wheel closes after the drop. It only shows formats supported by the selected file(s); mixed selections show formats shared by every file. Conversion starts immediately and saves to the configured output folder.

### Optional conversion engines

FFmpeg enables audio and video conversion. The app looks for `ffmpeg.exe` in its `tools` folder or on `PATH`.

LibreOffice enables Office document conversion. The app looks for `soffice` on `PATH` or in the standard Program Files location. `pdftoppm.exe` enables PDF page images and can be placed in the app's `tools` folder or on `PATH`.

## Build on Windows

Install the .NET 8 SDK, then run from this folder:

```powershell
dotnet build .\OneClickConverter.csproj -c Release
dotnet run --project .\OneClickConverter.csproj
```

To publish a self-contained Windows x64 app:

```powershell
dotnet publish .\OneClickConverter.csproj -c Release -r win-x64 --self-contained true
```

The executable is written under `bin\Release\net8.0-windows\win-x64\publish\`.
