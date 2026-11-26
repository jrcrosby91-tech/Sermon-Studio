import Foundation

public struct ScriptureReference: Equatable, Hashable, Sendable {
    public var book: String
    public var chapter: Int
    public var verse: Int?
    public var endChapter: Int?
    public var endVerse: Int?
    /// Position of the matched text in the searched string (UTF-16, NSRange-compatible).
    public var range: NSRange

    public var canonical: String {
        var text = "\(book) \(chapter)"
        if let verse {
            text += ":\(verse)"
            if let endChapter, endChapter != chapter { text += "-\(endChapter):\(endVerse ?? 1)" }
            else if let endVerse, endVerse != verse { text += "-\(endVerse)" }
        }
        return text
    }
}

/// Detects Bible references. Links open the ESV website (no API key, no scraping); previews use the
/// public-domain King James Version and are always labelled as KJV.
public enum ScriptureReferences {
    public static let previewTranslation = "King James Version (public domain)"

    private struct Book { let name: String; let chapters: Int; let numbered: Bool; let aliases: [String] }

    private static let books: [Book] = [
        Book(name: "Genesis", chapters: 50, numbered: false, aliases: ["Genesis", "Gen"]),
        Book(name: "Exodus", chapters: 40, numbered: false, aliases: ["Exodus", "Exod", "Ex"]),
        Book(name: "Leviticus", chapters: 27, numbered: false, aliases: ["Leviticus", "Lev"]),
        Book(name: "Numbers", chapters: 36, numbered: false, aliases: ["Numbers", "Num"]),
        Book(name: "Deuteronomy", chapters: 34, numbered: false, aliases: ["Deuteronomy", "Deut", "Dt"]),
        Book(name: "Joshua", chapters: 24, numbered: false, aliases: ["Joshua", "Josh"]),
        Book(name: "Judges", chapters: 21, numbered: false, aliases: ["Judges", "Judg"]),
        Book(name: "Ruth", chapters: 4, numbered: false, aliases: ["Ruth"]),
        Book(name: "Samuel", chapters: 31, numbered: true, aliases: ["Samuel", "Sam"]),
        Book(name: "Kings", chapters: 25, numbered: true, aliases: ["Kings", "Kgs"]),
        Book(name: "Chronicles", chapters: 36, numbered: true, aliases: ["Chronicles", "Chron", "Chr"]),
        Book(name: "Ezra", chapters: 10, numbered: false, aliases: ["Ezra"]),
        Book(name: "Nehemiah", chapters: 13, numbered: false, aliases: ["Nehemiah", "Neh"]),
        Book(name: "Esther", chapters: 10, numbered: false, aliases: ["Esther", "Esth"]),
        Book(name: "Job", chapters: 42, numbered: false, aliases: ["Job"]),
        Book(name: "Psalm", chapters: 150, numbered: false, aliases: ["Psalms", "Psalm", "Pss", "Psa", "Ps"]),
        Book(name: "Proverbs", chapters: 31, numbered: false, aliases: ["Proverbs", "Prov"]),
        Book(name: "Ecclesiastes", chapters: 12, numbered: false, aliases: ["Ecclesiastes", "Eccles", "Eccl"]),
        Book(name: "Song of Solomon", chapters: 8, numbered: false, aliases: ["Song of Solomon", "Song of Songs"]),
        Book(name: "Isaiah", chapters: 66, numbered: false, aliases: ["Isaiah", "Isa"]),
        Book(name: "Jeremiah", chapters: 52, numbered: false, aliases: ["Jeremiah", "Jer"]),
        Book(name: "Lamentations", chapters: 5, numbered: false, aliases: ["Lamentations", "Lam"]),
        Book(name: "Ezekiel", chapters: 48, numbered: false, aliases: ["Ezekiel", "Ezek"]),
        Book(name: "Daniel", chapters: 12, numbered: false, aliases: ["Daniel"]),
        Book(name: "Hosea", chapters: 14, numbered: false, aliases: ["Hosea", "Hos"]),
        Book(name: "Joel", chapters: 3, numbered: false, aliases: ["Joel"]),
        Book(name: "Amos", chapters: 9, numbered: false, aliases: ["Amos"]),
        Book(name: "Obadiah", chapters: 1, numbered: false, aliases: ["Obadiah", "Obad"]),
        Book(name: "Jonah", chapters: 4, numbered: false, aliases: ["Jonah"]),
        Book(name: "Micah", chapters: 7, numbered: false, aliases: ["Micah", "Mic"]),
        Book(name: "Nahum", chapters: 3, numbered: false, aliases: ["Nahum", "Nah"]),
        Book(name: "Habakkuk", chapters: 3, numbered: false, aliases: ["Habakkuk", "Hab"]),
        Book(name: "Zephaniah", chapters: 3, numbered: false, aliases: ["Zephaniah", "Zeph"]),
        Book(name: "Haggai", chapters: 2, numbered: false, aliases: ["Haggai", "Hag"]),
        Book(name: "Zechariah", chapters: 14, numbered: false, aliases: ["Zechariah", "Zech"]),
        Book(name: "Malachi", chapters: 4, numbered: false, aliases: ["Malachi", "Mal"]),
        Book(name: "Matthew", chapters: 28, numbered: false, aliases: ["Matthew", "Matt", "Mt"]),
        Book(name: "Mark", chapters: 16, numbered: false, aliases: ["Mark", "Mk"]),
        Book(name: "Luke", chapters: 24, numbered: false, aliases: ["Luke", "Lk"]),
        Book(name: "John", chapters: 21, numbered: false, aliases: ["John", "Jn"]),
        Book(name: "Acts", chapters: 28, numbered: false, aliases: ["Acts"]),
        Book(name: "Romans", chapters: 16, numbered: false, aliases: ["Romans", "Rom"]),
        Book(name: "Corinthians", chapters: 16, numbered: true, aliases: ["Corinthians", "Cor"]),
        Book(name: "Galatians", chapters: 6, numbered: false, aliases: ["Galatians", "Gal"]),
        Book(name: "Ephesians", chapters: 6, numbered: false, aliases: ["Ephesians", "Eph"]),
        Book(name: "Philippians", chapters: 4, numbered: false, aliases: ["Philippians", "Phil", "Php"]),
        Book(name: "Colossians", chapters: 4, numbered: false, aliases: ["Colossians", "Col"]),
        Book(name: "Thessalonians", chapters: 5, numbered: true, aliases: ["Thessalonians", "Thess"]),
        Book(name: "Timothy", chapters: 6, numbered: true, aliases: ["Timothy", "Tim"]),
        Book(name: "Titus", chapters: 3, numbered: false, aliases: ["Titus"]),
        Book(name: "Philemon", chapters: 1, numbered: false, aliases: ["Philemon", "Phlm"]),
        Book(name: "Hebrews", chapters: 13, numbered: false, aliases: ["Hebrews", "Heb"]),
        Book(name: "James", chapters: 5, numbered: false, aliases: ["James", "Jas"]),
        Book(name: "Peter", chapters: 5, numbered: true, aliases: ["Peter", "Pet"]),
        Book(name: "Jude", chapters: 1, numbered: false, aliases: ["Jude"]),
        Book(name: "Revelation", chapters: 22, numbered: false, aliases: ["Revelation", "Revelations", "Rev"]),
    ]

