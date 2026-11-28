import XCTest
@testable import SermonCore
#if canImport(PDFKit)
import PDFKit
#endif

final class MarkdownTests: XCTestCase {
    func testInlineParsing() {
        XCTAssertEqual(OutlineMarkdown.parseInline("Plain **bold** *italic* ***both*** end"), [
            OutlineRun("Plain "), OutlineRun("bold", bold: true), OutlineRun(" "), OutlineRun("italic", italic: true),
            OutlineRun(" "), OutlineRun("both", bold: true, italic: true), OutlineRun(" end")])
        XCTAssertEqual(OutlineMarkdown.parseInline("5 * 3 * 2"), [OutlineRun("5 * 3 * 2")])
        XCTAssertEqual(OutlineMarkdown.parseInline("**unclosed"), [OutlineRun("**unclosed")])
        XCTAssertEqual(OutlineMarkdown.parseInline("a \\*literal\\* b"), [OutlineRun("a *literal* b")])
    }

    func testInlineRoundTrips() {
        for sample in ["**Grace** and *truth*", "***All***", "5 * 3", "x*y", "Pay **$5** *now*", "trailing *", "esc \\*star\\*", "**a *b* c**", "", "Ünïcödé ✝ **恩典** 🙏🏽"] {
            let runs = OutlineMarkdown.parseInline(sample)
            XCTAssertEqual(OutlineMarkdown.parseInline(OutlineMarkdown.serializeInline(runs)), runs, sample)
        }
        XCTAssertEqual(OutlineMarkdown.serializeInline([OutlineRun("a*b", bold: true)]), "**a\\*b**")
        XCTAssertEqual(OutlineMarkdown.serializeInline([OutlineRun("Say "), OutlineRun(" this ", bold: true)]), "Say  **this** ")
        XCTAssertEqual(OutlineMarkdown.serializeInline([OutlineRun("5 * 3")]), "5 * 3")
    }

    func testBlockStructure() {
        let lines = OutlineMarkdown.parseDocument("# Title\r## Main\r\n### Sub\n> *Jn 3:16*\n- one\n* two\n1. first\n#hashtag\n    - indented")
        XCTAssertEqual(lines[0].block, .heading); XCTAssertEqual(lines[0].level, 1); XCTAssertEqual(lines[1].level, 2); XCTAssertEqual(lines[2].level, 3)
        XCTAssertEqual(lines[3].block, .quote); XCTAssertTrue(lines[3].runs[0].italic)
        XCTAssertEqual(lines[4].block, .bullet); XCTAssertEqual(lines[5].plainText, "two")
        XCTAssertEqual(lines[6].block, .numbered); XCTAssertEqual(lines[6].marker, "1. ")
        XCTAssertEqual(lines[7].block, .paragraph); XCTAssertEqual(lines[8].block, .bullet)
        XCTAssertEqual(OutlineMarkdown.splitLines("a\rb\r\nc\nd").count, 4)
    }

    func testVisualRoundTripsPreserveMarkdown() {
        let samples = [
            "## Introduction\n**Grace** is *unearned*.\n\n### Point one\n> For God so loved the world\n- First\n* Second\n1. Numbered\n# Title stays\n#### Deep",
            "Plain text only", "", "\n\n", "## #1 priority", "> **Bold** in quote", "- ***both*** in bullet",
            "Ünïcödé ✝ 恩典 — “quotes” 🙏🏽", "5 * 3 = 15 and **x**",
        ]
        for markdown in samples {
            for base in [14.0, 18.0, 24.0] {
                let stored = OutlineMarkdown.roundTrip(markdown, baseSize: base)
                XCTAssertEqual(OutlineMarkdown.mergeVisualEdit(original: markdown, updated: stored, baseSize: base), OutlineMarkdown.normalizeNewlines(markdown), markdown)
                XCTAssertEqual(OutlineMarkdown.roundTrip(stored, baseSize: base), stored, "Idempotent: " + markdown)
                XCTAssertEqual(OutlineMarkdown.toVisual(markdown, baseSize: base).text.components(separatedBy: "\n").count, OutlineMarkdown.splitLines(markdown).count)
            }
        }
        XCTAssertEqual(OutlineMarkdown.toVisual("## #1 priority", baseSize: 18).text, "#1 priority")
    }

