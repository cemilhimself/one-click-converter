# One Click Converter

A Windows desktop converter that processes files locally. The app itself does not upload files or require an internet connection.

## Supported conversions

- **Images:** PNG, JPEG, BMP, GIF, TIFF, WebP, and AVIF, plus SVG input. HEIC/HEIF input is enabled when the bundled image engine or FFmpeg can decode it. Center-square cropping is available; animated and multi-page sources use their first frame. Transparent pixels are flattened onto white for JPEG.
- **Text and markup:** TXT, Markdown, and HTML.
- **Tables:** CSV and TSV, with quoted delimiters preserved during conversion.
- **Subtitles:** SRT and WebVTT timestamp conversion.
- **Archives:** create ZIP, GZIP, TAR, or 7Z files; extract ZIP, GZIP, TAR/TAR.GZ, 7Z, and RAR. Multiple images can be combined into a PDF.
- **PDF:** export selectable text to DOCX, PPTX, XLSX, TXT, Markdown, and EPUB; export every page to JPG/PNG; split a PDF into page files; or merge selected PDFs. Text-based Office exports preserve extracted text, not the original page layout. Scanned PDFs need OCR, which is not included yet.
- **Audio and video:** MP3, WAV, FLAC, OGG, M4A, AAC, Opus, WMA, AIFF, MP4, MKV, WebM, MOV, AVI, and GIF when FFmpeg is installed. Video can be exported to GIF or MP3; a trim option saves the first 10 seconds.
- **Office documents:** Word, spreadsheet, and presentation conversions through LibreOffice when installed. Presentations can also export to PDF/JPG/PNG.

The output format list updates for the selected file types. For a mixed selection, it only offers formats shared by every selected file. Unsupported files are skipped.

One Click Converter stays available from the system tray. On first launch it registers to start with Windows; use the tray menu to disable that option, reopen the main window, or exit. Closing the main window keeps the converter running in the tray. Converted files save to the Desktop by default; the wheel shows the active output location.

Select a supported file in File Explorer and hold **Shift** to open the radial format wheel beside the pointer. Drag the selected file onto a format and release to convert it. The wheel closes after the drop. It only shows formats supported by the selected file(s); mixed selections show formats shared by every file. Conversion starts immediately and saves to the configured output folder.

### Optional conversion engines

The image engine and PDF renderer are bundled with the app. WebP/AVIF and HEIC/HEIF options are shown only when the bundled image engine can encode/decode the format. HEIC encoding may not be available on Windows because of codec and licensing support.

FFmpeg enables audio and video conversion. The app looks for `ffmpeg.exe` in its `tools` folder or on `PATH`.

LibreOffice enables Office document conversion. The app looks for `soffice` on `PATH` or in the standard Program Files location. PDF page rendering, splitting, merging, and image-to-PDF conversion work without Poppler.

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

