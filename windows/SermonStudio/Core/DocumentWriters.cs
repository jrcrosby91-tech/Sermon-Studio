using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SermonStudio;

/// <summary>Dependency-free DOCX and RTF writers that preserve outline formatting and Unicode.</summary>
internal static class DocumentWriters
{
    /// <summary>Keeps only characters XML 1.0 allows; valid surrogate pairs are kept, lone surrogates dropped.</summary>
    internal static string XmlSafe(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1])) { builder.Append(c).Append(value[i + 1]); i++; }
            else if (!char.IsSurrogate(c) && XmlConvert.IsXmlChar(c)) builder.Append(c);
        }
        return builder.ToString();
    }

    internal static void WriteDocx(string path, string title, string body, bool formatted)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement Run(string text, bool bold, bool italic, int? size) =>
            new(w + "r",
                new XElement(w + "rPr", bold ? new XElement(w + "b") : null, italic ? new XElement(w + "i") : null, size is int s ? new XElement(w + "sz", new XAttribute(w + "val", s)) : null),
                new XElement(w + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), XmlSafe(text)));
        XElement Paragraph(int? indent, int after, IEnumerable<XElement> runs) =>
            new(w + "p",
                new XElement(w + "pPr", new XElement(w + "spacing", new XAttribute(w + "after", after)), indent is int left ? new XElement(w + "ind", new XAttribute(w + "left", left)) : null),
                runs);

        var paragraphs = new List<XElement> { Paragraph(null, 200, new[] { Run(title, true, false, 36) }) };
        foreach (var raw in OutlineMarkdown.SplitLines(body))
        {
            if (!formatted) { paragraphs.Add(Paragraph(null, 120, new[] { Run(raw, false, false, null) })); continue; }
            var line = OutlineMarkdown.ParseLine(raw);
            bool heading = line.Block == OutlineBlock.Heading, quote = line.Block == OutlineBlock.Quote;
            int? size = heading ? (line.Level <= 2 ? 32 : 28) : null;
            int? indent = line.Block is OutlineBlock.Quote or OutlineBlock.Bullet or OutlineBlock.Numbered ? 360 : null;
            var runs = new List<XElement>();
            if (line.Block == OutlineBlock.Bullet) runs.Add(Run("• ", false, false, null));
            if (line.Block == OutlineBlock.Numbered) runs.Add(Run(line.Marker, false, false, null));
            runs.AddRange(line.Runs.Select(r => Run(r.Text, heading || r.Bold, quote || r.Italic, size)));
            paragraphs.Add(Paragraph(indent, heading ? 160 : 120, runs));
        }
        var document = new XDocument(new XElement(w + "document", new XAttribute(XNamespace.Xmlns + "w", w), new XElement(w + "body", paragraphs)));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        void Add(string name, string data)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
            writer.Write(data);
        }
        Add("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add("word/document.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + document.ToString(SaveOptions.DisableFormatting));
    }

    internal static string RtfEscape(string text)
    {
        var b = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c == '\\' || c == '{' || c == '}') b.Append('\\').Append(c);
            else if (c == '\t') b.Append(@"\tab ");
            else if (c < 32) continue;
            else if (c > 127) b.Append(@"\u").Append((short)c).Append('?');
            else b.Append(c);
        }
        return b.ToString();
    }

    internal static string BuildRtf(string? title, string body, bool formatted)
    {
        var b = new StringBuilder(@"{\rtf1\ansi\ansicpg1252\uc1\deff0{\fonttbl{\f0\froman Georgia;}}\f0\fs22 ");
        if (!string.IsNullOrEmpty(title)) b.Append(@"{\b\fs36 ").Append(RtfEscape(title)).Append(@"}\par\par ");
        foreach (var raw in OutlineMarkdown.SplitLines(body))
        {
            if (!formatted) { b.Append(RtfEscape(raw)).Append(@"\par "); continue; }
            var line = OutlineMarkdown.ParseLine(raw);
            bool heading = line.Block == OutlineBlock.Heading, quote = line.Block == OutlineBlock.Quote;
            b.Append(line.Block is OutlineBlock.Quote or OutlineBlock.Bullet or OutlineBlock.Numbered ? @"\pard\li360 " : @"\pard ");
            if (heading) b.Append(line.Level <= 2 ? @"\fs32 " : @"\fs28 ");
            if (line.Block == OutlineBlock.Bullet) b.Append(@"\u8226? ");
            if (line.Block == OutlineBlock.Numbered) b.Append(RtfEscape(line.Marker));
            foreach (var run in line.Runs)
            {
                b.Append('{');
                if (heading || run.Bold) b.Append(@"\b");
                if (quote || run.Italic) b.Append(@"\i");
                b.Append(' ').Append(RtfEscape(run.Text)).Append('}');
            }
            if (heading) b.Append(@"\fs22");
            b.Append(@"\par ");
        }
        return b.Append('}').ToString();
    }

    internal static void WriteRtf(string path, string? title, string body, bool formatted) =>
        File.WriteAllText(path, BuildRtf(title, body, formatted), Encoding.ASCII);
}
