using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using ImageMagick;
using PDFtoImage;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using SharpCompress.Archives;
using SharpCompress.Archives.Tar;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Common.Options;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Writers;
using SharpCompress.Readers;

namespace OneClickConverter;

public sealed record FormatChoice(string Name, string Extension);

public static class ConversionService
{
    private static readonly string[] ImageFormats = ["png", "jpg", "bmp", "gif", "tiff", "webp", "avif", "heic"];
    private static readonly string[] TextFormats = ["txt", "md", "html"];
    private static readonly string[] TableFormats = ["csv", "tsv"];
    private static readonly string[] SubtitleFormats = ["srt", "vtt"];
    private static readonly string[] AudioFormats = ["mp3", "wav", "flac", "ogg", "m4a", "aac", "opus", "wma", "aiff"];
    private static readonly string[] VideoFormats = ["mp4", "mkv", "webm", "mov", "avi", "gif", "mp3"];
    private static readonly string[] ArchiveFormats = ["zip", "gz", "tar", "7z"];
    private static readonly HashSet<string> ImageInputs = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".avif", ".heic", ".heif", ".svg"];
    private static readonly HashSet<string> TextInputs = [".txt", ".md", ".markdown", ".html", ".htm"];
    private static readonly HashSet<string> TableInputs = [".csv", ".tsv"];
    private static readonly HashSet<string> SubtitleInputs = [".srt", ".vtt"];
    private static readonly HashSet<string> AudioInputs = [".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".opus", ".wma", ".aiff", ".aif"];
    private static readonly HashSet<string> VideoInputs = [".mp4", ".mkv", ".webm", ".mov", ".avi", ".wmv", ".mpeg", ".mpg", ".gif"];
    private static readonly HashSet<string> ArchiveInputs = [".zip", ".gz", ".tar", ".tgz", ".tar.gz", ".7z", ".rar"];
    private static readonly HashSet<string> WordInputs = [".doc", ".docx", ".odt", ".rtf"];
    private static readonly HashSet<string> SheetInputs = [".xls", ".xlsx", ".ods"];
    private static readonly HashSet<string> SlideInputs = [".ppt", ".pptx", ".odp"];

    public static string? FfmpegPath => FindExecutable("ffmpeg.exe");
    public static string? LibreOfficePath => FindExecutable("soffice.exe") ?? FindExecutable("soffice");

    public static bool IsSupportedInput(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return ImageInputs.Contains(extension) && SupportsImageRead(extension) || TextInputs.Contains(extension) || TableInputs.Contains(extension)
            || SubtitleInputs.Contains(extension) || AudioInputs.Contains(extension) || VideoInputs.Contains(extension)
            || WordInputs.Contains(extension) || SheetInputs.Contains(extension) || SlideInputs.Contains(extension)
            || extension == ".pdf" || ArchiveInputs.Contains(extension) || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetEngineHint(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if ((AudioInputs.Contains(extension) || VideoInputs.Contains(extension)) && FfmpegPath is null)
            return "Install FFmpeg to enable audio and video conversions. ZIP/GZIP compression is available now.";
        if ((WordInputs.Contains(extension) || SheetInputs.Contains(extension) || SlideInputs.Contains(extension)) && LibreOfficePath is null)
            return "Install LibreOffice to enable Office document conversions. ZIP/GZIP compression is available now.";
        if (ImageInputs.Contains(extension) && !SupportsImageRead(extension) && FfmpegPath is null)
            return "Install FFmpeg or a Windows HEIF image extension to read this image format.";
        return null;
    }

    public static IReadOnlyList<FormatChoice> GetTargets(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (ImageInputs.Contains(extension)) return [.. WithArchives(ImageFormats.Where(format => IsSupportedOutput(path, format))), new FormatChoice("Center crop to square", "crop")];
        if (TextInputs.Contains(extension)) return WithArchives(TextFormats);
        if (TableInputs.Contains(extension)) return WithArchives(TableFormats);
        if (SubtitleInputs.Contains(extension)) return WithArchives(SubtitleFormats);
        if (AudioInputs.Contains(extension) || VideoInputs.Contains(extension))
            return FfmpegPath is not null
                ? [.. WithArchives(VideoInputs.Contains(extension) ? VideoFormats : AudioFormats), new FormatChoice("Trim first 10 seconds", "trim")]
                : Choices(ArchiveFormats);
        if (ArchiveInputs.Contains(extension) || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return [new FormatChoice("Extract", "extract")];
        if (extension == ".pdf")
        {
            var pdfFormats = new List<string> { "docx", "pptx", "xlsx", "txt", "md", "epub", "jpg", "png" };
            return [.. Choices(pdfFormats), new FormatChoice("Split into pages", "splitpdf")];
        }
        if (LibreOfficePath is not null)
        {
            if (WordInputs.Contains(extension)) return WithArchives(["pdf", "docx", "odt", "rtf", "txt", "html"]);
            if (SheetInputs.Contains(extension)) return WithArchives(["pdf", "xlsx", "ods", "csv"]);
            if (SlideInputs.Contains(extension)) return WithArchives(["pdf", "pptx", "odp", "jpg", "png"]);
        }
        if (extension != ".pdf" && !ArchiveInputs.Contains(extension)) return Choices(ArchiveFormats);
        return [];
    }

    public static void Convert(string sourcePath, string outputPath, string format)
    {
        var sourceExtension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (format is "zip" or "gz" or "tar" or "7z") Compress(sourcePath, outputPath, format);
        else if (ImageInputs.Contains(sourceExtension)) ConvertImage(sourcePath, outputPath, format);
        else if (TextInputs.Contains(sourceExtension)) ConvertText(sourcePath, outputPath, format);
        else if (TableInputs.Contains(sourceExtension)) ConvertTable(sourcePath, outputPath, format);
        else if (SubtitleInputs.Contains(sourceExtension)) ConvertSubtitle(sourcePath, outputPath, format);
        else if (format == "extract") ExtractArchive(sourcePath, outputPath);
        else if (format == "splitpdf") SplitPdf(sourcePath, outputPath);
        else if (format == "trim") ConvertMedia(sourcePath, outputPath, format);
        else if (format == "crop") ConvertImage(sourcePath, outputPath, format);
        else if (AudioInputs.Contains(sourceExtension) || VideoInputs.Contains(sourceExtension)) ConvertMedia(sourcePath, outputPath, format);
        else if (SlideInputs.Contains(sourceExtension) && format is "jpg" or "png") ConvertSlidesToImages(sourcePath, outputPath, format);
        else if ((WordInputs.Contains(sourceExtension) || SheetInputs.Contains(sourceExtension) || SlideInputs.Contains(sourceExtension)) && LibreOfficePath is not null)
            ConvertWithLibreOffice(sourcePath, outputPath, format);
        else if (sourceExtension == ".pdf") ConvertPdf(sourcePath, outputPath, format);
        else throw new NotSupportedException("No local conversion engine is available for this file type.");
    }

    private static IReadOnlyList<FormatChoice> Choices(IEnumerable<string> extensions) => extensions
        .Select(ext => new FormatChoice(ext.ToUpperInvariant(), ext))
        .ToArray();

    public static IReadOnlyList<FormatChoice> GetTargetsForSelection(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return Array.Empty<FormatChoice>();
        var targets = paths.Select(GetTargets).Aggregate((left, right) => left.Where(item => right.Any(other => other.Extension == item.Extension)).ToArray()).ToList();
        if (paths.Count > 1 && paths.All(path => Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
            targets.Add(new FormatChoice("Merge selected PDFs", "mergepdf"));
        if (paths.Count > 1 && paths.All(path => ImageInputs.Contains(Path.GetExtension(path).ToLowerInvariant())))
            targets.Add(new FormatChoice("Merge into PDF", "imagespdf"));
        return targets;
    }

    public static void ConvertSelection(IReadOnlyList<string> paths, string outputPath, string format)
    {
        if (format == "mergepdf") { MergePdfs(paths, outputPath); return; }
        if (format == "imagespdf") { ImagesToPdf(paths, outputPath); return; }
        if (paths.Count != 1) throw new NotSupportedException("This operation requires a single input file.");
        Convert(paths[0], outputPath, format);
    }

    public static bool IsSupportedOutput(string sourcePath, string format)
    {
        if (format is "png" or "jpg" or "bmp" or "gif" or "tiff") return true;
        var imageFormat = format == "jpg" ? "jpeg" : format;
        return MagickNET.SupportedFormats.Any(item => item.Format.ToString().Equals(imageFormat, StringComparison.OrdinalIgnoreCase) && item.SupportsWriting);
    }

    private static bool SupportsImageRead(string extension)
    {
        var format = extension.TrimStart('.').ToLowerInvariant() switch
        {
            "jpg" or "jpeg" => "jpeg",
            "tif" or "tiff" => "tiff",
            "heif" => "heic",
            _ => extension.TrimStart('.').ToLowerInvariant()
        };
        return MagickNET.SupportedFormats.Any(item => item.Format.ToString().Equals(format, StringComparison.OrdinalIgnoreCase) && item.SupportsReading)
            || ((format is "heic" or "heif") && FfmpegPath is not null);
    }

    public static string GetOutputExtension(string sourcePath, string format) => format switch
    {
        "crop" => IsSupportedOutput(sourcePath, Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant())
            ? Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant() : "png",
        "trim" => Path.GetExtension(sourcePath).TrimStart('.').ToLowerInvariant(),
        "splitpdf" => "pages",
        "extract" => "folder",
        "imagespdf" or "mergepdf" => "pdf",
        _ => format
    };

    private static IReadOnlyList<FormatChoice> WithArchives(IEnumerable<string> formats) => Choices(formats.Concat(ArchiveFormats).Distinct());

    private static void ConvertImage(string sourcePath, string outputPath, string format)
    {
        if (!MagickNET.SupportedFormats.Any(item => item.Format.ToString().Equals(Path.GetExtension(sourcePath).TrimStart('.'), StringComparison.OrdinalIgnoreCase) && item.SupportsReading)
            && FfmpegPath is not null)
        {
            var startInfo = new ProcessStartInfo(FfmpegPath) { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(sourcePath);
            if (format == "crop")
            {
                startInfo.ArgumentList.Add("-vf");
                startInfo.ArgumentList.Add("crop=w='min(iw,ih)':h='min(iw,ih)'");
            }
            startInfo.ArgumentList.Add("-frames:v");
            startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add(outputPath);
            using var decoder = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpeg could not be started.");
            decoder.WaitForExit();
            if (decoder.ExitCode != 0) throw new InvalidDataException("FFmpeg could not read this image.");
            return;
        }
        using var image = new MagickImage(sourcePath);
        if (format == "jpg" || format == "crop" && Path.GetExtension(outputPath).Equals(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }
        if (format == "crop")
        {
            var side = Math.Min(image.Width, image.Height);
            image.Crop(new MagickGeometry((int)((image.Width - side) / 2), (int)((image.Height - side) / 2), side, side));
            image.ResetPage();
            image.Format = MagickFormat.Svg.Equals(image.Format) ? MagickFormat.Png : image.Format;
        }
        else
        {
            image.Format = format switch
            {
                "jpg" => MagickFormat.Jpeg,
                "bmp" => MagickFormat.Bmp,
                "gif" => MagickFormat.Gif,
                "tiff" => MagickFormat.Tiff,
                "webp" => MagickFormat.WebP,
                "avif" => MagickFormat.Avif,
                _ => MagickFormat.Png
            };
        }
        if (format == "crop")
        {
            var outputFormat = Path.GetExtension(outputPath).TrimStart('.').ToLowerInvariant();
            image.Format = outputFormat switch
            {
                "jpg" or "jpeg" => MagickFormat.Jpeg,
                "bmp" => MagickFormat.Bmp,
                "gif" => MagickFormat.Gif,
                "tif" or "tiff" => MagickFormat.Tiff,
                "webp" => MagickFormat.WebP,
                "avif" => MagickFormat.Avif,
                "heic" => MagickFormat.Heic,
                _ => MagickFormat.Png
            };
        }
        image.Write(outputPath);
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
        else if (format == "gz")
        {
            using var input = File.OpenRead(sourcePath);
            using var output = File.Create(outputPath);
            using var gzip = new GZipStream(output, CompressionLevel.Optimal);
            input.CopyTo(gzip);
        }
        else if (format == "tar")
        {
            using var archive = TarArchive.CreateArchive();
            archive.AddEntry(Path.GetFileName(sourcePath), sourcePath);
            archive.SaveTo(outputPath, CompressionType.None);
        }
        else
        {
            using var writer = WriterFactory.OpenWriter(outputPath, ArchiveType.SevenZip, new SevenZipWriterOptions(CompressionType.LZMA2)
            {
                CompressHeader = true,
                LzmaProperties = new LzmaEncoderProperties()
            });
            using var input = File.OpenRead(sourcePath);
            writer.Write(Path.GetFileName(sourcePath), input, File.GetLastWriteTime(sourcePath));
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
        var root = Path.GetFullPath(outputPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (Path.GetExtension(sourcePath).Equals(".rar", StringComparison.OrdinalIgnoreCase)
            || sourcePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(sourcePath).Equals(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(sourcePath);
            using var reader = ReaderFactory.OpenReader(stream);
            while (reader.MoveToNextEntry())
            {
                if (reader.Entry.IsDirectory || string.IsNullOrWhiteSpace(reader.Entry.Key)) continue;
                var target = ResolveArchiveTarget(root, reader.Entry.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = reader.OpenEntryStream();
                using var output = File.Create(target);
                input.CopyTo(output);
            }
            return;
        }

        using var archive = ArchiveFactory.OpenArchive(sourcePath);
        foreach (var entry in archive.Entries.Where(item => !item.IsDirectory && !string.IsNullOrWhiteSpace(item.Key)))
        {
            var target = ResolveArchiveTarget(root, entry.Key!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var input = entry.OpenEntryStream();
            using var output = File.Create(target);
            input.CopyTo(output);
        }
    }

    private static string ResolveArchiveTarget(string root, string entryName)
    {
        var target = Path.GetFullPath(Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The archive contains a path outside the extraction folder.");
        return target;
    }

    private static void ConvertMedia(string sourcePath, string outputPath, string format)
    {
        var executable = FfmpegPath ?? throw new NotSupportedException("Install FFmpeg or place ffmpeg.exe in the app's tools folder to convert audio and video.");
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sourcePath);
        if (format == "trim")
        {
            startInfo.ArgumentList.Add("-t");
            startInfo.ArgumentList.Add("10");
        }
        if (format == "mp3" && VideoInputs.Contains(Path.GetExtension(sourcePath).ToLowerInvariant())) startInfo.ArgumentList.Add("-vn");
        if (format == "gif")
        {
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add("fps=12,scale=720:-1:flags=lanczos");
            startInfo.ArgumentList.Add("-an");
        }
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
            using var input = File.OpenRead(sourcePath);
            var pageCount = Conversion.GetPageCount(input, leaveOpen: true, password: null);
            input.Position = 0;
            var options = new PDFtoImage.RenderOptions { Dpi = 300 };
            var pages = Conversion.ToImages(input, Enumerable.Range(0, pageCount), leaveOpen: false, password: null, options);
            var stem = Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileNameWithoutExtension(outputPath));
            var index = 0;
            foreach (var page in pages)
            {
                var pagePath = pageCount == 1 || index == 0 ? outputPath : $"{stem}-{index + 1:000}.{format}";
                using (page)
                using (var encoded = page.Encode(format == "jpg" ? SkiaSharp.SKEncodedImageFormat.Jpeg : SkiaSharp.SKEncodedImageFormat.Png, 95))
                using (var output = File.Create(pagePath))
                    encoded.SaveTo(output);
                index++;
            }
            return;
        }

        string text;
        using (var document = UglyToad.PdfPig.PdfDocument.Open(sourcePath))
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
            case "pptx":
                BasicOfficeWriter.WritePresentation(text, outputPath);
                break;
            case "xlsx": BasicOfficeWriter.WriteSpreadsheet(text, outputPath); break;
            case "md": File.WriteAllText(outputPath, text, new UTF8Encoding(false)); break;
            default: File.WriteAllText(outputPath, text, new UTF8Encoding(false)); break;
        }
    }

    private static void SplitPdf(string sourcePath, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        for (var index = 0; index < source.PageCount; index++)
        {
            using var pageDocument = new PdfSharp.Pdf.PdfDocument();
            pageDocument.AddPage(source.Pages[index]);
            pageDocument.Save(Path.Combine(outputFolder, $"page-{index + 1:000}.pdf"));
        }
    }

    private static void ImagesToPdf(IReadOnlyList<string> sourcePaths, string outputPath)
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"OneClickConverter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            for (var index = 0; index < sourcePaths.Count; index++)
            {
                var imagePath = Path.Combine(temporaryDirectory, $"image-{index + 1:000}.png");
                using (var image = new MagickImage(sourcePaths[index]))
                {
                    image.Format = MagickFormat.Png;
                    image.Write(imagePath);
                }
                using var pageImage = XImage.FromFile(imagePath);
                var page = document.AddPage();
                page.Width = XUnit.FromPoint(pageImage.PixelWidth * 0.75);
                page.Height = XUnit.FromPoint(pageImage.PixelHeight * 0.75);
                using var graphics = XGraphics.FromPdfPage(page);
                graphics.DrawImage(pageImage, 0, 0, page.Width.Point, page.Height.Point);
            }
            document.Save(outputPath);
        }
        finally { Directory.Delete(temporaryDirectory, true); }
    }

    private static void MergePdfs(IReadOnlyList<string> sourcePaths, string outputPath)
    {
        if (sourcePaths.Count < 2) throw new ArgumentException("Select at least two PDF files to merge.");
        using var merged = new PdfSharp.Pdf.PdfDocument();
        foreach (var sourcePath in sourcePaths)
        {
            using var source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
            foreach (var page in source.Pages) merged.AddPage(page);
        }
        merged.Save(outputPath);
    }

    private static void ConvertSlidesToImages(string sourcePath, string outputPath, string format)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"OneClickConverter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var pdfPath = Path.Combine(temporaryDirectory, "slides.pdf");
            ConvertWithLibreOffice(sourcePath, pdfPath, "pdf");
            ConvertPdf(pdfPath, outputPath, format);
        }
        finally { Directory.Delete(temporaryDirectory, true); }
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

