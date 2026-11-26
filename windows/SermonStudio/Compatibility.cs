using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net;
using System.IO.Compression;
using System.Diagnostics;
using System.Security.Cryptography;
namespace SermonStudio;
public sealed partial class MainWindow
{
    private static readonly JsonSerializerOptions ArchiveOptions=new(){PropertyNameCaseInsensitive=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
    private static List<SermonSegment> ParseSegments(string srt)
    {
        var result=new List<SermonSegment>();
        foreach(Match m in Regex.Matches(srt,@"(?ms)^\d+\s*\r?\n(\d\d:\d\d:\d\d,\d{3}) --> (\d\d:\d\d:\d\d,\d{3})[^\r\n]*\r?\n(.*?)(?=\r?\n\r?\n|\z)")){
            if(TimeSpan.TryParse(m.Groups[1].Value.Replace(',','.'),CultureInfo.InvariantCulture,out var a)&&TimeSpan.TryParse(m.Groups[2].Value.Replace(',','.'),CultureInfo.InvariantCulture,out var b))
                result.Add(new SermonSegment{Start=a.TotalSeconds,End=b.TotalSeconds,Text=m.Groups[3].Value.Trim()});
        }
        return result;
    }
    private static string SubtitleTime(double seconds,bool vtt){
        var t=TimeSpan.FromSeconds(Math.Max(0,seconds));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{(vtt?".":",")}{t.Milliseconds:000}";
    }
    private static string MakeSubtitles(List<SermonSegment> segments,bool vtt)
    {
        return (vtt?"WEBVTT\n\n":"")+string.Join("\n\n",segments.Select((s,i)=>(vtt?"":(i+1)+"\n")+SubtitleTime(s.Start,vtt)+" --> "+SubtitleTime(s.End,vtt)+"\n"+s.Text))+"\n";
    }
    private async void MacArchive_Click(object sender,RoutedEventArgs e)
    {
        try{
            if(_selectedId!=null)Save();
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Sermon Studio");Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,"mac-compatible-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");
            var records=_library.Select(s=>new {s.Id,s.Title,s.Date,s.Original,s.Cleaned,s.Outline,segments=s.Segments??ParseSegments(s.Srt)});
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(records,ArchiveOptions));Status.Text="Mac-compatible archive: "+path;
        }catch(Exception ex){Status.Text=ex.Message;}
    }
    private void Pdf_Click(object sender,RoutedEventArgs e)
    {
        try {
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Sermon Studio");Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,"Sermon-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".pdf");
            var doc=new MigraDoc.DocumentObjectModel.Document();
            doc.Info.Title=SermonTitle.Text;
            doc.Styles["Normal"]!.Font.Name="Arial";
            doc.Styles["Normal"].Font.Size=11;
            var section=doc.AddSection();
            section.AddParagraph(SermonTitle.Text,"Heading1");
            foreach(var line in Transcript.Text.Replace("\r","").Split('\n')) section.AddParagraph(line);
            if(!string.IsNullOrWhiteSpace(Outline.Text)){
                section=doc.AddSection();section.AddParagraph("Sermon outline","Heading1");
                foreach(var raw in Outline.Text.Replace("\r","").Split('\n')){
                    var line=raw.Trim();var heading=line.StartsWith("#");var quote=line.StartsWith(">");var bullet=line.StartsWith("- ")||line.StartsWith("* ");
                    var paragraph=section.AddParagraph();paragraph.Format.SpaceAfter=5;
                    if(heading){paragraph.Format.Font.Bold=true;paragraph.Format.Font.Size=line.StartsWith("###")?14:17;paragraph.Format.SpaceBefore=10;line=line.TrimStart('#',' ');}
                    if(quote){paragraph.Format.LeftIndent=18;paragraph.Format.Font.Italic=true;line=line.TrimStart('>',' ');}
                    if(bullet){paragraph.Format.LeftIndent=18;line="• "+line.Substring(2);}
                    foreach(var part in Regex.Split(line,@"(\*\*\*[^*]+\*\*\*|\*\*[^*]+\*\*|\*[^*]+\*)")){
                        if(part.Length==0)continue;
                        var run=paragraph.AddFormattedText(part.Trim('*'));
                        if(part.StartsWith("**")&&part.EndsWith("**"))run.Bold=true;
                        if((part.StartsWith("***")||!part.StartsWith("**"))&&part.StartsWith("*")&&part.EndsWith("*"))run.Italic=true;
                    }
                }
            }
            var renderer=new MigraDoc.Rendering.PdfDocumentRenderer{Document=doc};
            renderer.RenderDocument();renderer.PdfDocument.Save(path);
            Status.Text="PDF saved: "+path;
        }catch(Exception ex){Status.Text="PDF export failed: "+ex.Message;}
    }
    private async Task InstallReleaseAsync(JsonElement release)
    {
        if(_transcription!=null || _modelDownload!=null)throw new InvalidOperationException("Finish active transcription or download before updating.");
        var tag=release.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^windows-test-\d+-\d+$"))throw new InvalidDataException("Unexpected release tag.");
        var asset=release.GetProperty("assets").EnumerateArray().First(a=>a.GetProperty("name").GetString()=="Sermon-Studio-Windows-x64-portable.zip");
        var url=asset.GetProperty("browser_download_url").GetString()!;
        if(!url.StartsWith("https://github.com/jrcrosby91-tech/Sermon-Studio/releases/download/",StringComparison.Ordinal))throw new InvalidDataException("Unexpected download URL.");
        var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
        if(digest==null||!digest.StartsWith("sha256:"))throw new InvalidDataException("Release has no SHA-256 digest.");
        var parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SermonStudio","versions");
        Directory.CreateDirectory(parent);
        var target=Path.Combine(parent,tag);var staging=target+"-"+Guid.NewGuid().ToString("N");var zip=staging+".zip";
        try{
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(30)};
            using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
            await using(var stream=await response.Content.ReadAsStreamAsync())await using(var output=File.Create(zip))await stream.CopyToAsync(output);
            await using(var input=File.OpenRead(zip)){var hash=Convert.ToHexString(await SHA256.HashDataAsync(input));if(!hash.Equals(digest[7..],StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update checksum mismatch.");}
            await Task.Run(()=>ZipFile.ExtractToDirectory(zip,staging));
            if(!File.Exists(Path.Combine(staging,"SermonStudio.exe"))||(!File.Exists(Path.Combine(staging,"SermonStudio.pri"))&&!File.Exists(Path.Combine(staging,"resources.pri"))))throw new InvalidDataException("Incomplete application package.");
            if(Directory.Exists(target))throw new IOException("This version is already installed.");
            Directory.Move(staging,target);
            if(_transcription!=null || _modelDownload!=null)throw new InvalidOperationException("Update installed; finish active work before launching it.");
            if(_selectedId!=null || !string.IsNullOrWhiteSpace(Transcript.Text) || !string.IsNullOrWhiteSpace(Outline.Text)){
                Save();
                if(Status.Text.StartsWith("Save failed"))throw new IOException("Update installed but sermon could not be saved. Restart manually.");
            }
            var next=Process.Start(new ProcessStartInfo(Path.Combine(target,"SermonStudio.exe")){WorkingDirectory=target,UseShellExecute=true});
            if(next==null)throw new IOException("Unable to launch updated version.");
            Application.Current.Exit();
        }finally{try{File.Delete(zip);if(Directory.Exists(staging))Directory.Delete(staging,true);}catch{}}
    }
}
public sealed class SermonSegment
{
    public string Id {get;set;}=Guid.NewGuid().ToString();
    public double Start {get;set;}
    public double End {get;set;}
    public string Text {get;set;}="";
    public double? Confidence {get;set;}
}
