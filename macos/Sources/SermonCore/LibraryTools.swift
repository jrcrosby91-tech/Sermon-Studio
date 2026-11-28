import Foundation

public enum LibrarySort: String, CaseIterable, Identifiable, Sendable {
    case newest = "Newest", oldest = "Oldest", title = "Title", recentlyEdited = "Recently edited"
    public var id: String { rawValue }
}

public struct LibraryFilter: Equatable, Sendable {
    public var search = ""
    public var favoritesOnly = false
    public var speaker = ""
    public var tag = ""
    public var series = ""
    public var collection = ""
    public var from: Date?
    public var to: Date?
    public init() {}
}

public struct LibraryStats: Equatable, Sendable {
    public var sermons = 0, favorites = 0, outlines = 0, series = 0, words = 0
}

public enum LibraryQuery {
    public static func apply(_ records: [SermonRecord], filter: LibraryFilter, sort: LibrarySort) -> [SermonRecord] {
        let terms = filter.search.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        func contains(_ value: String, _ field: String) -> Bool {
            let needle = field.trimmingCharacters(in: .whitespaces)
            return needle.isEmpty || value.localizedCaseInsensitiveContains(needle)
        }
        let calendar = Calendar(identifier: .gregorian)
        let filtered = records.filter { record in
            if filter.favoritesOnly && !record.favorite { return false }
            let fields = [record.title, record.date, record.speaker, record.primaryScripture, record.tags, record.series,
                          record.collection, record.original, record.cleaned, record.outline]
            guard terms.allSatisfy({ term in fields.contains { $0.localizedCaseInsensitiveContains(term) } }) else { return false }
            guard contains(record.speaker, filter.speaker), contains(record.series, filter.series), contains(record.collection, filter.collection) else { return false }
            if !filter.tag.trimmingCharacters(in: .whitespaces).isEmpty,
               !record.tagList.contains(where: { $0.localizedCaseInsensitiveContains(filter.tag.trimmingCharacters(in: .whitespaces)) }) { return false }
            if filter.from != nil || filter.to != nil {
                guard let day = ArchiveDates.dayDate(record.date) else { return false }
                if let from = filter.from, day < calendar.startOfDay(for: from) { return false }
                if let to = filter.to, let end = calendar.date(byAdding: .day, value: 1, to: calendar.startOfDay(for: to)), day >= end { return false }
            }
            return true
        }
        switch sort {
        case .newest: return filtered.sorted { $0.date > $1.date }
        case .oldest: return filtered.sorted { $0.date < $1.date }
        case .title: return filtered.sorted { $0.displayTitle.localizedStandardCompare($1.displayTitle) == .orderedAscending }
        case .recentlyEdited: return filtered.sorted { ($0.lastEdited ?? .distantPast) > ($1.lastEdited ?? .distantPast) }
        }
    }

    public static func stats(_ records: [SermonRecord]) -> LibraryStats {
        var stats = LibraryStats()
        stats.sermons = records.count
        stats.favorites = records.filter(\.favorite).count
        stats.outlines = records.filter { !$0.outline.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }.count
        stats.series = Set(records.map { $0.series.trimmingCharacters(in: .whitespaces).lowercased() }.filter { !$0.isEmpty }).count
        stats.words = records.reduce(0) { $0 + TranscriptTools.wordCount($1.original) }
        return stats
    }

    public static func recent(_ records: [SermonRecord], limit: Int = 8) -> [SermonRecord] {
        Array(records.filter { $0.lastOpened != nil }.sorted { $0.lastOpened! > $1.lastOpened! }.prefix(limit))
    }

    /// Distinct non-empty values for filter suggestions.
    public static func values(_ records: [SermonRecord], _ field: KeyPath<SermonRecord, String>) -> [String] {
        var seen = Set<String>()
        return records.map { $0[keyPath: field].trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty && seen.insert($0.lowercased()).inserted }
            .sorted { $0.localizedStandardCompare($1) == .orderedAscending }
    }
}

