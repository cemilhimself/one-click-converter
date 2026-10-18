using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace OneClickConverter;

public sealed record FormatChoice(string Name, string Extension);

public static class ConversionService
{
    private static readonly string[] ImageFormats = ["png", "jpg", "bmp", "gif", "tiff"];
    private static readonly string[] TextFormats = ["txt", "md", "html"];
    private static readonly string[] TableFormats = ["csv", "tsv"];
    private static readonly string[] SubtitleFormats = ["srt", "vtt"];
    private static readonly string[] AudioFormats = ["mp3", "wav", "flac", "ogg", "m4a", "aac"];
    private static readonly string[] VideoFormats = ["mp4", "mkv", "webm", "mov", "avi", "mp3"];
    private static readonly string[] ArchiveFormats = ["zip", "gz"];
    private static readonly HashSet<string> ImageInputs = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];
    private static readonly HashSet<string> TextInputs = [".txt", ".md", ".markdown", ".html", ".htm"];
    private static readonly HashSet<string> TableInputs = [".csv", ".tsv"];
    private static readonly HashSet<string> SubtitleInputs = [".srt", ".vtt"];
    private static readonly HashSet<string> AudioInputs = [".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".opus", ".wma", ".aiff", ".aif"];
    private static readonly HashSet<string> VideoInputs = [".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".mpeg", ".mpg"];
    private static readonly HashSet<string> WordInputs = [".doc", ".docx", ".odt", ".rtf"];
    private static readonly HashSet<string> SheetInputs = [".xls", ".xlsx", ".ods"];
    private static readonly HashSet<string> SlideInputs = [".ppt", ".pptx", ".odp"];

    public static string? FfmpegPath => FindExecutable("ffmpeg.exe");
    public static string? LibreOfficePath => FindExecutable("soffice.exe") ?? FindExecutable("soffice");

    public static bool IsSupportedInput(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return ImageInputs.Contains(extension) || TextInputs.Contains(extension) || TableInputs.Contains(extension)
            || SubtitleInputs.Contains(extension) || AudioInputs.Contains(extension) || VideoInputs.Contains(extension)
            || WordInputs.Contains(extension) || SheetInputs.Contains(extension) || SlideInputs.Contains(extension)
            || extension is ".pdf" or ".zip" or ".gz";
    }

    public static string? GetEngineHint(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if ((AudioInputs.Contains(extension) || VideoInputs.Contains(extension)) && FfmpegPath is null)
            return "Install FFmpeg to enable audio and video conversions. ZIP/GZIP compression is available now.";
        if ((WordInputs.Contains(extension) || SheetInputs.Contains(extension) || SlideInputs.Contains(extension)) && LibreOfficePath is null)
            return "Install LibreOffice to enable Office document conversions. ZIP/GZIP compression is available now.";
        if (extension == ".pdf" && FindExecutable("pdftoppm.exe") is null)
            return "PDF text conversions are ready. Install Poppler's pdftoppm tool to also export PDF pages as images.";
        return null;
    }

    public static IReadOnlyList<FormatChoice> GetTargets(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (ImageInputs.Contains(extension)) return WithArchives(ImageFormats);
        if (TextInputs.Contains(extension)) return WithArchives(TextFormats);
        if (TableInputs.Contains(extension)) return WithArchives(TableFormats);
        if (SubtitleInputs.Contains(extension)) return WithArchives(SubtitleFormats);
        if (AudioInputs.Contains(extension) || VideoInputs.Contains(extension))
            return FfmpegPath is not null ? WithArchives(VideoInputs.Contains(extension) ? VideoFormats : AudioFormats) : Choices(ArchiveFormats);
        if (extension is ".zip" or ".gz") return [new FormatChoice("Extract", "extract")];
        if (extension == ".pdf")
        {
            var pdfFormats = new List<string> { "docx", "txt", "md", "epub" };
            if (FindExecutable("pdftoppm.exe") is not null) pdfFormats.AddRange(["jpg", "png"]);
            return Choices(pdfFormats);
        }
        if (LibreOfficePath is not null)
        {
            if (WordInputs.Contains(extension)) return WithArchives(["pdf", "docx", "odt", "rtf", "txt", "html"]);
            if (SheetInputs.Contains(extension)) return WithArchives(["pdf", "xlsx", "ods", "csv"]);
            if (SlideInputs.Contains(extension)) return WithArchives(["pdf", "pptx", "odp"]);
        }
        if (extension != ".pdf") return Choices(ArchiveFormats);
        return [];
    }

    public static void Convert(string sourcePath, string outputPath, string format)
    {
        var sourceExtension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (format is "zip" or "gz") Compress(sourcePath, outputPath, format);
        else if (ImageInputs.Contains(sourceExtension)) ConvertImage(sourcePath, outputPath, format);
        else if (TextInputs.Contains(sourceExtension)) ConvertText(sourcePath, outputPath, format);
        else if (TableInputs.Contains(sourceExtension)) ConvertTable(sourcePath, outputPath, format);
        else if (SubtitleInputs.Contains(sourceExtension)) ConvertSubtitle(sourcePath, outputPath, format);
        else if (format == "extract") ExtractArchive(sourcePath, outputPath);
        else if (AudioInputs.Contains(sourceExtension) || VideoInputs.Contains(sourceExtension)) ConvertMedia(sourcePath, outputPath, format);
        else if ((WordInputs.Contains(sourceExtension) || SheetInputs.Contains(sourceExtension) || SlideInputs.Contains(sourceExtension)) && LibreOfficePath is not null)
            ConvertWithLibreOffice(sourcePath, outputPath, format);
        else if (sourceExtension == ".pdf") ConvertPdf(sourcePath, outputPath, format);
        else throw new NotSupportedException("No local conversion engine is available for this file type.");
    }

    private static IReadOnlyList<FormatChoice> Choices(IEnumerable<string> extensions) => extensions
        .Select(ext => new FormatChoice(ext.ToUpperInvariant(), ext))
        .ToArray();

    private static IReadOnlyList<FormatChoice> WithArchives(IEnumerable<string> formats) => Choices(formats.Concat(ArchiveFormats).Distinct());

    private static void ConvertImage(string sourcePath, string outputPath, string format)
    {
        BitmapFrame frame;
        using (var input = File.OpenRead(sourcePath))
        {
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0) throw new InvalidDataException("No image frame was found.");
            frame = decoder.Frames[0];
        }

        BitmapSource source = frame;
        if (format == "jpg" && (source.Format == PixelFormats.Pbgra32 || source.Format == PixelFormats.Bgra32))
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
                context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            }
            var flattened = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, source.DpiX, source.DpiY, PixelFormats.Pbgra32);
            flattened.Render(visual);
            source = flattened;
        }

        BitmapEncoder encoder = format switch
        {
            "jpg" => new JpegBitmapEncoder { QualityLevel = 92 },
            "bmp" => new BmpBitmapEncoder(),
            "gif" => new GifBitmapEncoder(),
            "tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private static void ConvertText(string sourcePath, string outputPath, string format)
    {
        var content = File.ReadAllText(sourcePath, Encoding.UTF8);
        var sourceFormat = Path.GetExtension(sourcePath).ToLowerInvariant();
        var plain = sourceFormat is ".html" or ".htm" ? HtmlToText(content) : MarkdownToText(content);
        var converted = format switch
        {
            "html" => sourceFormat is ".md" or ".markdown" ? MarkdownToHtml(content) : $"<!doctype html><meta charset=\"utf-8\"><pre>{WebUtility.HtmlEncode(plain)}</pre>",
            "md" => sourceFormat is ".html" or ".htm" ? HtmlToMarkdown(content) : content,
            _ => plain
        };
        File.WriteAllText(outputPath, converted, new UTF8Encoding(false));
    }

    private static string HtmlToText(string html)
    {
        var withoutHidden = Regex.Replace(html, "<(script|style)\\b[^>]*>[\\s\\S]*?</\\1>", " ", RegexOptions.IgnoreCase);
        var withBreaks = Regex.Replace(withoutHidden, "<(br|/p|/div|/h[1-6]|/li|/tr)\\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        return Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(withBreaks, "<[^>]+>", " ")), "[ \\t]+", " ").Trim();
    }

    private static string MarkdownToText(string markdown) => Regex.Replace(markdown, @"(?m)^\s{0,3}#{1,6}\s*|[*_`~]", "").Trim();

    private static string MarkdownToHtml(string markdown)
    {
        var blocks = Regex.Split(markdown.Trim(), @"\r?\n\s*\r?\n").Where(x => x.Length > 0);
        var output = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><main>\n");
        foreach (var block in blocks)
        {
            var heading = Regex.Match(block, @"^\s{0,3}(#{1,6})\s+(.+)$", RegexOptions.Singleline);
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                output.Append('<').Append('h').Append(level).Append('>').Append(InlineMarkdown(heading.Groups[2].Value.Trim())).Append("</h").Append(level).AppendLine(">");
            }
            else output.Append("<p>").Append(InlineMarkdown(block.Replace("\r", "").Replace("\n", " "))).AppendLine("</p>");
        }
        return output.AppendLine("</main>").ToString();
    }

    private static string InlineMarkdown(string text)
    {
        var value = WebUtility.HtmlEncode(text);
        value = Regex.Replace(value, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        value = Regex.Replace(value, @"\*(.+?)\*", "<em>$1</em>");
        value = Regex.Replace(value, "`(.+?)`", "<code>$1</code>");
        return value;
    }

    private static string HtmlToMarkdown(string html)
    {
        var value = Regex.Replace(html, @"<h([1-6])[^>]*>(.*?)</h\1>", m => $"{new string('#', int.Parse(m.Groups[1].Value))} {HtmlToText(m.Groups[2].Value)}\n\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"<(strong|b)[^>]*>(.*?)</\1>", "**$2**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"<(em|i)[^>]*>(.*?)</\1>", "*$2*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        value = Regex.Replace(value, @"</p\s*>", "\n\n", RegexOptions.IgnoreCase);
        return HtmlToText(value).Replace("\n ", "\n").Trim();
    }

    private static void ConvertTable(string sourcePath, string outputPath, string format)
    {
        var sourceDelimiter = Path.GetExtension(sourcePath).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';
        var targetDelimiter = format == "tsv" ? '\t' : ',';
        var text = File.ReadAllText(sourcePath, Encoding.UTF8);
        var rows = ParseDelimited(text, sourceDelimiter);
        var converted = string.Join(Environment.NewLine, rows.Select(row => string.Join(targetDelimiter, row.Select(field => QuoteField(field, targetDelimiter)))));
        File.WriteAllText(outputPath, converted, new UTF8Encoding(false));
    }

    private static List<List<string>> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"' && quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
            else if (c == '"') quoted = !quoted;
            else if (c == delimiter && !quoted) { row.Add(field.ToString()); field.Clear(); }
            else if ((c == '\n' || c == '\r') && !quoted)
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear(); rows.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static string QuoteField(string field, char delimiter) => field.Contains(delimiter) || field.Contains('"') || field.Contains('\n') || field.Contains('\r')
        ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    private static void ConvertSubtitle(string sourcePath, string outputPath, string format)
    {
        var content = File.ReadAllText(sourcePath, Encoding.UTF8).TrimStart('\uFEFF');
        var sourceIsVtt = Path.GetExtension(sourcePath).Equals(".vtt", StringComparison.OrdinalIgnoreCase);
        if (format == "vtt")
        {
            content = Regex.Replace(content, @"(?<!\d)(\d{2}:\d{2}:\d{2}),(\d{3})(?!\d)", "$1.$2");
            if (!sourceIsVtt) content = "WEBVTT\n\n" + content;
        }
        else
        {
            content = Regex.Replace(content, @"(?<!\d)(\d{2}:\d{2}:\d{2})\.(\d{3})(?!\d)", "$1,$2");
            if (sourceIsVtt) content = Regex.Replace(content, @"^WEBVTT[^\r\n]*(?:\r?\n|$)", "", RegexOptions.IgnoreCase);
        }
        File.WriteAllText(outputPath, content, new UTF8Encoding(false));
    }

    private static void Compress(string sourcePath, string outputPath, string format)
    {
        if (format == "zip")
        {
            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
            archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Optimal);
        }
        else
        {
            using var input = File.OpenRead(sourcePath);
            using var output = File.Create(outputPath);
            using var gzip = new GZipStream(output, CompressionLevel.Optimal);
            input.CopyTo(gzip);
        }
    }

    private static void ExtractArchive(string sourcePath, string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        if (Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(sourcePath, outputPath);
            return;
        }
        var extractedName = Path.GetFileNameWithoutExtension(sourcePath);
        using var input = File.OpenRead(sourcePath);
        using var gzip = new GZipStream(input, CompressionMode.Decompress, leaveOpen: false);
        using var output = File.Create(Path.Combine(outputPath, extractedName));
        gzip.CopyTo(output);
    }

    private static void ConvertMedia(string sourcePath, string outputPath, string format)
    {
        var executable = FfmpegPath ?? throw new NotSupportedException("Install FFmpeg or place ffmpeg.exe in the app's tools folder to convert audio and video.");
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        if (format == "mp3" && VideoInputs.Contains(Path.GetExtension(sourcePath).ToLowerInvariant())) startInfo.ArgumentList.Add("-vn");
        startInfo.ArgumentList.Add(outputPath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpeg could not be started.");
        var errors = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidDataException(errors.GetAwaiter().GetResult());
    }

    private static void ConvertWithLibreOffice(string sourcePath, string outputPath, string format)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"OneClickConverter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var startInfo = new ProcessStartInfo(LibreOfficePath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            startInfo.ArgumentList.Add("--headless");
            startInfo.ArgumentList.Add("--convert-to");
            startInfo.ArgumentList.Add(format);
            startInfo.ArgumentList.Add("--outdir");
            startInfo.ArgumentList.Add(temporaryDirectory);
            startInfo.ArgumentList.Add(sourcePath);
            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("LibreOffice could not be started.");
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            var createdFile = Path.Combine(temporaryDirectory, $"{Path.GetFileNameWithoutExtension(sourcePath)}.{format}");
            if (process.ExitCode != 0 || !File.Exists(createdFile)) throw new InvalidDataException(errors.GetAwaiter().GetResult());
            File.Move(createdFile, outputPath);
        }
        finally { Directory.Delete(temporaryDirectory, true); }
    }

    private static void ConvertPdf(string sourcePath, string outputPath, string format)
    {
        if (format is "jpg" or "png")
        {
            var renderer = FindExecutable("pdftoppm.exe") ?? throw new NotSupportedException("Install Poppler's pdftoppm tool to render PDF pages as images.");
            var startInfo = new ProcessStartInfo(renderer) { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add(format == "jpg" ? "-jpeg" : "-png");
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add("150");
            startInfo.ArgumentList.Add(sourcePath);
            startInfo.ArgumentList.Add(Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath)));
            using var render = Process.Start(startInfo) ?? throw new InvalidOperationException("pdftoppm could not be started.");
            render.WaitForExit();
            if (render.ExitCode != 0) throw new InvalidDataException("PDF page rendering failed.");
            return;
        }

        string text;
        using (var document = PdfDocument.Open(sourcePath))
        {
            text = string.Join(Environment.NewLine + Environment.NewLine,
                document.GetPages().Select(page => ContentOrderTextExtractor.GetText(page)));
        }
        if (string.IsNullOrWhiteSpace(text))
            throw new NotSupportedException("This PDF has no selectable text. Scanned PDFs need OCR, which is not included yet.");
        switch (format)
        {
            case "docx": WriteDocx(text, outputPath); break;
            case "epub": WriteEpub(text, outputPath, Path.GetFileNameWithoutExtension(sourcePath)); break;
            case "md": File.WriteAllText(outputPath, text, new UTF8Encoding(false)); break;
            default: File.WriteAllText(outputPath, text, new UTF8Encoding(false)); break;
        }
    }

    private static void WriteDocx(string text, string outputPath)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace contentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        WriteXmlEntry(archive, "[Content_Types].xml", new XDocument(
            new XElement(contentTypes + "Types",
                new XElement(contentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                new XElement(contentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
                new XElement(contentTypes + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")))));
        WriteXmlEntry(archive, "_rels/.rels", new XDocument(
            new XElement(relationships + "Relationships",
                new XElement(relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "word/document.xml")))));
        var body = new XElement(w + "body");
        using var reader = new StringReader(text.Replace("\f", "\n"));
        string? line;
        while ((line = reader.ReadLine()) is not null)
            body.Add(new XElement(w + "p", new XElement(w + "r", new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), line))));
        body.Add(new XElement(w + "sectPr"));
        WriteXmlEntry(archive, "word/document.xml", new XDocument(new XElement(w + "document", body)));
    }

    private static void WriteEpub(string text, string outputPath, string title)
    {
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        var mimetype = archive.CreateEntry("mimetype", CompressionLevel.NoCompression);
        using (var writer = new StreamWriter(mimetype.Open(), new UTF8Encoding(false))) writer.Write("application/epub+zip");
        XNamespace container = "urn:oasis:names:tc:opendocument:xmlns:container";
        WriteXmlEntry(archive, "META-INF/container.xml", new XDocument(new XElement(container + "container", new XAttribute("version", "1.0"),
            new XElement(container + "rootfiles", new XElement(container + "rootfile", new XAttribute("full-path", "OEBPS/content.opf"), new XAttribute("media-type", "application/oebps-package+xml"))))));
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace opf = "http://www.idpf.org/2007/opf";
        WriteXmlEntry(archive, "OEBPS/content.opf", new XDocument(new XElement(opf + "package", new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "book-id"),
            new XElement(opf + "metadata", new XAttribute(XNamespace.Xmlns + "dc", dc), new XElement(dc + "identifier", new XAttribute("id", "book-id"), Guid.NewGuid().ToString()), new XElement(dc + "title", title), new XElement(dc + "language", "en")),
            new XElement(opf + "manifest", new XElement(opf + "item", new XAttribute("id", "content"), new XAttribute("href", "content.xhtml"), new XAttribute("media-type", "application/xhtml+xml"))),
            new XElement(opf + "spine", new XElement(opf + "itemref", new XAttribute("idref", "content"))))));
        XNamespace html = "http://www.w3.org/1999/xhtml";
        var paragraphs = text.Replace("\f", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries).Select(block => new XElement(html + "p", block.Trim()));
        WriteXmlEntry(archive, "OEBPS/content.xhtml", new XDocument(new XElement(html + "html", new XElement(html + "head", new XElement(html + "title", title)), new XElement(html + "body", paragraphs))));
    }

    private static void WriteXmlEntry(ZipArchive archive, string name, XDocument document)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static string? FindExecutable(string executableName)
    {
        var local = Path.Combine(AppContext.BaseDirectory, "tools", executableName);
        if (File.Exists(local)) return local;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory.Trim('"'), executableName);
            if (File.Exists(candidate)) return candidate;
        }
        if (executableName.Equals("soffice.exe", StringComparison.OrdinalIgnoreCase))
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var candidate = Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
