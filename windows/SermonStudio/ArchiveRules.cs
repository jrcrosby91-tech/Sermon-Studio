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
            list.Add(r);
        }
        return list;
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
