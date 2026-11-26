import Foundation

/// SRT and WebVTT reading/writing, compatible with whisper.cpp (Windows) and WhisperKit (macOS) output.
public enum Subtitles {
    private static let timing = try! NSRegularExpression(pattern: "^\\s*((?:\\d+:)?\\d{1,2}:\\d{1,2}[,.]\\d{1,3})\\s*-->\\s*((?:\\d+:)?\\d{1,2}:\\d{1,2}[,.]\\d{1,3})")

    private static func timingMatch(_ line: String) -> (Double, Double)? {
        let ns = line as NSString
        guard let match = timing.firstMatch(in: line, range: NSRange(location: 0, length: ns.length)),
              let start = parseTime(ns.substring(with: match.range(at: 1))),
              let end = parseTime(ns.substring(with: match.range(at: 2))) else { return nil }
        return (start, end)
    }

    /// Parses SRT or WebVTT. Malformed cues are skipped rather than failing the whole file.
    public static func parse(_ text: String) -> [SermonSegment] {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return [] }
        let lines = OutlineMarkdown.splitLines(text.hasPrefix("\u{FEFF}") ? String(text.dropFirst()) : text)
        var result: [SermonSegment] = []
        var i = 0
        while i < lines.count {
            guard let cue = timingMatch(lines[i]) else { i += 1; continue }
            let (start, end) = cue
            var body: [String] = []
            var j = i + 1
            while j < lines.count, !lines[j].trimmingCharacters(in: .whitespaces).isEmpty, timingMatch(lines[j]) == nil {
                body.append(lines[j].trimmingCharacters(in: .whitespaces))
                j += 1
            }
            result.append(SermonSegment(start: start, end: max(start, end), text: body.joined(separator: "\n")))
            i = j
        }
        return result
    }

    public static func parseTime(_ value: String) -> Double? {
        let parts = value.replacingOccurrences(of: ",", with: ".").split(separator: ":").map(String.init)
        guard parts.count == 2 || parts.count == 3 else { return nil }
        let hours = parts.count == 3 ? Int(parts[0]) : 0
        guard let h = hours, let m = Int(parts[parts.count - 2]), m < 60, let s = Double(parts[parts.count - 1]), s < 60, s >= 0 else { return nil }
        return Double(h * 3600 + m * 60) + s
    }

    public static func formatTime(_ seconds: Double, vtt: Bool) -> String {
        let value = seconds.isFinite && seconds > 0 ? seconds : 0
        let ms = Int64((value * 1000).rounded())
        return String(format: "%02lld:%02lld:%02lld%@%03lld", ms / 3_600_000, ms / 60_000 % 60, ms / 1000 % 60, vtt ? "." : ",", ms % 1000)
    }

    public static func make(_ segments: [SermonSegment], vtt: Bool) -> String {
        var output = vtt ? "WEBVTT\n\n" : ""
        for (index, segment) in segments.enumerated() {
            // A blank line inside a cue would end it early, so collapse internal blank lines.
            let text = OutlineMarkdown.splitLines(segment.text).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }.joined(separator: "\n")
            if index > 0 { output += "\n" }
            if !vtt { output += "\(index + 1)\n" }
            output += formatTime(segment.start, vtt: vtt) + " --> " + formatTime(max(segment.start, segment.end), vtt: vtt) + "\n" + text + "\n"
        }
        return output
    }
}
