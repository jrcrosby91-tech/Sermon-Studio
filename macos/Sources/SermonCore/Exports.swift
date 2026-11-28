import Foundation
#if canImport(Darwin)
import Darwin
#elseif canImport(Glibc)
import Glibc
#endif

/// File naming for exports. Names are safe on macOS and Windows (archives are shared between them), and an
/// existing file is never replaced.
public enum ExportNaming {
    private static let reserved: Set<String> = ["CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"]
    private static let invalid = Set("\\/:*?\"<>|")

    public static func sanitize(_ name: String?, fallback: String = "Sermon", maxLength: Int = 80) -> String {
        var value = ""
        for ch in (name ?? "").precomposedStringWithCanonicalMapping {
            if ch.isWhitespace || ch.isNewline { value.append(" ") }
            else if invalid.contains(ch) || ch.unicodeScalars.contains(where: { $0.value < 32 || $0.value == 127 }) { value.append("_") }
            else { value.append(ch) }
        }
        value = value.split(separator: " ", omittingEmptySubsequences: true).joined(separator: " ")
        value = trimEnds(value)
        if value.count > maxLength { value = trimEnds(String(value.prefix(maxLength))) }
        // A leading dot would make a hidden file on macOS.
        while value.hasPrefix(".") { value.removeFirst() }
        if value.isEmpty || value.allSatisfy({ $0 == "_" }) { value = fallback }
        let stem = String(value.split(separator: ".", maxSplits: 1, omittingEmptySubsequences: false).first ?? "").trimmingCharacters(in: .whitespaces)
        if reserved.contains(stem.uppercased()) { value = "_" + value }
        return value
    }

    private static func trimEnds(_ value: String) -> String {
        var result = value.trimmingCharacters(in: .whitespaces)
        while result.hasSuffix(".") || result.hasSuffix(" ") { result.removeLast() }
        return result
    }

    /// A base name for which every `name + suffix` is unused ("Title", "Title (2)", …).
    public static func uniqueBaseName(in folder: URL, base: String, suffixes: [String]) -> String {
        let fm = FileManager.default
        let endings = suffixes.isEmpty ? [""] : suffixes
        for n in 1..<10_000 {
            let candidate = n == 1 ? base : "\(base) (\(n))"
            if endings.allSatisfy({ !fm.fileExists(atPath: folder.appendingPathComponent(candidate + $0).path) }) { return candidate }
        }
        return base + "-" + String(UUID().uuidString.prefix(8))
    }

    /// Atomically creates a new, previously nonexistent file and returns it. Callers then overwrite their own
    /// empty placeholder, so a file created concurrently by someone else is never replaced.
    public static func reserveNewFile(in folder: URL, base: String, extension ext: String) throws -> URL {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        for n in 1..<10_000 {
            let url = folder.appendingPathComponent((n == 1 ? base : "\(base) (\(n))") + ext)
            let fd = open(url.path, O_CREAT | O_EXCL | O_WRONLY, 0o644)
            if fd >= 0 { close(fd); return url }
            if errno != EEXIST { throw NSError(domain: NSPOSIXErrorDomain, code: Int(errno), userInfo: [NSLocalizedDescriptionKey: String(cString: strerror(errno))]) }
        }
        throw CocoaError(.fileWriteFileExists)
    }
}

/// Minimal ZIP writer (stored entries) used for DOCX packages; no external zip tool is required.
public struct ZipWriter {
    private var entries: [(name: String, data: Data, crc: UInt32, offset: UInt32)] = []
    private var body = Data()
    public init() {}

    private static let crcTable: [UInt32] = (0..<256).map { n -> UInt32 in
        var c = UInt32(n)
        for _ in 0..<8 { c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1 }
        return c
    }

    public static func crc32(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xFFFFFFFF
        for byte in data { crc = crcTable[Int((crc ^ UInt32(byte)) & 0xFF)] ^ (crc >> 8) }
        return crc ^ 0xFFFFFFFF
    }

    private static func le16(_ value: UInt16) -> Data { Data([UInt8(value & 0xFF), UInt8(value >> 8)]) }
    private static func le32(_ value: UInt32) -> Data { Data([UInt8(value & 0xFF), UInt8((value >> 8) & 0xFF), UInt8((value >> 16) & 0xFF), UInt8(value >> 24)]) }