    private static let numberedChapters: [String: Int] = [
        "1 Samuel": 31, "2 Samuel": 24, "1 Kings": 22, "2 Kings": 25, "1 Chronicles": 29, "2 Chronicles": 36,
        "1 Corinthians": 16, "2 Corinthians": 13, "1 Thessalonians": 5, "2 Thessalonians": 3,
        "1 Timothy": 6, "2 Timothy": 4, "1 Peter": 5, "2 Peter": 3, "1 John": 5, "2 John": 1, "3 John": 1,
    ]

    private static let byAlias: [String: Book] = {
        var map: [String: Book] = [:]
        for book in books { for alias in book.aliases { map[alias.lowercased()] = book } }
        return map
    }()

    private static let pattern: NSRegularExpression = {
        let names = books.flatMap(\.aliases).sorted { $0.count > $1.count }
            .map { NSRegularExpression.escapedPattern(for: $0).replacingOccurrences(of: "\\ ", with: " ").replacingOccurrences(of: " ", with: "\\s+") }
            .joined(separator: "|")
        // Roman numerals are case-sensitive so the pronoun "I" only counts directly before a numbered book.
        let source = "(?<![\\p{L}\\p{N}])(?:(?<num>[123])\\s*|(?<rom>III|II|I)\\s+|(?<word>(?i:first|second|third|1st|2nd|3rd))\\s+)?"
            + "(?<book>(?i:" + names + "))\\.?\\s*(?<ch>\\d{1,3})"
            + "(?:(?::\\s?|\\.)(?<v1>\\d{1,3})(?:\\s*[-–—]\\s*(?:(?<ch2>\\d{1,3})\\s*:\\s*)?(?<v2>\\d{1,3}))?)?(?![\\p{L}\\p{N}])"
        return try! NSRegularExpression(pattern: source, options: [])
    }()

