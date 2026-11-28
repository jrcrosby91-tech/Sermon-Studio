import Foundation

public struct OutlineRun: Equatable, Sendable {
    public var text: String
    public var bold: Bool
    public var italic: Bool
    public init(_ text: String, bold: Bool = false, italic: Bool = false) { self.text = text; self.bold = bold; self.italic = italic }
}

public enum OutlineBlock: Equatable, Sendable { case paragraph, heading, quote, bullet, numbered }

public struct OutlineLine: Equatable, Sendable {
    public var block: OutlineBlock
    public var level: Int
    public var marker: String
    public var runs: [OutlineRun]
    public var plainText: String { runs.map(\.text).joined() }
}

/// A formatted span in the visual editor. Offsets are UTF-16 (NSAttributedString/NSTextView ranges).
public struct VisualSpan: Equatable, Sendable {
    public var start: Int
    public var end: Int
    public var bold: Bool
    public var italic: Bool
    public var size: Double
}

public struct VisualDocument: Equatable, Sendable {
    public var text: String
    public var spans: [VisualSpan]
}

/// The outline formatting model shared by the visual editor, reading view and exports, and identical in
/// behaviour to the Windows app: "## " heading, "### " subheading, "> " scripture quotation, "- "/"* " bullets,
/// numbered lines, **bold**, *italic*, ***bold italic***, and "\*" for a literal asterisk.
public enum OutlineMarkdown {
    public static let visualQuote = "❯ "
    public static let visualBullet = "• "
    public static func headingSize(_ base: Double) -> Double { base * 23 / 17 }
    public static func subheadingSize(_ base: Double) -> Double { base * 19 / 17 }

    public static func splitLines(_ text: String) -> [String] {
        text.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
            .components(separatedBy: "\n")
    }

    public static func normalizeNewlines(_ text: String, _ newline: String = "\n") -> String {
        splitLines(text).joined(separator: newline)
    }

    // MARK: Inline formatting

    public static func parseInline(_ text: String) -> [OutlineRun] {
        let c = Array(text)
        var runs: [OutlineRun] = []
        var plain = ""
        func flush() { if !plain.isEmpty { runs.append(OutlineRun(plain)); plain = "" } }
        var i = 0
        while i < c.count {
            let ch = c[i]
            if ch == "\\", i + 1 < c.count, c[i + 1] == "*" { plain.append("*"); i += 2; continue }
            if ch == "*" {
                var n = 0
                while i + n < c.count, c[i + n] == "*" { n += 1 }
                if n <= 3, i + n < c.count, !c[i + n].isWhitespace {
                    let close = findClose(c, i + n, n)
                    if close > 0 {
                        flush()
                        runs.append(OutlineRun(String(c[(i + n)..<close]).replacingOccurrences(of: "\\*", with: "*"), bold: n >= 2, italic: n != 2))
                        i = close + n
                        continue
                    }
                }
                plain.append(String(repeating: "*", count: n))
                i += n
                continue
            }
            plain.append(ch)
            i += 1
        }
        flush()
        return merge(runs)
    }

    private static func findClose(_ c: [Character], _ start: Int, _ count: Int) -> Int {
        var j = start
        while j < c.count {
            if c[j] == "\\", j + 1 < c.count, c[j + 1] == "*" { j += 2; continue }
            if c[j] != "*" { j += 1; continue }
            var n = 0
            while j + n < c.count, c[j + n] == "*" { n += 1 }
            return n == count && j > start && !c[j - 1].isWhitespace ? j : -1
        }
        return -1
    }

    public static func merge(_ runs: [OutlineRun]) -> [OutlineRun] {
        var result: [OutlineRun] = []
        for run in runs where !run.text.isEmpty {
            if let last = result.last, last.bold == run.bold, last.italic == run.italic {
                result[result.count - 1].text += run.text
            } else { result.append(run) }
        }
        return result
    }

    /// Writes runs as Markdown, escaping literal asterisks only when needed for an exact round trip.
    public static func serializeInline(_ runs: [OutlineRun]) -> String {
        let merged = merge(runs)
        let simple = write(merged, escapePlain: false)
        return parseInline(simple) == merged ? simple : write(merged, escapePlain: true)
    }