    public mutating func add(_ name: String, _ data: Data) {
        let nameData = Data(name.utf8)
        let crc = Self.crc32(data)
        let offset = UInt32(body.count)
        var header = Data()
        header += Self.le32(0x04034B50) + Self.le16(20) + Self.le16(0x0800) + Self.le16(0) + Self.le16(0) + Self.le16(0x21)
        header += Self.le32(crc) + Self.le32(UInt32(data.count)) + Self.le32(UInt32(data.count))
        header += Self.le16(UInt16(nameData.count)) + Self.le16(0) + nameData
        body += header + data
        entries.append((name, data, crc, offset))
    }

    public func finish() -> Data {
        var central = Data()
        for entry in entries {
            let nameData = Data(entry.name.utf8)
            central += Self.le32(0x02014B50) + Self.le16(20) + Self.le16(20) + Self.le16(0x0800) + Self.le16(0) + Self.le16(0) + Self.le16(0x21)
            central += Self.le32(entry.crc) + Self.le32(UInt32(entry.data.count)) + Self.le32(UInt32(entry.data.count))
            central += Self.le16(UInt16(nameData.count)) + Self.le16(0) + Self.le16(0) + Self.le16(0) + Self.le16(0) + Self.le32(0)
            central += Self.le32(entry.offset) + nameData
        }
        var end = Data()
        end += Self.le32(0x06054B50) + Self.le16(0) + Self.le16(0) + Self.le16(UInt16(entries.count)) + Self.le16(UInt16(entries.count))
        end += Self.le32(UInt32(central.count)) + Self.le32(UInt32(body.count)) + Self.le16(0)
        return body + central + end
    }
}

/// DOCX, RTF, Markdown and plain-text writers. Formatting (headings, bold, italic, lists, scripture
/// quotations), Scripture hyperlinks and Unicode are preserved.
public enum DocumentWriters {
    public static func xmlEscape(_ value: String) -> String {
        var output = ""
        for scalar in value.unicodeScalars {
            switch scalar {
            case "&": output += "&amp;"
            case "<": output += "&lt;"
            case ">": output += "&gt;"
            case "\"": output += "&quot;"
            default:
                let v = scalar.value
                // XML 1.0 only allows these characters; others (control characters) are dropped.
                if v == 0x9 || v == 0xA || v == 0xD || (v >= 0x20 && v <= 0xD7FF) || (v >= 0xE000 && v <= 0xFFFD) || v >= 0x10000 { output.unicodeScalars.append(scalar) }
            }
        }
        return output
    }

    public static func docx(title: String, subtitle: String? = nil, body: String, formatted: Bool) -> Data {
        var links: [URL] = []
        func run(_ text: String, bold: Bool, italic: Bool, size: Int?) -> String {
            var props = ""
            if bold { props += "<w:b/>" }
            if italic { props += "<w:i/>" }
            if let size { props += "<w:sz w:val=\"\(size)\"/>" }
            return "<w:r><w:rPr>" + props + "</w:rPr><w:t xml:space=\"preserve\">" + xmlEscape(text) + "</w:t></w:r>"
        }
        func linkedRuns(_ text: String, bold: Bool, italic: Bool, size: Int?) -> String {
            ScriptureReferences.linkSegments(text).map { part in
                guard let url = part.url else { return run(part.text, bold: bold, italic: italic, size: size) }
                links.append(url)
                let props = (bold ? "<w:b/>" : "") + (italic ? "<w:i/>" : "") + "<w:color w:val=\"1F4E79\"/><w:u w:val=\"single\"/>" + (size.map { "<w:sz w:val=\"\($0)\"/>" } ?? "")
                return "<w:hyperlink r:id=\"rIdLink\(links.count)\"><w:r><w:rPr>" + props + "</w:rPr><w:t xml:space=\"preserve\">" + xmlEscape(part.text) + "</w:t></w:r></w:hyperlink>"
            }.joined()
        }
        func paragraph(_ content: String, indent: Int? = nil, after: Int = 120) -> String {
            "<w:p><w:pPr><w:spacing w:after=\"\(after)\"/>" + (indent.map { "<w:ind w:left=\"\($0)\"/>" } ?? "") + "</w:pPr>" + content + "</w:p>"
        }
        var paragraphs = [paragraph(run(title, bold: true, italic: false, size: 36), after: 200)]
        if let subtitle, !subtitle.isEmpty { paragraphs.append(paragraph(run(subtitle, bold: false, italic: true, size: nil), after: 240)) }
        for raw in OutlineMarkdown.splitLines(body) {
            guard formatted else { paragraphs.append(paragraph(linkedRuns(raw, bold: false, italic: false, size: nil))); continue }
            let line = OutlineMarkdown.parseLine(raw)
            let heading = line.block == .heading, quote = line.block == .quote
            let size: Int? = heading ? (line.level <= 2 ? 32 : 28) : nil
            var content = ""
            if line.block == .bullet { content += run("• ", bold: false, italic: false, size: nil) }
            if line.block == .numbered { content += run(line.marker, bold: false, italic: false, size: nil) }
            content += line.runs.map { linkedRuns($0.text, bold: heading || $0.bold, italic: quote || $0.italic, size: size) }.joined()
            let indent: Int? = [.quote, .bullet, .numbered].contains(line.block) ? 360 : nil
            paragraphs.append(paragraph(content, indent: indent, after: heading ? 160 : 120))
        }
        let document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>"
            + paragraphs.joined() + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\"/></w:sectPr></w:body></w:document>"
        let documentRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + links.enumerated().map { "<Relationship Id=\"rIdLink\($0.offset + 1)\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink\" Target=\"" + xmlEscape($0.element.absoluteString) + "\" TargetMode=\"External\"/>" }.joined()
            + "</Relationships>"
        var zip = ZipWriter()
        zip.add("[Content_Types].xml", Data("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>".utf8))
        zip.add("_rels/.rels", Data("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>".utf8))
        zip.add("word/document.xml", Data(document.utf8))
        zip.add("word/_rels/document.xml.rels", Data(documentRels.utf8))
        return zip.finish()
    }