    func testVisualSpansUseUTF16Offsets() {
        let visual = OutlineMarkdown.toVisual("🙏🏽 **b**", baseSize: 18)
        let bold = visual.spans.first { $0.bold }!
        XCTAssertEqual((visual.text as NSString).substring(with: NSRange(location: bold.start, length: bold.end - bold.start)), "b")
        XCTAssertEqual(OutlineMarkdown.toVisual("## Head", baseSize: 24).spans.first?.size, OutlineMarkdown.headingSize(24))
    }

    func testEditingOneLineKeepsOtherSpellings() {
        let merged = OutlineMarkdown.mergeVisualEdit(original: "* alpha\n## Beta\n#### gamma\n\\*literal\\*", updated: OutlineMarkdown.roundTrip("- alpha\n## Beta changed\n#### gamma\n*literal*", baseSize: 18), baseSize: 18)
        let lines = merged.components(separatedBy: "\n")
        XCTAssertEqual(Array(lines.prefix(3)), ["* alpha", "## Beta changed", "#### gamma"])
        XCTAssertEqual(OutlineMarkdown.mergeVisualEdit(original: "* a\n* c", updated: "- a\n- b\n- c", baseSize: 18), "* a\n- b\n* c")
        XCTAssertEqual(OutlineMarkdown.mergeVisualEdit(original: "* a\n* b", updated: "- b", baseSize: 18), "* b")
    }

    func testLineFromVisual() {
        XCTAssertEqual(OutlineMarkdown.lineFromVisual("Heading", firstSize: 24.4, baseSize: 18) { _ in (true, false) }, "## Heading")
        XCTAssertEqual(OutlineMarkdown.lineFromVisual("Sub", firstSize: 20.2, baseSize: 18) { _ in (true, false) }, "### Sub")
        XCTAssertEqual(OutlineMarkdown.lineFromVisual("• item", firstSize: 18, baseSize: 18) { $0 >= 2 ? (true, false) : (false, false) }, "- **item**")
        XCTAssertEqual(OutlineMarkdown.lineFromVisual("❯ verse", firstSize: 18, baseSize: 18) { _ in (false, true) }, "> verse")
    }
}

final class ScriptureTests: XCTestCase {
    func testDetection() {
        let found = ScriptureReferences.distinct("Read John 3:16 and 1 John 4:8, then 2 Cor 5:17-21, Ps 23, Psalm 119:105, Song of Solomon 2:4, I Peter 5:7, Second Timothy 3:16 and Jude 3.")
        XCTAssertEqual(found, ["John 3:16", "1 John 4:8", "2 Corinthians 5:17-21", "Psalm 23", "Psalm 119:105", "Song of Solomon 2:4", "1 Peter 5:7", "2 Timothy 3:16", "Jude 1:3"])
        XCTAssertEqual(ScriptureReferences.distinct("1 John 3:16"), ["1 John 3:16"])
        XCTAssertTrue(ScriptureReferences.distinct("Mark 25:1 Genesis 51 Acts 0").isEmpty)
        XCTAssertTrue(ScriptureReferences.distinct("Corinthians 13").isEmpty)
        XCTAssertTrue(ScriptureReferences.distinct("I am here. I went 3 times. Johnathan 3:4").isEmpty)
        XCTAssertEqual(ScriptureReferences.distinct("Romans 8:28-9:2"), ["Romans 8:28-9:2"])
        XCTAssertEqual(ScriptureReferences.distinct("John 3:16 john 3:16").count, 1)
        XCTAssertTrue(ScriptureReferences.find("See Romans 8", requireVerse: true).isEmpty)
    }

    func testRangesLinksAndPreviews() throws {
        let text = "Text: Eph. 2:8-9!"
        let reference = try XCTUnwrap(ScriptureReferences.find(text).first)
        XCTAssertEqual((text as NSString).substring(with: reference.range), "Eph. 2:8-9")
        XCTAssertEqual(reference.canonical, "Ephesians 2:8-9")
        XCTAssertEqual(ScriptureReferences.link(reference).host, "www.esv.org")
        let preview = try XCTUnwrap(ScriptureReferences.previewURL(reference))
        XCTAssertEqual(preview.host, "bible-api.com")
        XCTAssertTrue(preview.absoluteString.contains("translation=kjv"))
        XCTAssertTrue(ScriptureReferences.previewTranslation.contains("King James"))
        let segments = ScriptureReferences.linkSegments("See John 3:16 today")
        XCTAssertEqual(segments.map(\.text), ["See ", "John 3:16", " today"])
        XCTAssertNotNil(segments[1].url)
    }
}