    private static func write(_ runs: [OutlineRun], escapePlain: Bool) -> String {
        var output = ""
        for run in runs {
            if !run.bold && !run.italic { output += escapePlain ? run.text.replacingOccurrences(of: "*", with: "\\*") : run.text; continue }
            let lead = run.text.prefix(while: { $0.isWhitespace }).count
            let trail = run.text.reversed().prefix(while: { $0.isWhitespace }).count
            guard lead + trail < run.text.count else { output += run.text; continue }
            let core = String(run.text.dropFirst(lead).dropLast(trail))
            let marker = run.bold && run.italic ? "***" : run.bold ? "**" : "*"
            output += String(run.text.prefix(lead)) + marker + core.replacingOccurrences(of: "*", with: "\\*") + marker + String(run.text.suffix(trail))
        }
        return output
    }

    // MARK: Block structure (exports and reading view)

    public static func parseLine(_ raw: String) -> OutlineLine {
        let line = String(raw.reversed().drop(while: { $0.isWhitespace }).reversed())
        let body = String(line.drop(while: { $0.isWhitespace }))
        func match(_ pattern: String) -> String? {
            guard let range = body.range(of: pattern, options: .regularExpression), range.lowerBound == body.startIndex else { return nil }
            return String(body[range])
        }
        if let marker = match("^#{1,6}[ \t]+") {
            let level = marker.filter { $0 == "#" }.count
            return OutlineLine(block: .heading, level: level, marker: marker, runs: parseInline(String(body.dropFirst(marker.count))))
        }
        if let marker = match("^>[ \t]?") { return OutlineLine(block: .quote, level: 0, marker: marker, runs: parseInline(String(body.dropFirst(marker.count)))) }
        if let marker = match("^[-*+][ \t]+") { return OutlineLine(block: .bullet, level: 0, marker: marker, runs: parseInline(String(body.dropFirst(marker.count)))) }
        if let marker = match("^[0-9]{1,3}[.)][ \t]+") {
            return OutlineLine(block: .numbered, level: 0, marker: marker.trimmingCharacters(in: .whitespaces) + " ", runs: parseInline(String(body.dropFirst(marker.count))))
        }
        return OutlineLine(block: .paragraph, level: 0, marker: "", runs: parseInline(body))
    }

    public static func parseDocument(_ markdown: String) -> [OutlineLine] { splitLines(markdown).map(parseLine) }

    // MARK: Visual editor mapping

    /// Markdown to the plain text and character formatting shown in the visual editor (lines separated by "\n").
    public static func toVisual(_ markdown: String, baseSize: Double) -> VisualDocument {
        var text = ""
        var offset = 0
        var spans: [VisualSpan] = []
        for (index, line) in splitLines(markdown).enumerated() {
            if index > 0 { text += "\n"; offset += 1 }
            var size = baseSize
            let heading = line.hasPrefix("## "), sub = line.hasPrefix("### ")
            var quote = false
            var prefix = "", body = line
            if heading { size = headingSize(baseSize); body = String(line.dropFirst(3)) }
            else if sub { size = subheadingSize(baseSize); body = String(line.dropFirst(4)) }
            else if line.hasPrefix("> ") { quote = true; prefix = visualQuote; body = String(line.dropFirst(2)) }
            else if line.hasPrefix("- ") || line.hasPrefix("* ") { prefix = visualBullet; body = String(line.dropFirst(2)) }
            let strong = heading || sub
            if !prefix.isEmpty {
                let length = prefix.utf16.count
                spans.append(VisualSpan(start: offset, end: offset + length, bold: strong, italic: quote, size: size))
                text += prefix; offset += length
            }
            for run in parseInline(body) {
                let length = run.text.utf16.count
                spans.append(VisualSpan(start: offset, end: offset + length, bold: run.bold || strong, italic: run.italic || quote, size: size))
                text += run.text; offset += length
            }
        }
        return VisualDocument(text: text, spans: spans)
    }

