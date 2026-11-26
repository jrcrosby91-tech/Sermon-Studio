using System.Globalization;
using System.Text.RegularExpressions;

namespace SermonStudio;

internal sealed record ScriptureReference(string Book, int Chapter, int? Verse, int? EndChapter, int? EndVerse, int Index, int Length)
{
    public string Canonical
    {
        get
        {
            var text = Book + " " + Chapter.ToString(CultureInfo.InvariantCulture);
            if (Verse is int v)
            {
                text += ":" + v.ToString(CultureInfo.InvariantCulture);
                if (EndChapter is int ec && ec != Chapter) text += "-" + ec.ToString(CultureInfo.InvariantCulture) + ":" + (EndVerse ?? 1).ToString(CultureInfo.InvariantCulture);
                else if (EndVerse is int ev && ev != v) text += "-" + ev.ToString(CultureInfo.InvariantCulture);
            }
            return text;
        }
    }
    public override string ToString() => Canonical;
}

/// <summary>
/// Detects Bible references in sermon text. Only public-domain (KJV) sources are linked; no copyrighted
/// translation text is bundled or scraped. A licensed ESV provider can later be plugged in through
/// <see cref="PassageLink"/> and <see cref="PreviewUrl"/> without changing detection.
/// </summary>
internal static partial class ScriptureReferences
{
    internal const string DefaultTranslation = "KJV";

    private sealed record Book(string Name, int Chapters, bool Numbered, string[] Aliases);

    private static readonly Book[] Books =
    {
        new("Genesis",50,false,new[]{"Genesis","Gen"}), new("Exodus",40,false,new[]{"Exodus","Exod","Ex"}),
        new("Leviticus",27,false,new[]{"Leviticus","Lev"}), new("Numbers",36,false,new[]{"Numbers","Num"}),
        new("Deuteronomy",34,false,new[]{"Deuteronomy","Deut","Dt"}), new("Joshua",24,false,new[]{"Joshua","Josh"}),
        new("Judges",21,false,new[]{"Judges","Judg"}), new("Ruth",4,false,new[]{"Ruth"}),
        new("Samuel",31,true,new[]{"Samuel","Sam"}), new("Kings",25,true,new[]{"Kings","Kgs"}),
        new("Chronicles",36,true,new[]{"Chronicles","Chron","Chr"}), new("Ezra",10,false,new[]{"Ezra"}),
        new("Nehemiah",13,false,new[]{"Nehemiah","Neh"}), new("Esther",10,false,new[]{"Esther","Esth"}),
        new("Job",42,false,new[]{"Job"}), new("Psalm",150,false,new[]{"Psalms","Psalm","Pss","Psa","Ps"}),
        new("Proverbs",31,false,new[]{"Proverbs","Prov"}), new("Ecclesiastes",12,false,new[]{"Ecclesiastes","Eccles","Eccl"}),
        new("Song of Solomon",8,false,new[]{"Song of Solomon","Song of Songs"}), new("Isaiah",66,false,new[]{"Isaiah","Isa"}),
        new("Jeremiah",52,false,new[]{"Jeremiah","Jer"}), new("Lamentations",5,false,new[]{"Lamentations","Lam"}),
        new("Ezekiel",48,false,new[]{"Ezekiel","Ezek"}), new("Daniel",12,false,new[]{"Daniel"}),
        new("Hosea",14,false,new[]{"Hosea","Hos"}), new("Joel",3,false,new[]{"Joel"}), new("Amos",9,false,new[]{"Amos"}),
        new("Obadiah",1,false,new[]{"Obadiah","Obad"}), new("Jonah",4,false,new[]{"Jonah"}), new("Micah",7,false,new[]{"Micah","Mic"}),
        new("Nahum",3,false,new[]{"Nahum","Nah"}), new("Habakkuk",3,false,new[]{"Habakkuk","Hab"}),
        new("Zephaniah",3,false,new[]{"Zephaniah","Zeph"}), new("Haggai",2,false,new[]{"Haggai","Hag"}),
        new("Zechariah",14,false,new[]{"Zechariah","Zech"}), new("Malachi",4,false,new[]{"Malachi","Mal"}),
        new("Matthew",28,false,new[]{"Matthew","Matt","Mt"}), new("Mark",16,false,new[]{"Mark","Mk"}),
        new("Luke",24,false,new[]{"Luke","Lk"}), new("John",21,false,new[]{"John","Jn"}), new("Acts",28,false,new[]{"Acts"}),
        new("Romans",16,false,new[]{"Romans","Rom"}), new("Corinthians",16,true,new[]{"Corinthians","Cor"}),
        new("Galatians",6,false,new[]{"Galatians","Gal"}), new("Ephesians",6,false,new[]{"Ephesians","Eph"}),
        new("Philippians",4,false,new[]{"Philippians","Phil","Php"}), new("Colossians",4,false,new[]{"Colossians","Col"}),
        new("Thessalonians",5,true,new[]{"Thessalonians","Thess"}), new("Timothy",6,true,new[]{"Timothy","Tim"}),
        new("Titus",3,false,new[]{"Titus"}), new("Philemon",1,false,new[]{"Philemon","Phlm"}),
        new("Hebrews",13,false,new[]{"Hebrews","Heb"}), new("James",5,false,new[]{"James","Jas"}),
        new("Peter",5,true,new[]{"Peter","Pet"}), new("Jude",1,false,new[]{"Jude"}),
        new("Revelation",22,false,new[]{"Revelation","Revelations","Rev"}),
    };