final class SubtitleTests: XCTestCase {
    func testParsing() {
        let srt = "1\r\n00:00:01,000 --> 00:00:04,250\r\nIn the beginning\r\nwas the Word\r\n\r\n2\r\n01:02:03,004 --> 01:02:05,000\r\nSecond\r\n"
        let segments = Subtitles.parse(srt)
        XCTAssertEqual(segments.count, 2)
        XCTAssertEqual(segments[0].start, 1); XCTAssertEqual(segments[0].end, 4.25)
        XCTAssertEqual(segments[0].text, "In the beginning\nwas the Word")
        XCTAssertEqual(segments[1].start, 3723.004, accuracy: 1e-9)
        let vtt = Subtitles.parse("WEBVTT\n\nNOTE header\n\n00:01.500 --> 00:03.000\nShort form\n\n00:00:04.000 --> 00:00:05.000 align:start\nWith settings")
        XCTAssertEqual(vtt.count, 2); XCTAssertEqual(vtt[0].start, 1.5); XCTAssertEqual(vtt[1].text, "With settings")
        XCTAssertTrue(Subtitles.parse("garbage\n99:99:99,999 --> x\n\n").isEmpty)
        XCTAssertTrue(Subtitles.parse("").isEmpty)
    }

    func testWritingRoundTrip() {
        let segments = [SermonSegment(start: 1, end: 4.25, text: "One"), SermonSegment(start: 3723.004, end: 3725, text: "Two")]
        let again = Subtitles.parse(Subtitles.make(segments, vtt: false))
        XCTAssertEqual(again.map(\.text), ["One", "Two"])
        XCTAssertEqual(again[1].start, 3723.004, accuracy: 1e-9)
        XCTAssertTrue(Subtitles.make(segments, vtt: true).hasPrefix("WEBVTT\n\n00:00:01.000 --> 00:00:04.250"))
        XCTAssertEqual(Subtitles.formatTime(7322.9996, vtt: false), "02:02:03,000")
        XCTAssertEqual(Subtitles.formatTime(-5, vtt: true), "00:00:00.000")
        XCTAssertEqual(Subtitles.parse(Subtitles.make([SermonSegment(start: 1, end: 2, text: "a\n\nb")], vtt: false)).first?.text, "a\nb")
    }
}

final class ExportTests: XCTestCase {
    func testFilenameSanitization() {
        XCTAssertEqual(ExportNaming.sanitize("Faith: Hope / Love?"), "Faith_ Hope _ Love_")
        XCTAssertEqual(ExportNaming.sanitize("CON"), "_CON")
        XCTAssertEqual(ExportNaming.sanitize("lpt1.notes"), "_lpt1.notes")
        XCTAssertEqual(ExportNaming.sanitize("Ending dots... "), "Ending dots")
        XCTAssertEqual(ExportNaming.sanitize("   "), "Sermon")
        XCTAssertEqual(ExportNaming.sanitize(nil), "Sermon")
        XCTAssertEqual(ExportNaming.sanitize(".hidden"), "hidden")
        XCTAssertEqual(ExportNaming.sanitize("Tab\there\nnew"), "Tab here new")
        XCTAssertEqual(ExportNaming.sanitize("恩典与真理 ✝ Gnade"), "恩典与真理 ✝ Gnade")
        XCTAssertLessThanOrEqual(ExportNaming.sanitize(String(repeating: "a", count: 200)).count, 80)
    }

    func testNoOverwrite() throws {
        let dir = try temporaryDirectory()
        let first = try ExportNaming.reserveNewFile(in: dir, base: "Sermon", extension: ".md")
        try Data("existing work".utf8).write(to: first)
        let second = try ExportNaming.reserveNewFile(in: dir, base: "Sermon", extension: ".md")
        XCTAssertEqual(second.lastPathComponent, "Sermon (2).md")
        XCTAssertEqual(try String(contentsOf: first, encoding: .utf8), "existing work")
        try Data().write(to: dir.appendingPathComponent("Talk.docx"))
        XCTAssertEqual(ExportNaming.uniqueBaseName(in: dir, base: "Talk", suffixes: [".txt", ".docx"]), "Talk (2)")
        XCTAssertEqual(ExportNaming.uniqueBaseName(in: dir, base: "Fresh", suffixes: [".txt"]), "Fresh")
    }