    public static func find(_ text: String, requireVerse: Bool = false) -> [ScriptureReference] {
        let ns = text as NSString
        var results: [ScriptureReference] = []
        for match in pattern.matches(in: text, range: NSRange(location: 0, length: ns.length)) {
            func group(_ name: String) -> String? {
                let range = match.range(withName: name)
                return range.location == NSNotFound ? nil : ns.substring(with: range)
            }
            guard let rawBook = group("book") else { continue }
            let normalizedBook = rawBook.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ").lowercased()
            guard let book = byAlias[normalizedBook] else { continue }
            var number = 0
            if let digit = group("num") { number = Int(digit) ?? 0 }
            else if let roman = group("rom") { number = roman.count }
            else if let word = group("word")?.lowercased() { number = ["first", "1st"].contains(word) ? 1 : ["second", "2nd"].contains(word) ? 2 : 3 }
            var range = match.range
            let name: String
            let chapters: Int
            if book.numbered || (number > 0 && book.name == "John") {
                guard number > 0 else { continue } // "Corinthians 13" without 1/2 is ambiguous.
                name = "\(number) \(book.name)"
                guard let count = numberedChapters[name] else { continue }
                chapters = count
            } else {
                if number > 0 {
                    let bookRange = match.range(withName: "book")
                    range = NSRange(location: bookRange.location, length: match.range.location + match.range.length - bookRange.location)
                }
                name = book.name
                chapters = book.chapters
            }
            guard var chapter = group("ch").flatMap({ Int($0) }) else { continue }
            var verse = group("v1").flatMap { Int($0) }
            var endChapter = group("ch2").flatMap { Int($0) }
            var endVerse = group("v2").flatMap { Int($0) }
            if chapters == 1, verse == nil, chapter > 1 { verse = chapter; chapter = 1 } // "Jude 5" = verse 5.
            guard chapter >= 1, chapter <= chapters else { continue }
            if let v = verse, v < 1 || v > 176 { continue }
            if let ec = endChapter, ec <= chapter || ec > chapters { endChapter = nil; endVerse = nil }
            if endChapter == nil, let ev = endVerse, let v = verse, ev < v { endVerse = nil }
            if requireVerse && verse == nil { continue }
            results.append(ScriptureReference(book: name, chapter: chapter, verse: verse, endChapter: endChapter, endVerse: endVerse, range: range))
        }
        return results
    }

    public static func distinct(_ text: String, requireVerse: Bool = false) -> [String] {
        var seen = Set<String>()
        return find(text, requireVerse: requireVerse).map(\.canonical).filter { seen.insert($0.lowercased()).inserted }
    }

    /// The ESV website passage page (as in earlier releases). Opening it needs no API key.
    public static func link(_ reference: ScriptureReference) -> URL {
        let path = reference.canonical.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? ""
        return URL(string: "https://www.esv.org/" + path + "/") ?? URL(string: "https://www.esv.org/")!
    }

    /// Public-domain KJV preview endpoint. Only the reference is sent, never sermon text.
    public static func previewURL(_ reference: ScriptureReference) -> URL? {
        guard let path = reference.canonical.addingPercentEncoding(withAllowedCharacters: .alphanumerics) else { return nil }
        return URL(string: "https://bible-api.com/" + path + "?translation=kjv")
    }

    /// Splits text into plain and reference segments, for hyperlinking in exports and the reading view.
    public static func linkSegments(_ text: String) -> [(text: String, url: URL?)] {
        let ns = text as NSString
        var output: [(text: String, url: URL?)] = []
        var cursor = 0
        for reference in find(text, requireVerse: false) where reference.range.location >= cursor {
            if reference.range.location > cursor { output.append((ns.substring(with: NSRange(location: cursor, length: reference.range.location - cursor)), nil)) }
            output.append((ns.substring(with: reference.range), link(reference)))
            cursor = reference.range.location + reference.range.length
        }
        if cursor < ns.length { output.append((ns.substring(from: cursor), nil)) }
        return output
    }
}