public enum TranscriptTools {
    /// Removes filler words. The original transcript is never modified; the result is stored as the cleaned version.
    public static func clean(_ text: String) -> String {
        var result = text.replacingOccurrences(of: "\\b(um+|uh+|erm)\\b[ ,]*", with: "", options: [.regularExpression, .caseInsensitive])
        result = result.replacingOccurrences(of: "[ \\t]{2,}", with: " ", options: .regularExpression)
        result = result.replacingOccurrences(of: " +([,.;:!?])", with: "$1", options: .regularExpression)
        return result.trimmingCharacters(in: .whitespaces)
    }

    public static func wordCount(_ text: String) -> Int {
        text.split(whereSeparator: { $0.isWhitespace || $0.isNewline }).filter { $0.contains(where: { $0.isLetter || $0.isNumber }) }.count
    }

    /// UTF-16 ranges of every match (NSTextView-compatible).
    public static func matches(in text: String, of query: String, matchCase: Bool) -> [NSRange] {
        guard !query.isEmpty else { return [] }
        let ns = text as NSString
        var ranges: [NSRange] = []
        var search = NSRange(location: 0, length: ns.length)
        let options: NSString.CompareOptions = matchCase ? [] : [.caseInsensitive]
        while search.length > 0 {
            let found = ns.range(of: query, options: options, range: search)
            if found.location == NSNotFound || found.length == 0 { break }
            ranges.append(found)
            let next = found.location + found.length
            search = NSRange(location: next, length: ns.length - next)
        }
        return ranges
    }

    /// Index of the match after (or before) the given location, wrapping around.
    public static func nextMatch(_ ranges: [NSRange], after location: Int, backwards: Bool) -> Int? {
        guard !ranges.isEmpty else { return nil }
        if backwards { return ranges.lastIndex { $0.location < location } ?? ranges.count - 1 }
        return ranges.firstIndex { $0.location >= location } ?? 0
    }

    public static func replace(in text: String, range: NSRange, with replacement: String) -> String {
        let ns = text as NSString
        guard range.location != NSNotFound, range.location + range.length <= ns.length else { return text }
        return ns.replacingCharacters(in: range, with: replacement)
    }

    public static func replaceAll(in text: String, of query: String, with replacement: String, matchCase: Bool) -> (text: String, count: Int) {
        let ranges = matches(in: text, of: query, matchCase: matchCase)
        guard !ranges.isEmpty else { return (text, 0) }
        let result = NSMutableString(string: text)
        for range in ranges.reversed() { result.replaceCharacters(in: range, with: replacement) }
        return (result as String, ranges.count)
    }

    /// Finds an outline phrase in the transcript, ignoring case, punctuation spacing and Markdown markers.
    public static func locate(_ phrase: String, in text: String) -> NSRange? {
        let cleaned = phrase.replacingOccurrences(of: "[*>#]", with: "", options: .regularExpression)
            .split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        guard cleaned.count >= 3 else { return nil }
        let words = cleaned.split(separator: " ").map { NSRegularExpression.escapedPattern(for: String($0)) }
        guard let regex = try? NSRegularExpression(pattern: words.joined(separator: "\\s+"), options: [.caseInsensitive]) else { return nil }
        let match = regex.firstMatch(in: text, range: NSRange(location: 0, length: (text as NSString).length))
        return match?.range
    }

    /// The segment active at a playback time.
    public static func activeSegmentIndex(_ segments: [SermonSegment], at time: Double) -> Int? {
        guard !segments.isEmpty, time.isFinite else { return nil }
        var low = 0, high = segments.count - 1, found: Int?
        while low <= high {
            let mid = (low + high) / 2
            if segments[mid].start <= time { found = mid; low = mid + 1 } else { high = mid - 1 }
        }
        guard let index = found else { return nil }
        return time <= max(segments[index].end, segments[index].start) + 0.75 ? index : nil
    }

    public static func timestamp(_ seconds: Double) -> String {
        let total = Int(max(0, seconds.isFinite ? seconds : 0))
        return total >= 3600 ? String(format: "%d:%02d:%02d", total / 3600, total / 60 % 60, total % 60) : String(format: "%02d:%02d", total / 60, total % 60)
    }
}