    func testDocxPackageAndFormatting() throws {
        let outline = "## Main point\n**Bold** and *italic* and ***both***\n> For God so loved — John 3:16\n- bullet\n1. numbered\nÜnïcödé ✝ 恩典 🙏🏽\u{0001}"
        let data = DocumentWriters.docx(title: "Title & <More>", body: outline, formatted: true)
        let dir = try temporaryDirectory()
        let url = dir.appendingPathComponent("o.docx")
        try data.write(to: url)
        let out = dir.appendingPathComponent("unzipped")
        let unzip = Process()
        unzip.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        unzip.arguments = ["-q", url.path, "-d", out.path]
        try unzip.run(); unzip.waitUntilExit()
        XCTAssertEqual(unzip.terminationStatus, 0, "The DOCX must be a valid ZIP package")
        let xml = try String(contentsOf: out.appendingPathComponent("word/document.xml"), encoding: .utf8)
        XCTAssertNoThrow(try XMLDocument(xmlString: xml, options: []), "document.xml must be well-formed")
        XCTAssertTrue(xml.contains("<w:b/><w:i/>"))
        XCTAssertTrue(xml.contains(">Bold<") && !xml.contains("**"))
        XCTAssertTrue(xml.contains("Ünïcödé ✝ 恩典 🙏🏽"))
        XCTAssertTrue(xml.contains("Title &amp; &lt;More&gt;"))
        XCTAssertTrue(xml.contains("w:hyperlink"), "Scripture references are hyperlinked")
        let rels = try String(contentsOf: out.appendingPathComponent("word/_rels/document.xml.rels"), encoding: .utf8)
        XCTAssertTrue(rels.contains("esv.org"))
        XCTAssertNotNil(try? XMLDocument(xmlString: rels, options: []))
    }

    func testZipCRC() {
        XCTAssertEqual(ZipWriter.crc32(Data("123456789".utf8)), 0xCBF43926)
    }

    func testRTF() {
        let rtf = String(decoding: DocumentWriters.rtf(title: "Title", body: "**Bold** and *italic* and ***both***\n{braces} \\slash 🙏 John 3:16", formatted: true), as: UTF8.self)
        XCTAssertTrue(rtf.hasPrefix("{\\rtf1") && rtf.hasSuffix("}"))
        XCTAssertTrue(rtf.contains("{\\b Bold}") && rtf.contains("{\\i italic}") && rtf.contains("{\\b\\i both}"))
        XCTAssertTrue(rtf.contains("\\u-10179?\\u-8625?"), "Astral characters as UTF-16 units")
        XCTAssertTrue(rtf.contains("\\{braces\\}") && rtf.contains("\\\\slash"))
        XCTAssertTrue(rtf.contains("HYPERLINK"))
        XCTAssertTrue(rtf.unicodeScalars.allSatisfy { $0.value < 128 })
        let unescaped = rtf.replacingOccurrences(of: "\\\\", with: "").replacingOccurrences(of: "\\{", with: "").replacingOccurrences(of: "\\}", with: "")
        XCTAssertEqual(unescaped.filter { $0 == "{" }.count, unescaped.filter { $0 == "}" }.count)
    }

    func testPlainTextExport() {
        XCTAssertEqual(DocumentWriters.plainText("## Head\n- **b**\n1. n\n> q"), "Head\n• b\n1. n\n    q")
    }