    public static func rtfEscape(_ text: String) -> String {
        var output = ""
        for unit in text.utf16 {
            switch unit {
            case 0x5C: output += "\\\\"
            case 0x7B: output += "\\{"
            case 0x7D: output += "\\}"
            case 0x09: output += "\\tab "
            case 0..<0x20: continue
            case 0x20..<0x80: output.unicodeScalars.append(Unicode.Scalar(UInt8(unit)))
            default: output += "\\u" + String(Int16(bitPattern: unit)) + "?"
            }
        }
        return output
    }

    public static func rtf(title: String?, body: String, formatted: Bool) -> Data {
        var b = "{\\rtf1\\ansi\\ansicpg1252\\uc1\\deff0{\\fonttbl{\\f0\\froman Georgia;}}{\\colortbl;\\red31\\green78\\blue121;}\\f0\\fs22 "
        if let title, !title.isEmpty { b += "{\\b\\fs36 " + rtfEscape(title) + "}\\par\\par " }
        func linked(_ text: String) -> String {
            ScriptureReferences.linkSegments(text).map { part in
                guard let url = part.url else { return rtfEscape(part.text) }
                return "{\\field{\\*\\fldinst HYPERLINK \"" + rtfEscape(url.absoluteString) + "\"}{\\fldrslt{\\cf1\\ul " + rtfEscape(part.text) + "}}}"
            }.joined()
        }
        for raw in OutlineMarkdown.splitLines(body) {
            guard formatted else { b += linked(raw) + "\\par "; continue }
            let line = OutlineMarkdown.parseLine(raw)
            let heading = line.block == .heading, quote = line.block == .quote
            b += [.quote, .bullet, .numbered].contains(line.block) ? "\\pard\\li360 " : "\\pard "
            if heading { b += line.level <= 2 ? "\\fs32 " : "\\fs28 " }
            if line.block == .bullet { b += "\\u8226? " }
            if line.block == .numbered { b += rtfEscape(line.marker) }
            for run in line.runs {
                b += "{"
                if heading || run.bold { b += "\\b" }
                if quote || run.italic { b += "\\i" }
                b += " " + linked(run.text) + "}"
            }
            if heading { b += "\\fs22" }
            b += "\\par "
        }
        return Data((b + "}").utf8)
    }

    public static func markdown(title: String, body: String) -> Data {
        Data(("# " + title + "\n\n" + OutlineMarkdown.normalizeNewlines(body) + "\n").utf8)
    }

    /// Plain text with Markdown markers removed (outline TXT export).
    public static func plainText(_ markdown: String) -> String {
        OutlineMarkdown.parseDocument(markdown).map { line in
            switch line.block {
            case .bullet: return "• " + line.plainText
            case .numbered: return line.marker + line.plainText
            case .quote: return "    " + line.plainText
            default: return line.plainText
            }
        }.joined(separator: "\n")
    }
}