    // Chapter counts for numbered books, keyed by "n Name".
    private static readonly Dictionary<string, int> NumberedChapters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1 Samuel"]=31,["2 Samuel"]=24,["1 Kings"]=22,["2 Kings"]=25,["1 Chronicles"]=29,["2 Chronicles"]=36,
        ["1 Corinthians"]=16,["2 Corinthians"]=13,["1 Thessalonians"]=5,["2 Thessalonians"]=3,
        ["1 Timothy"]=6,["2 Timothy"]=4,["1 Peter"]=5,["2 Peter"]=3,["1 John"]=5,["2 John"]=1,["3 John"]=1,
    };

    private static readonly Dictionary<string, Book> ByAlias = Books
        .SelectMany(b => b.Aliases.Select(a => (Alias: a, Book: b)))
        .ToDictionary(x => x.Alias, x => x.Book, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex Pattern = BuildPattern();

    private static Regex BuildPattern()
    {
        var names = string.Join("|", ByAlias.Keys.OrderByDescending(x => x.Length).Select(x => Regex.Escape(x).Replace(@"\ ", @"\s+")));
        // Numeric prefixes are matched case-sensitively for roman numerals so the pronoun "I" in prose
        // is only treated as "1" when immediately followed by a numbered book name.
        var pattern = @"(?<![\p{L}\p{N}])(?:(?<num>[123])\s*|(?<rom>III|II|I)\s+|(?<word>(?i:first|second|third|1st|2nd|3rd))\s+)?"
            + @"(?<book>(?i:" + names + @"))\.?\s*(?<ch>\d{1,3})"
            + @"(?:(?::\s?|\.)(?<v1>\d{1,3})(?:\s*[-–—]\s*(?:(?<ch2>\d{1,3})\s*:\s*)?(?<v2>\d{1,3}))?)?(?![\p{L}\p{N}])";
        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    /// <summary>Finds references in order of appearance. When <paramref name="requireVerse"/> is true, chapter-only references are ignored.</summary>
    internal static List<ScriptureReference> Find(string? text, bool requireVerse = false)
    {
        var results = new List<ScriptureReference>();
        if (string.IsNullOrEmpty(text)) return results;
        foreach (Match m in Pattern.Matches(text))
        {
            if (!ByAlias.TryGetValue(Regex.Replace(m.Groups["book"].Value, @"\s+", " "), out var book)) continue;
            int number = m.Groups["num"].Success ? m.Groups["num"].Value[0] - '0'
                : m.Groups["rom"].Success ? m.Groups["rom"].Value.Length
                : m.Groups["word"].Success ? m.Groups["word"].Value.ToLowerInvariant() switch { "first" or "1st" => 1, "second" or "2nd" => 2, _ => 3 }
                : 0;
            string name;
            int chapters;
            int index = m.Index, length = m.Length;
            if (book.Numbered || (number > 0 && book.Name == "John"))
            {
                if (number == 0) continue; // "Corinthians 13" without 1/2 is ambiguous.
                name = number.ToString(CultureInfo.InvariantCulture) + " " + book.Name;
                if (!NumberedChapters.TryGetValue(name, out chapters)) continue;
            }
            else
            {
                if (number > 0)
                {
                    // A stray number before an unnumbered book ("3 Romans 8:1"): keep only the book part.
                    var bookGroup = m.Groups["book"];
                    length = m.Index + m.Length - bookGroup.Index;
                    index = bookGroup.Index;
                }
                name = book.Name;
                chapters = book.Chapters;
            }
            int ch = int.Parse(m.Groups["ch"].Value, CultureInfo.InvariantCulture);
            int? v1 = m.Groups["v1"].Success ? int.Parse(m.Groups["v1"].Value, CultureInfo.InvariantCulture) : null;
            int? ch2 = m.Groups["ch2"].Success ? int.Parse(m.Groups["ch2"].Value, CultureInfo.InvariantCulture) : null;
            int? v2 = m.Groups["v2"].Success ? int.Parse(m.Groups["v2"].Value, CultureInfo.InvariantCulture) : null;
            if (chapters == 1 && v1 == null && ch > 1) { v1 = ch; ch = 1; } // "Jude 5" = verse 5.
            if (ch < 1 || ch > chapters) continue;
            if (v1 is int a && (a < 1 || a > 176)) continue;
            if (ch2 is int c2 && (c2 <= ch || c2 > chapters)) { ch2 = null; v2 = null; }
            if (ch2 == null && v2 is int b && v1 is int start && b < start) v2 = null;
            if (requireVerse && v1 == null) continue;
            results.Add(new ScriptureReference(name, ch, v1, ch2, v2, index, length));
        }
        return results;
    }

    internal static List<string> Distinct(string? text, bool requireVerse = false) =>
        Find(text, requireVerse).Select(r => r.Canonical).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    internal static Uri PassageLink(ScriptureReference reference, string translation = DefaultTranslation) =>
        new("https://www.biblegateway.com/passage/?search=" + Uri.EscapeDataString(reference.Canonical) + "&version=" + Uri.EscapeDataString(translation));

    /// <summary>Public-domain KJV passage preview. Only the reference (never sermon text) is sent.</summary>
    internal static string PreviewUrl(ScriptureReference reference) =>
        "https://bible-api.com/" + Uri.EscapeDataString(reference.Canonical) + "?translation=kjv";
}