    #if canImport(PDFKit)
    func testLongPDFIsPaginatedNotClipped() throws {
        let body = (1...1500).map { "Line \($0) of a long outline with **bold** text and John 3:16." }.joined(separator: "\n")
        let document = PDFExport.attributedDocument(title: "Long", subtitle: "Outline", body: body, formatted: true)
        let url = try temporaryDirectory().appendingPathComponent("long.pdf")
        let pages = try PDFExport.write(document, to: url, title: "Long")
        XCTAssertGreaterThan(pages, 20)
        let pdf = try XCTUnwrap(PDFDocument(url: url))
        XCTAssertEqual(pdf.pageCount, pages)
        let text = try XCTUnwrap(pdf.string)
        XCTAssertTrue(text.contains("Line 1500 of a long outline"), "Last line must be present")
        XCTAssertTrue(text.contains("Line 1 of a long outline"))
        XCTAssertFalse(text.contains("**"))
    }

    func testEmptyPDF() throws {
        let url = try temporaryDirectory().appendingPathComponent("empty.pdf")
        let pages = try PDFExport.write(PDFExport.attributedDocument(title: "", subtitle: nil, body: "", formatted: false), to: url, title: "")
        XCTAssertEqual(pages, 1)
        XCTAssertNotNil(PDFDocument(url: url))
    }
    #endif
}

final class OutlineAndLibraryTests: XCTestCase {
    func testOriginalGenerator() throws {
        let transcript = "Welcome to church this morning everyone. Today we look at grace in Ephesians 2:8. Grace is a gift that we cannot earn ourselves. We respond with faith and with thankful obedience. Let us live as people who have received grace."
        let outline = try XCTUnwrap(OriginalOutlineGenerator.generate(title: "Grace", transcript: transcript))
        XCTAssertTrue(outline.contains("## Introduction") && outline.contains("## Main points") && outline.contains("### Point 1:"))
        XCTAssertTrue(outline.contains("- Ephesians 2:8"))
        XCTAssertTrue(outline.contains("**Title:** Grace"))
        XCTAssertNil(OriginalOutlineGenerator.generate(title: "", transcript: "Too short."))
        // The generated Markdown survives the visual editor unchanged.
        XCTAssertEqual(OutlineMarkdown.mergeVisualEdit(original: outline, updated: OutlineMarkdown.roundTrip(outline, baseSize: 18), baseSize: 18), outline)
    }

    func testOllamaChunksAndPrompts() {
        let transcript = String(repeating: "This is a sentence about faith. ", count: 1000)
        let chunks = OllamaOutlinePrompts.chunks(transcript)
        XCTAssertEqual(chunks.joined(), transcript, "Chunking loses nothing")
        XCTAssertTrue(chunks.dropLast().allSatisfy { $0.count <= OllamaOutlinePrompts.chunkSize && $0.hasSuffix(".") })
        XCTAssertTrue(OllamaOutlinePrompts.outline(title: "T", references: ["John 3:16"], notes: ["n"]).contains("John 3:16"))
        XCTAssertTrue(OllamaOutlinePrompts.chunks("").isEmpty)
        XCTAssertEqual(OllamaClient.base.host, "127.0.0.1")
    }

