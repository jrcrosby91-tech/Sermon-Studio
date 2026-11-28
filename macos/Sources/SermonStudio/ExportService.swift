import AppKit
import CoreText
import SwiftUI
import UniformTypeIdentifiers
import SermonCore

enum ExportFormat: String, CaseIterable, Identifiable {
    case txt = "Plain text", md = "Markdown", rtf = "Rich text (RTF)", docx = "Word (DOCX)", pdf = "PDF", srt = "Subtitles (SRT)", vtt = "Subtitles (WebVTT)"
    var id: String { rawValue }
    var fileExtension: String {
        switch self { case .txt: return "txt"; case .md: return "md"; case .rtf: return "rtf"; case .docx: return "docx"; case .pdf: return "pdf"; case .srt: return "srt"; case .vtt: return "vtt" }
    }
    static let transcript: [ExportFormat] = [.txt, .md, .rtf, .docx, .pdf, .srt, .vtt]
    static let outline: [ExportFormat] = [.txt, .md, .rtf, .docx, .pdf]
}

extension AppModel {
    private var subtitleLine: String {
        var parts = [draft.date]
        if !draft.speaker.isEmpty { parts.append(draft.speaker) }
        if !draft.primaryScripture.isEmpty { parts.append(draft.primaryScripture) }
        return parts.filter { !$0.isEmpty }.joined(separator: " · ")
    }

    private func data(for format: ExportFormat, outline: Bool, record: SermonRecord) throws -> Data {
        let title = record.displayTitle + (outline ? " — Outline" : "")
        let body = outline ? record.outline : (record.cleaned.isEmpty || !editingCleaned ? record.original : record.cleaned)
        switch format {
        case .txt: return Data(((outline ? DocumentWriters.plainText(body) : body) + "\n").utf8)
        case .md: return DocumentWriters.markdown(title: title, body: body)
        case .rtf: return DocumentWriters.rtf(title: title, body: body, formatted: outline)
        case .docx: return DocumentWriters.docx(title: title, subtitle: subtitleLine, body: body, formatted: outline)
        case .srt, .vtt:
            let segments = record.segments ?? Subtitles.parse(record.srt)
            guard !segments.isEmpty else { throw NSError(domain: "SermonStudio.Export", code: 1, userInfo: [NSLocalizedDescriptionKey: "This sermon has no timestamps. Transcribe a recording first."]) }
            return Data(Subtitles.make(segments, vtt: format == .vtt).utf8)
        case .pdf:
            let temp = FileManager.default.temporaryDirectory.appendingPathComponent("sermon-" + UUID().uuidString + ".pdf")
            defer { try? FileManager.default.removeItem(at: temp) }
            try PDFExport.write(PDFExport.attributedDocument(title: title, subtitle: subtitleLine, body: body, formatted: outline), to: temp, title: title)
            return try Data(contentsOf: temp)
        }
    }

    /// Exports the open sermon. With a default export folder the file gets a new unique name (never
    /// overwriting); otherwise a save panel asks, and macOS confirms before replacing a file.
    func export(_ format: ExportFormat, outline: Bool) {
        outlineEditor.flush()
        let record = draft
        if outline && record.outline.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { status = "Write or generate an outline before exporting it."; return }
        let base = ExportNaming.sanitize(record.displayTitle) + (outline ? "-Outline" : "")
        do {
            let contents = try data(for: format, outline: outline, record: record)
            let destination: URL
            if exportFolder != nil {
                destination = try ExportNaming.reserveNewFile(in: try exportDirectory(), base: base, extension: "." + format.fileExtension)
            } else {
                let panel = NSSavePanel()
                panel.nameFieldStringValue = base + "." + format.fileExtension
                if let type = UTType(filenameExtension: format.fileExtension) { panel.allowedContentTypes = [type] }
                guard panel.runModal() == .OK, let url = panel.url else { return }
                destination = url
            }
            try AtomicFile.write(contents, to: destination)
            lastExport = destination
            status = "Exported " + format.rawValue + ": " + destination.path
        } catch { status = "Export failed: " + error.localizedDescription }
    }

