#if canImport(CoreText)
import Foundation
import CoreText
import CoreGraphics

/// Paginated PDF output with Core Text. Text flows across as many US Letter pages as needed, so long
/// transcripts and outlines are never clipped to a single fixed-height page.
public enum PDFExport {
    public struct Options: Sendable {
        public var pageSize = CGSize(width: 612, height: 792)
        public var margin: CGFloat = 72
        public var baseSize: CGFloat = 12
        public init() {}
    }

    private static func font(_ size: CGFloat, bold: Bool = false, italic: Bool = false) -> CTFont {
        let base = CTFontCreateWithName("Georgia" as CFString, size, nil)
        var traits: CTFontSymbolicTraits = []
        if bold { traits.insert(.traitBold) }
        if italic { traits.insert(.traitItalic) }
        guard !traits.isEmpty else { return base }
        return CTFontCreateCopyWithSymbolicTraits(base, size, nil, traits, traits) ?? base
    }

    private static func paragraphStyle(indent: CGFloat, spacingAfter: CGFloat, spacingBefore: CGFloat = 0) -> CTParagraphStyle {
        var head = indent, first = indent, after = spacingAfter, before = spacingBefore
        return withUnsafePointer(to: &head) { h in
            withUnsafePointer(to: &first) { f in
                withUnsafePointer(to: &after) { a in
                    withUnsafePointer(to: &before) { b in
                        let size = MemoryLayout<CGFloat>.size
                        let settings = [
                            CTParagraphStyleSetting(spec: .headIndent, valueSize: size, value: h),
                            CTParagraphStyleSetting(spec: .firstLineHeadIndent, valueSize: size, value: f),
                            CTParagraphStyleSetting(spec: .paragraphSpacing, valueSize: size, value: a),
                            CTParagraphStyleSetting(spec: .paragraphSpacingBefore, valueSize: size, value: b),
                        ]
                        return CTParagraphStyleCreate(settings, settings.count)
                    }
                }
            }
        }
    }

    private static func append(_ output: NSMutableAttributedString, _ text: String, font: CTFont, style: CTParagraphStyle, link: URL? = nil) {
        var attributes: [NSAttributedString.Key: Any] = [
            NSAttributedString.Key(kCTFontAttributeName as String): font,
            NSAttributedString.Key(kCTParagraphStyleAttributeName as String): style,
        ]
        if let link {
            attributes[NSAttributedString.Key(kCTForegroundColorAttributeName as String)] = CGColor(red: 0.12, green: 0.31, blue: 0.47, alpha: 1)
            attributes[NSAttributedString.Key(kCTUnderlineStyleAttributeName as String)] = CTUnderlineStyle.single.rawValue
            attributes[NSAttributedString.Key("SermonStudioLink")] = link
        }
        output.append(NSAttributedString(string: text, attributes: attributes))
    }

    /// Builds the attributed document: title, optional subtitle, then either formatted outline Markdown or plain paragraphs.
    public static func attributedDocument(title: String, subtitle: String?, body: String, formatted: Bool, options: Options = Options()) -> NSAttributedString {
        let output = NSMutableAttributedString()
        let base = options.baseSize
        append(output, title + "\n", font: font(base * 1.7, bold: true), style: paragraphStyle(indent: 0, spacingAfter: 6))
        if let subtitle, !subtitle.isEmpty { append(output, subtitle + "\n", font: font(base, italic: true), style: paragraphStyle(indent: 0, spacingAfter: 14)) }
        for raw in OutlineMarkdown.splitLines(body) {
            if !formatted {
                for part in ScriptureReferences.linkSegments(raw) { append(output, part.text, font: font(base), style: paragraphStyle(indent: 0, spacingAfter: 6), link: part.url) }
                append(output, "\n", font: font(base), style: paragraphStyle(indent: 0, spacingAfter: 6))
                continue
            }
            let line = OutlineMarkdown.parseLine(raw)
            let heading = line.block == .heading, quote = line.block == .quote
            let size = heading ? (line.level <= 2 ? base * 1.4 : base * 1.2) : base
            let indented = [.quote, .bullet, .numbered].contains(line.block)
            let style = paragraphStyle(indent: indented ? 18 : 0, spacingAfter: heading ? 4 : 5, spacingBefore: heading ? 10 : 0)
            if line.block == .bullet { append(output, "• ", font: font(size), style: style) }
            if line.block == .numbered { append(output, line.marker, font: font(size), style: style) }
            for run in line.runs {
                for part in ScriptureReferences.linkSegments(run.text) {
                    append(output, part.text, font: font(size, bold: heading || run.bold, italic: quote || run.italic), style: style, link: part.url)
                }
            }
            append(output, "\n", font: font(size), style: style)
        }
        return output
    }

    /// Writes a paginated PDF and returns the number of pages. Scripture references become clickable links.
    @discardableResult
    public static func write(_ document: NSAttributedString, to url: URL, title: String, options: Options = Options()) throws -> Int {
        var mediaBox = CGRect(origin: .zero, size: options.pageSize)
        let info = [kCGPDFContextTitle as String: title, kCGPDFContextCreator as String: "Sermon Studio"] as CFDictionary
        guard let consumer = CGDataConsumer(url: url as CFURL), let context = CGContext(consumer: consumer, mediaBox: &mediaBox, info) else {
            throw CocoaError(.fileWriteUnknown, userInfo: [NSFilePathErrorKey: url.path])
        }
        let framesetter = CTFramesetterCreateWithAttributedString(document as CFAttributedString)
        let textRect = mediaBox.insetBy(dx: options.margin, dy: options.margin)
        let total = document.length
        var location = 0
        var pages = 0
        repeat {
            context.beginPDFPage(nil)
            let path = CGPath(rect: textRect, transform: nil)
            let frame = CTFramesetterCreateFrame(framesetter, CFRange(location: location, length: 0), path, nil)
            CTFrameDraw(frame, context)
            addLinks(frame, in: context, document: document, origin: textRect.origin)
            let visible = CTFrameGetVisibleStringRange(frame)
            context.endPDFPage()
            pages += 1
            // A zero-length frame would loop forever; it only happens if a single line is taller than a page.
            if visible.length <= 0 { break }
            location += visible.length
        } while location < total
        context.closePDF()
        return pages
    }

    private static func addLinks(_ frame: CTFrame, in context: CGContext, document: NSAttributedString, origin: CGPoint) {
        guard let lines = CTFrameGetLines(frame) as? [CTLine], !lines.isEmpty else { return }
        var origins = Array(repeating: CGPoint.zero, count: lines.count)
        CTFrameGetLineOrigins(frame, CFRange(location: 0, length: 0), &origins)
        for (index, line) in lines.enumerated() {
            guard let runs = CTLineGetGlyphRuns(line) as? [CTRun] else { continue }
            for run in runs {
                let range = CTRunGetStringRange(run)
                guard range.location < document.length,
                      let url = document.attribute(NSAttributedString.Key("SermonStudioLink"), at: range.location, effectiveRange: nil) as? URL else { continue }
                var ascent: CGFloat = 0, descent: CGFloat = 0
                let width = CGFloat(CTRunGetTypographicBounds(run, CFRange(location: 0, length: 0), &ascent, &descent, nil))
                let x = CTLineGetOffsetForStringIndex(line, range.location, nil)
                let rect = CGRect(x: origin.x + origins[index].x + x, y: origin.y + origins[index].y - descent, width: width, height: ascent + descent)
                context.setURL(url as CFURL, for: rect)
            }
        }
    }
}
#endif