    func testLibraryFiltersSortsAndStats() {
        var a = SermonRecord(id: "a", title: "Alpha", date: "2026-01-05", speaker: "Reed", series: "Romans", tags: "grace, faith", favorite: true, original: "word one two")
        a.lastEdited = Date(timeIntervalSince1970: 10)
        var b = SermonRecord(id: "b", title: "beta", date: "2026-03-01", speaker: "Kim", collection: "Evening", tags: "hope", original: "three", outline: "## O")
        b.lastEdited = Date(timeIntervalSince1970: 20); b.lastOpened = Date()
        let c = SermonRecord(id: "c", title: "Gamma", date: "2025-12-25", original: "Christmas grace")
        let all = [a, b, c]
        var filter = LibraryFilter()
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .newest).map(\.id), ["b", "a", "c"])
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .oldest).map(\.id), ["c", "a", "b"])
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .title).map(\.id), ["a", "b", "c"])
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .recentlyEdited).map(\.id), ["b", "a", "c"])
        filter.search = "grace"
        XCTAssertEqual(Set(LibraryQuery.apply(all, filter: filter, sort: .newest).map(\.id)), ["a", "c"])
        filter = LibraryFilter(); filter.favoritesOnly = true
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .newest).map(\.id), ["a"])
        filter = LibraryFilter(); filter.tag = "hope"
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .newest).map(\.id), ["b"])
        filter = LibraryFilter(); filter.speaker = "reed"; filter.series = "rom"
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .newest).map(\.id), ["a"])
        filter = LibraryFilter(); filter.collection = "evening"
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .newest).map(\.id), ["b"])
        filter = LibraryFilter(); filter.from = ArchiveDates.dayDate("2026-01-01"); filter.to = ArchiveDates.dayDate("2026-01-31")
        XCTAssertEqual(LibraryQuery.apply(all, filter: filter, sort: .newest).map(\.id), ["a"])
        let stats = LibraryQuery.stats(all)
        XCTAssertEqual(stats.sermons, 3); XCTAssertEqual(stats.favorites, 1); XCTAssertEqual(stats.outlines, 1); XCTAssertEqual(stats.series, 1); XCTAssertEqual(stats.words, 6)
        XCTAssertEqual(LibraryQuery.recent(all).map(\.id), ["b"])
        XCTAssertEqual(LibraryQuery.values(all, \.speaker), ["Kim", "Reed"])
    }

    func testTranscriptTools() {
        XCTAssertEqual(TranscriptTools.clean("Um, so, uh we are, erm, saved"), "so, we are, saved")
        XCTAssertEqual(TranscriptTools.wordCount("One two  three\n— four"), 4)
        let text = "Grace and grace and GRACE"
        XCTAssertEqual(TranscriptTools.matches(in: text, of: "grace", matchCase: false).count, 3)
        XCTAssertEqual(TranscriptTools.matches(in: text, of: "grace", matchCase: true).count, 1)
        let ranges = TranscriptTools.matches(in: text, of: "grace", matchCase: false)
        XCTAssertEqual(TranscriptTools.nextMatch(ranges, after: 1, backwards: false), 1)
        XCTAssertEqual(TranscriptTools.nextMatch(ranges, after: 100, backwards: false), 0, "Wraps around")
        XCTAssertEqual(TranscriptTools.nextMatch(ranges, after: 0, backwards: true), 2, "Wraps backwards")
        XCTAssertEqual(TranscriptTools.replace(in: text, range: ranges[1], with: "mercy"), "Grace and mercy and GRACE")
        let all = TranscriptTools.replaceAll(in: text, of: "grace", with: "love", matchCase: false)
        XCTAssertEqual(all.text, "love and love and love"); XCTAssertEqual(all.count, 3)
        XCTAssertEqual(TranscriptTools.replaceAll(in: "🙏 grace 🙏 grace", of: "grace", with: "x", matchCase: true).text, "🙏 x 🙏 x")
        XCTAssertNotNil(TranscriptTools.locate("**saved   through** faith", in: "For by grace you have been saved through faith."))
        XCTAssertNil(TranscriptTools.locate("absent phrase", in: "text"))
        let segments = [SermonSegment(start: 0, end: 2, text: "a"), SermonSegment(start: 2, end: 5, text: "b"), SermonSegment(start: 10, end: 12, text: "c")]
        XCTAssertEqual(TranscriptTools.activeSegmentIndex(segments, at: 3), 1)
        XCTAssertNil(TranscriptTools.activeSegmentIndex(segments, at: 8))
        XCTAssertEqual(TranscriptTools.activeSegmentIndex(segments, at: 11), 2)
        XCTAssertEqual(TranscriptTools.timestamp(3725), "1:02:05")
    }

    func testAsynchronousIsolation() async {
        let guardian = WorkspaceGuard()
        let ticket = guardian.capture("transcript", "outline")
        XCTAssertTrue(guardian.isCurrent(ticket, "transcript", "outline"))
        XCTAssertFalse(guardian.isCurrent(ticket, "transcript", "edited outline"), "Rejected after user edits")
        guardian.advance()
        XCTAssertFalse(guardian.isCurrent(ticket, "transcript", "outline"), "Rejected after switching sermons")
        let pending = guardian.capture("x")
        let delayed = Task { () -> Bool in
            try? await Task.sleep(nanoseconds: 30_000_000)
            return guardian.isCurrent(pending, "x")
        }
        guardian.advance()
        let stillCurrent = await delayed.value
        XCTAssertFalse(stillCurrent, "Delayed completion is checked against the latest revision")
        await withTaskGroup(of: Void.self) { group in for _ in 0..<100 { group.addTask { guardian.advance() } } }
        XCTAssertGreaterThanOrEqual(guardian.revision, 102)
    }
}