    /// Exports sermons into a new folder: a cross-platform archive plus transcript, outline and Word files for each.
    func bulkExport(_ records: [SermonRecord], label: String) {
        guard flushPendingSave() else { status = "Save the open sermon before exporting."; return }
        guard !records.isEmpty else { status = "No sermons to export."; return }
        let panel = NSOpenPanel()
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.canCreateDirectories = true
        panel.prompt = "Export Here"
        panel.directoryURL = try? exportDirectory()
        guard panel.runModal() == .OK, let parent = panel.url else { return }
        do {
            let name = ExportNaming.uniqueBaseName(in: parent, base: "Sermon Studio " + label + " " + ArchiveDates.day(Date()), suffixes: [""])
            let folder = parent.appendingPathComponent(name)
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: false)
            try AtomicFile.write(ArchiveCodec.encodeLibrary(records), to: folder.appendingPathComponent("sermons.json"))
            for record in records {
                let base = ExportNaming.uniqueBaseName(in: folder, base: ExportNaming.sanitize(record.displayTitle, maxLength: 70), suffixes: ["-Transcript.txt", "-Outline.md", "-Outline.docx"])
                try AtomicFile.write(Data((record.cleaned.isEmpty ? record.original : record.cleaned).utf8), to: folder.appendingPathComponent(base + "-Transcript.txt"))
                try AtomicFile.write(DocumentWriters.markdown(title: record.displayTitle, body: record.outline), to: folder.appendingPathComponent(base + "-Outline.md"))
                try AtomicFile.write(DocumentWriters.docx(title: record.displayTitle + " — Outline", subtitle: record.date, body: record.outline, formatted: true), to: folder.appendingPathComponent(base + "-Outline.docx"))
            }
            lastExport = folder
            status = "Exported \(records.count) sermons to " + folder.path
        } catch { status = "Bulk export failed: " + error.localizedDescription }
    }

    /// Native printing (and Save as PDF) with automatic pagination.
    func printDocument(outline: Bool) {
        outlineEditor.flush()
        let body = outline ? draft.outline : activeTranscript
        let document = PDFExport.attributedDocument(title: draft.displayTitle + (outline ? " — Outline" : ""), subtitle: subtitleLine, body: body, formatted: outline)
        let info = NSPrintInfo.shared.copy() as! NSPrintInfo
        info.horizontalPagination = .fit
        info.verticalPagination = .automatic
        info.topMargin = 54; info.bottomMargin = 54; info.leftMargin = 54; info.rightMargin = 54
        let width = info.paperSize.width - info.leftMargin - info.rightMargin
        let view = NSTextView(frame: NSRect(x: 0, y: 0, width: width, height: 100))
        view.textStorage?.setAttributedString(Self.appKitDocument(document))
        view.isEditable = false
        view.sizeToFit()
        let operation = NSPrintOperation(view: view, printInfo: info)
        operation.jobTitle = draft.displayTitle
        operation.run()
    }

    /// Converts the Core Text document used for PDF export into AppKit attributes for printing.
    private static func appKitDocument(_ source: NSAttributedString) -> NSAttributedString {
        let output = NSMutableAttributedString(string: source.string)
        source.enumerateAttributes(in: NSRange(location: 0, length: source.length)) { attributes, range, _ in
            for (key, value) in attributes {
                if key.rawValue == (kCTFontAttributeName as String), CFGetTypeID(value as CFTypeRef) == CTFontGetTypeID() {
                    output.addAttribute(.font, value: (value as! CTFont) as NSFont, range: range)
                } else if key.rawValue == "SermonStudioLink", let url = value as? URL {
                    output.addAttribute(.link, value: url, range: range)
                } else if key.rawValue == (kCTParagraphStyleAttributeName as String) {
                    let style = NSMutableParagraphStyle()
                    let paragraph = value as! CTParagraphStyle
                    var indent: CGFloat = 0, after: CGFloat = 0
                    CTParagraphStyleGetValueForSpecifier(paragraph, .headIndent, MemoryLayout<CGFloat>.size, &indent)
                    CTParagraphStyleGetValueForSpecifier(paragraph, .paragraphSpacing, MemoryLayout<CGFloat>.size, &after)
                    style.headIndent = indent; style.firstLineHeadIndent = indent; style.paragraphSpacing = after
                    output.addAttribute(.paragraphStyle, value: style, range: range)
                }
            }
        }
        output.addAttribute(.foregroundColor, value: NSColor.black, range: NSRange(location: 0, length: output.length))
        return output
    }
}
