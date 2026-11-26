using System.Text.Json;
namespace SermonStudio;
internal static class ArchiveRules
{
    internal static List<SermonRecord> NormalizeRecords(IEnumerable<SermonRecord?>? records)
    {
        var list = new List<SermonRecord>();
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in records ?? Enumerable.Empty<SermonRecord?>())
        {
            if (r == null) continue;
            if (string.IsNullOrWhiteSpace(r.Id)||!ids.Add(r.Id)){r.Id=Guid.NewGuid().ToString();ids.Add(r.Id);}
            r.Title ??= ""; r.Date ??= ""; r.Original ??= ""; r.Cleaned ??= "";
            r.Outline ??= ""; r.Srt ??= ""; r.Vtt ??= "";
            r.Speaker ??= ""; r.PrimaryScripture ??= ""; r.Tags ??= ""; r.Series ??= ""; r.Collection ??= "";
            list.Add(r);
        }
        return list;
    }

    internal static readonly System.Text.Json.JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
    internal static readonly System.Text.Json.JsonSerializerOptions LibraryWriteOptions = new() { WriteIndented = true };
    private static readonly System.Text.Json.JsonSerializerOptions MacWriteOptions = new() { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    /// <summary>Parses a library or backup file. Empty, truncated or non-array content throws instead of being treated as an empty library.</summary>
    internal static List<SermonRecord> ParseLibrary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("The sermon archive is empty or truncated.");
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) throw new InvalidDataException("The sermon archive is not a list of sermons.");
            return NormalizeRecords(document.RootElement.Deserialize<List<SermonRecord?>>(ReadOptions));
        }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException("The sermon archive is not valid JSON: " + ex.Message, ex); }
    }

    internal static string SerializeLibrary(IEnumerable<SermonRecord> records) => System.Text.Json.JsonSerializer.Serialize(records, LibraryWriteOptions);

    internal static SermonRecord Clone(SermonRecord record) =>
        System.Text.Json.JsonSerializer.Deserialize<SermonRecord>(System.Text.Json.JsonSerializer.Serialize(record))!;

    internal sealed record RestorePlan(List<SermonRecord> Additions, int Skipped, int Total);

    /// <summary>
    /// Validates a backup and returns the sermons it would add. Existing sermons are never overwritten;
    /// records whose identifier already exists are skipped.
    /// </summary>
    internal static RestorePlan PlanRestore(IEnumerable<SermonRecord> existing, string? backupJson)
    {
        if (string.IsNullOrWhiteSpace(backupJson)) throw new InvalidDataException("The backup file is empty.");
        List<SermonRecord?>? raw;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(backupJson);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) throw new InvalidDataException("The backup is not a list of sermons.");
            raw = document.RootElement.Deserialize<List<SermonRecord?>>(ReadOptions);
        }
        catch (System.Text.Json.JsonException ex) { throw new InvalidDataException("The backup is not valid JSON: " + ex.Message, ex); }
        if (raw == null) throw new InvalidDataException("Invalid archive.");
        if (raw.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id))) throw new InvalidDataException("Invalid sermon record.");
        if (raw.Select(x => x!.Id).Distinct(StringComparer.Ordinal).Count() != raw.Count) throw new InvalidDataException("Backup contains duplicate identifiers.");
        var records = NormalizeRecords(raw);
        var ids = existing.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var additions = records.Where(x => !ids.Contains(x.Id)).ToList();
        return new RestorePlan(additions, records.Count - additions.Count, records.Count);
    }

    /// <summary>
    /// Writes the subset the macOS app decodes: camelCase keys, non-null id/title/date/original/cleaned and
    /// segments whose identifiers are valid UUIDs (Swift decodes SermonSegment.id as UUID).
    /// </summary>
    internal static string SerializeMacArchive(IEnumerable<SermonRecord> records)
    {
        var items = records.Select(s => new
        {
            id = string.IsNullOrWhiteSpace(s.Id) ? Guid.NewGuid().ToString() : s.Id,
            title = s.Title ?? "",
            date = s.Date ?? "",
            original = s.Original ?? "",
            cleaned = s.Cleaned ?? "",
            outline = s.Outline ?? "",
            segments = (s.Segments is { Count: > 0 } ? s.Segments : Subtitles.Parse(s.Srt)).Select(x => new
            {
                id = Guid.TryParse(x.Id, out var g) ? g.ToString().ToUpperInvariant() : Guid.NewGuid().ToString().ToUpperInvariant(),
                start = double.IsFinite(x.Start) ? x.Start : 0,
                end = double.IsFinite(x.End) ? x.End : 0,
                text = x.Text ?? "",
                confidence = x.Confidence is double c && double.IsFinite(c) ? c : (double?)null,
            }).ToList(),
        });
        return System.Text.Json.JsonSerializer.Serialize(items, MacWriteOptions);
    }
}
public sealed class SermonRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "";
    public string Date { get; set; } = "";
    public string Speaker { get; set; } = "";
    public string PrimaryScripture { get; set; } = "";
    public string Series {get;set;}="";
    public string Collection {get;set;}="";
    public DateTimeOffset LastOpened {get;set;}
    public string Tags {get;set;}="";
    public bool Favorite {get;set;}
    public DateTimeOffset LastEdited {get;set;}
    [System.Text.Json.Serialization.JsonIgnore]
    public string LibraryHeading => (Favorite?"★  ":"")+(string.IsNullOrWhiteSpace(Title)?"Untitled sermon":Title);
    [System.Text.Json.Serialization.JsonIgnore]
    public string LibraryDetails => string.Join(" · ",new[]{Date,Speaker,PrimaryScripture,Series,Collection,Tags}.Where(x=>!string.IsNullOrWhiteSpace(x)));
    [System.Text.Json.Serialization.JsonIgnore]
    public string LibraryPreview {get{var value=string.IsNullOrWhiteSpace(Cleaned)?Original:Cleaned;value=System.Text.RegularExpressions.Regex.Replace(value??"",@"\s+"," ").Trim();return value.Length>150?value[..150]+"…":value;}}
    public string Original { get; set; } = "";
    public string Cleaned { get; set; } = "";
    public string Outline { get; set; } = "";
    public List<SermonSegment>? Segments {get;set;}
    public string Srt { get; set; } = "";
    public string Vtt { get; set; } = "";
}

public sealed class SermonSegment
{
    public string Id {get;set;}=Guid.NewGuid().ToString();
    public double Start {get;set;}
    public double End {get;set;}
    public string Text {get;set;}="";
    public double? Confidence {get;set;}
}
