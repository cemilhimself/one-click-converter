using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace OneClickConverter;

internal static class BasicOfficeWriter
{
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void WriteSpreadsheet(string text, string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteXml(archive, "[Content_Types].xml", new XDocument(new XElement(ContentTypes + "Types",
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
        WriteXml(archive, "_rels/.rels", new XDocument(new XElement(Relationships + "Relationships",
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "xl/workbook.xml")))));
        WriteXml(archive, "xl/workbook.xml", new XDocument(new XElement(XName.Get("workbook", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
            new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
            new XElement(XName.Get("sheets", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"),
                new XElement(XName.Get("sheet", "http://schemas.openxmlformats.org/spreadsheetml/2006/main"), new XAttribute("name", "PDF text"), new XAttribute("sheetId", 1), new XAttribute(OfficeRelationships + "id", "rId1"))))));
        WriteXml(archive, "xl/_rels/workbook.xml.rels", new XDocument(new XElement(Relationships + "Relationships",
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")))));

        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = text.Replace("\f", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        var sheetData = new XElement(spreadsheet + "sheetData");
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            var row = new XElement(spreadsheet + "row", new XAttribute("r", rowIndex + 1));
            var cells = rows[rowIndex].Split('\t');
            for (var columnIndex = 0; columnIndex < cells.Length; columnIndex++)
                row.Add(new XElement(spreadsheet + "c", new XAttribute("r", CellReference(columnIndex, rowIndex + 1)), new XAttribute("t", "inlineStr"),
                    new XElement(spreadsheet + "is", new XElement(spreadsheet + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), cells[columnIndex]))));
            sheetData.Add(row);
        }
        WriteXml(archive, "xl/worksheets/sheet1.xml", new XDocument(new XElement(spreadsheet + "worksheet", sheetData)));
    }

    public static void WritePresentation(string text, string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteXml(archive, "[Content_Types].xml", new XDocument(new XElement(ContentTypes + "Types",
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypes + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/ppt/presentation.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/ppt/slides/slide1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slide+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/ppt/slideLayouts/slideLayout1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/ppt/slideMasters/slideMaster1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml")),
            new XElement(ContentTypes + "Override", new XAttribute("PartName", "/ppt/theme/theme1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.theme+xml")))));
        WriteXml(archive, "_rels/.rels", new XDocument(new XElement(Relationships + "Relationships",
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"), new XAttribute("Target", "ppt/presentation.xml")))));
        WriteXml(archive, "ppt/presentation.xml", new XDocument(new XElement(Presentation + "presentation",
            new XAttribute(XNamespace.Xmlns + "a", Drawing), new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
            new XElement(Presentation + "sldMasterIdLst", new XElement(Presentation + "sldMasterId", new XAttribute("id", 2147483648L), new XAttribute(OfficeRelationships + "id", "rId1"))),
            new XElement(Presentation + "sldIdLst", new XElement(Presentation + "sldId", new XAttribute("id", 256), new XAttribute(OfficeRelationships + "id", "rId2"))),
            new XElement(Presentation + "sldSz", new XAttribute("cx", 9144000), new XAttribute("cy", 6858000), new XAttribute("type", "screen4x3")),
            new XElement(Presentation + "notesSz", new XAttribute("cx", 6858000), new XAttribute("cy", 9144000)))));
        WriteXml(archive, "ppt/_rels/presentation.xml.rels", new XDocument(new XElement(Relationships + "Relationships",
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster"), new XAttribute("Target", "slideMasters/slideMaster1.xml")),
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide"), new XAttribute("Target", "slides/slide1.xml")))));

        var paragraphs = text.Replace("\f", "\n").Split('\n').Where(line => line.Length > 0).Select(line =>
            new XElement(Drawing + "p", new XElement(Drawing + "r", new XElement(Drawing + "rPr", new XAttribute("lang", "en-US"), new XAttribute("sz", 1400)),
                new XElement(Drawing + "t", line)), new XElement(Drawing + "endParaRPr", new XAttribute("lang", "en-US")))).ToArray();
        var shapeTree = new XElement(Presentation + "spTree",
            new XElement(Presentation + "nvGrpSpPr",
                new XElement(Presentation + "cNvPr", new XAttribute("id", 1), new XAttribute("name", "")),
                new XElement(Presentation + "cNvGrpSpPr"), new XElement(Presentation + "nvPr")),
            new XElement(Presentation + "grpSpPr",
                new XElement(Drawing + "xfrm",
                    new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(Drawing + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0)),
                    new XElement(Drawing + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)),
                    new XElement(Drawing + "chExt", new XAttribute("cx", 0), new XAttribute("cy", 0)))),
            new XElement(Presentation + "sp",
                new XElement(Presentation + "nvSpPr",
                    new XElement(Presentation + "cNvPr", new XAttribute("id", 2), new XAttribute("name", "PDF text")),
                    new XElement(Presentation + "cNvSpPr", new XAttribute("txBox", 1)), new XElement(Presentation + "nvPr")),
                new XElement(Presentation + "spPr",
                    new XElement(Drawing + "xfrm",
                        new XElement(Drawing + "off", new XAttribute("x", 457200), new XAttribute("y", 274320)),
                        new XElement(Drawing + "ext", new XAttribute("cx", 8229600), new XAttribute("cy", 6309360))),
                    new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst"))),
                new XElement(Presentation + "txBody",
                    new XElement(Drawing + "bodyPr", new XAttribute("wrap", "square")),
                    new XElement(Drawing + "lstStyle"), paragraphs)));
        WriteXml(archive, "ppt/slides/slide1.xml", new XDocument(new XElement(Presentation + "sld", new XAttribute(XNamespace.Xmlns + "a", Drawing), new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
            new XElement(Presentation + "cSld", shapeTree), new XElement(Presentation + "clrMapOvr", new XElement(Drawing + "masterClrMapping")))));
        WriteXml(archive, "ppt/slides/_rels/slide1.xml.rels", new XDocument(new XElement(Relationships + "Relationships",
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout"), new XAttribute("Target", "../slideLayouts/slideLayout1.xml")))));

        var masterTree = new XElement(Presentation + "spTree",
            new XElement(Presentation + "nvGrpSpPr", new XElement(Presentation + "cNvPr", new XAttribute("id", 1), new XAttribute("name", "")), new XElement(Presentation + "cNvGrpSpPr"), new XElement(Presentation + "nvPr")),
            new XElement(Presentation + "grpSpPr", new XElement(Drawing + "xfrm", new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(Drawing + "ext", new XAttribute("cx", 0), new XAttribute("cy", 0)), new XElement(Drawing + "chOff", new XAttribute("x", 0), new XAttribute("y", 0)), new XElement(Drawing + "chExt", new XAttribute("cx", 0), new XAttribute("cy", 0)))));
        WriteXml(archive, "ppt/slideLayouts/slideLayout1.xml", new XDocument(new XElement(Presentation + "sldLayout", new XAttribute("type", "blank"), new XAttribute("preserve", 1),
            new XAttribute(XNamespace.Xmlns + "a", Drawing), new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
            new XElement(Presentation + "cSld", new XAttribute("name", "Blank"), masterTree), new XElement(Presentation + "clrMapOvr", new XElement(Drawing + "masterClrMapping")))));
        WriteXml(archive, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", new XDocument(new XElement(Relationships + "Relationships",
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster"), new XAttribute("Target", "../slideMasters/slideMaster1.xml")))));
        WriteXml(archive, "ppt/slideMasters/slideMaster1.xml", new XDocument(new XElement(Presentation + "sldMaster",
            new XAttribute(XNamespace.Xmlns + "a", Drawing), new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships),
            new XElement(Presentation + "cSld", masterTree),
            new XElement(Presentation + "clrMap", new XAttribute("accent1", "accent1"), new XAttribute("accent2", "accent2"), new XAttribute("accent3", "accent3"), new XAttribute("accent4", "accent4"), new XAttribute("accent5", "accent5"), new XAttribute("accent6", "accent6"), new XAttribute("bg1", "lt1"), new XAttribute("bg2", "lt2"), new XAttribute("folHlink", "folHlink"), new XAttribute("hlink", "hlink"), new XAttribute("tx1", "dk1"), new XAttribute("tx2", "dk2")),
            new XElement(Presentation + "sldLayoutIdLst", new XElement(Presentation + "sldLayoutId", new XAttribute("id", 1), new XAttribute(OfficeRelationships + "id", "rId1"))))));
        WriteXml(archive, "ppt/slideMasters/_rels/slideMaster1.xml.rels", new XDocument(new XElement(Relationships + "Relationships",
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout"), new XAttribute("Target", "../slideLayouts/slideLayout1.xml")),
            new XElement(Relationships + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme"), new XAttribute("Target", "../theme/theme1.xml")))));
        WriteXml(archive, "ppt/theme/theme1.xml", Theme());
    }

    private static XDocument Theme()
    {
        var colors = new XElement(Drawing + "clrScheme", new XAttribute("name", "Office"),
            new XElement(Drawing + "dk1", new XElement(Drawing + "sysClr", new XAttribute("val", "windowText"), new XAttribute("lastClr", "000000"))),
            new XElement(Drawing + "lt1", new XElement(Drawing + "sysClr", new XAttribute("val", "window"), new XAttribute("lastClr", "FFFFFF"))),
            new XElement(Drawing + "dk2", new XElement(Drawing + "srgbClr", new XAttribute("val", "44546A"))),
            new XElement(Drawing + "lt2", new XElement(Drawing + "srgbClr", new XAttribute("val", "E7E6E6"))));
        for (var index = 1; index <= 6; index++) colors.Add(new XElement(Drawing + $"accent{index}", new XElement(Drawing + "srgbClr", new XAttribute("val", index % 2 == 0 ? "70AD47" : "4472C4"))));
        colors.Add(new XElement(Drawing + "hlink", new XElement(Drawing + "srgbClr", new XAttribute("val", "0563C1"))));
        colors.Add(new XElement(Drawing + "folHlink", new XElement(Drawing + "srgbClr", new XAttribute("val", "954F72"))));
        var fontScheme = new XElement(Drawing + "fontScheme", new XAttribute("name", "Office"), new XElement(Drawing + "majorFont", new XElement(Drawing + "latin", new XAttribute("typeface", "Aptos Display"))), new XElement(Drawing + "minorFont", new XElement(Drawing + "latin", new XAttribute("typeface", "Aptos"))));
        var formatScheme = new XElement(Drawing + "fmtScheme", new XAttribute("name", "Office"),
            new XElement(Drawing + "fillStyleLst", Enumerable.Range(0, 3).Select(_ => new XElement(Drawing + "solidFill", new XElement(Drawing + "schemeClr", new XAttribute("val", "phClr"))))),
            new XElement(Drawing + "lnStyleLst", Enumerable.Range(0, 3).Select(_ => new XElement(Drawing + "ln", new XAttribute("w", 9525), new XElement(Drawing + "solidFill", new XElement(Drawing + "schemeClr", new XAttribute("val", "phClr"))), new XElement(Drawing + "prstDash", new XAttribute("val", "solid"))))),
            new XElement(Drawing + "effectStyleLst", Enumerable.Range(0, 3).Select(_ => new XElement(Drawing + "effectStyle", new XElement(Drawing + "effectLst")))),
            new XElement(Drawing + "bgFillStyleLst", Enumerable.Range(0, 3).Select(_ => new XElement(Drawing + "solidFill", new XElement(Drawing + "schemeClr", new XAttribute("val", "phClr"))))));
        return new XDocument(new XElement(Drawing + "theme", new XAttribute("name", "Office Theme"), new XElement(Drawing + "themeElements", colors, fontScheme, formatScheme)));
    }

    private static string CellReference(int columnIndex, int rowIndex)
    {
        var column = string.Empty;
        for (var value = columnIndex + 1; value > 0; value = (value - 1) / 26)
            column = (char)('A' + (value - 1) % 26) + column;
        return column + rowIndex;
    }

    private static void WriteXml(ZipArchive archive, string name, XDocument document)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        document.Save(writer);
    }
}