    /// Visual-editor content back to Markdown. The closures read formatting at a UTF-16 offset in `plain`.
    public static func fromVisual(_ plain: String, baseSize: Double, sizeAt: (Int) -> Double, formatAt: (Int) -> (bold: Bool, italic: Bool)) -> String {
        var lines: [String] = []
        var starts: [Int] = []
        var current = ""
        var lineStart = 0, offset = 0
        for ch in plain {
            let width = ch.utf16.count
            if ch == "\n" || ch == "\r\n" || ch == "\r" || ch == "\u{2028}" || ch == "\u{2029}" {
                lines.append(current); starts.append(lineStart)
                current = ""; offset += width; lineStart = offset
            } else { current.append(ch); offset += width }
        }
        lines.append(current); starts.append(lineStart)
        return zip(lines, starts).map { line, start in
            lineFromVisual(line, firstSize: line.isEmpty ? baseSize : sizeAt(start), baseSize: baseSize) { formatAt(start + $0) }
        }.joined(separator: "\n")
    }

    /// One visual line to Markdown. `formatAt` takes a UTF-16 offset within the line.
    public static func lineFromVisual(_ line: String, firstSize: Double, baseSize: Double, formatAt: (Int) -> (bold: Bool, italic: Bool)) -> String {
        guard !line.isEmpty else { return "" }
        // Thresholds sit midway between normal, subheading and heading sizes, relative to the editor's base size.
        let heading = firstSize >= baseSize * 21 / 17
        let sub = !heading && firstSize >= baseSize * 18 / 17
        var prefix = heading ? "## " : sub ? "### " : ""
        var characters = Array(line)
        var offset = 0
        var quote = false
        if !heading && !sub {
            if line.hasPrefix(visualQuote) { prefix = "> "; quote = true }
            else if line.hasPrefix(visualBullet) { prefix = "- " }
            if !prefix.isEmpty {
                let marker = Array(prefix == "> " ? visualQuote : visualBullet)
                offset = String(marker).utf16.count
                characters.removeFirst(marker.count)
            }
        }
        var runs: [OutlineRun] = []
        for ch in characters {
            let format = formatAt(offset)
            let bold = !heading && !sub && format.bold
            let italic = !quote && format.italic
            if let last = runs.last, last.bold == bold, last.italic == italic { runs[runs.count - 1].text.append(ch) }
            else { runs.append(OutlineRun(String(ch), bold: bold, italic: italic)) }
            offset += ch.utf16.count
        }
        return prefix + serializeInline(runs)
    }

    /// Round-trips Markdown through the visual model exactly as the editor would store it unchanged.
    public static func roundTrip(_ markdown: String, baseSize: Double) -> String {
        let visual = toVisual(markdown, baseSize: baseSize)
        let length = visual.text.utf16.count
        var sizes = Array(repeating: baseSize, count: length)
        var formats = Array(repeating: (bold: false, italic: false), count: length)
        for span in visual.spans { for k in span.start..<span.end { sizes[k] = span.size; formats[k] = (span.bold, span.italic) } }
        return fromVisual(visual.text, baseSize: baseSize, sizeAt: { sizes[$0] }, formatAt: { formats[$0] })
    }

    /// Combines a visual-editor result with the Markdown it was loaded from: lines the user did not change keep
    /// their original spelling ("* " bullets, H1/H4 lines, escapes), so switching views never rewrites them.
    public static func mergeVisualEdit(original: String, updated: String, baseSize: Double) -> String {
        let a = splitLines(original), b = splitLines(updated)
        let keyA = a.map { roundTrip($0, baseSize: baseSize) }, keyB = b.map { roundTrip($0, baseSize: baseSize) }
        var output = b
        if a.count * b.count > 4_000_000 {
            for j in b.indices where j < a.count && keyA[j] == keyB[j] { output[j] = a[j] }
            return output.joined(separator: "\n")
        }
        var lcs = Array(repeating: Array(repeating: 0, count: b.count + 1), count: a.count + 1)
        for i in stride(from: a.count - 1, through: 0, by: -1) {
            for j in stride(from: b.count - 1, through: 0, by: -1) {
                lcs[i][j] = keyA[i] == keyB[j] ? lcs[i + 1][j + 1] + 1 : max(lcs[i + 1][j], lcs[i][j + 1])
            }
        }
        var x = 0, y = 0
        while y < b.count {
            if x < a.count && keyA[x] == keyB[y] { output[y] = a[x]; x += 1; y += 1 }
            else if x < a.count && lcs[x + 1][y] >= lcs[x][y + 1] { x += 1 }
            else { y += 1 }
        }
        return output.joined(separator: "\n")
    }
}
