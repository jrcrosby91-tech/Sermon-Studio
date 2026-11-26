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
    private static List<SermonSegment> ParseSegments(string srt) => Subtitles.Parse(srt);
    private static string MakeSubtitles(List<SermonSegment> segments,bool vtt) => Subtitles.Make(segments,vtt);
    private async void MacArchive_Click(object sender,RoutedEventArgs e)
    {
        try{
            if(!FlushPendingSave()){Status.Text="The open sermon could not be saved. Retry Save sermon before exporting.";return;}
            var json=ArchiveRules.SerializeMacArchive(_library);
            var path=ExportPaths.ReserveNewFile(ExportPaths.DefaultFolder(),"mac-compatible-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),".json");
            await File.WriteAllTextAsync(path,json,new System.Text.UTF8Encoding(false));
            Status.Text="Mac-compatible archive ("+_library.Count+" sermons): "+path;
        }catch(Exception ex){Status.Text="Mac archive export failed: "+ex.Message;}
    }
    private async void Pdf_Click(object sender,RoutedEventArgs e)
    {
        _syncVisual?.Invoke();
        string? path=null;
        try {
            var title=SermonTitle.Text;var transcript=Transcript.Text;var outline=Outline.Text;
            path=ExportPaths.ReserveNewFile(ExportPaths.DefaultFolder(),ExportPaths.SanitizeFileName(title),".pdf");
            var target=path;
            Status.Text="Creating PDF…";
            // Layout of long transcripts can take several seconds; keep it off the UI thread.
            await Task.Run(()=>RenderPdf(target,title,transcript,outline));
            Status.Text="PDF saved: "+path;
        }catch(Exception ex){
            try{if(path!=null&&File.Exists(path)&&new FileInfo(path).Length==0)File.Delete(path);}catch{}
            Status.Text="PDF export failed: "+ex.Message;
        }
    }
    private static void RenderPdf(string path,string title,string transcript,string outline)
    {
        var doc=new MigraDoc.DocumentObjectModel.Document();
        doc.Info.Title=title;
        var normal=doc.Styles["Normal"] ?? throw new InvalidOperationException("PDF Normal style unavailable.");
        normal.Font.Name="Arial";
        normal.Font.Size=11;
        var section=doc.AddSection();
        section.AddParagraph(title,"Heading1");
        foreach(var line in OutlineMarkdown.SplitLines(transcript)) section.AddParagraph(line);
        if(!string.IsNullOrWhiteSpace(outline)){
            section=doc.AddSection();section.AddParagraph("Sermon outline","Heading1");
            foreach(var raw in OutlineMarkdown.SplitLines(outline)){
                var line=OutlineMarkdown.ParseLine(raw);
                var paragraph=section.AddParagraph();paragraph.Format.SpaceAfter=5;
                bool heading=line.Block==OutlineBlock.Heading,quote=line.Block==OutlineBlock.Quote;
                if(heading){paragraph.Format.Font.Bold=true;paragraph.Format.Font.Size=line.Level<=2?17:14;paragraph.Format.SpaceBefore=10;}
                if(quote){paragraph.Format.LeftIndent=18;paragraph.Format.Font.Italic=true;}
                if(line.Block==OutlineBlock.Bullet){paragraph.Format.LeftIndent=18;paragraph.AddText("• ");}
                if(line.Block==OutlineBlock.Numbered){paragraph.Format.LeftIndent=18;paragraph.AddText(line.Marker);}
                foreach(var part in line.Runs){
                    var run=paragraph.AddFormattedText(part.Text);
                    if(part.Bold)run.Bold=true;
                    if(part.Italic)run.Italic=true;
                }
            }
        }
        var renderer=new MigraDoc.Rendering.PdfDocumentRenderer{Document=doc};
        renderer.RenderDocument();renderer.PdfDocument.Save(path);
    }
    private async Task InstallReleaseAsync(JsonElement release)
    {
        if(_transcription!=null || _modelDownload!=null || _generation!=null)throw new InvalidOperationException("Finish active transcription, download or outline generation before updating.");
        var tag=release.GetProperty("tag_name").GetString()??"";
        if(!UpdateRules.IsReleaseTag(tag))throw new InvalidDataException("Unexpected release tag.");
        var asset=release.GetProperty("assets").EnumerateArray().FirstOrDefault(a=>a.TryGetProperty("name",out var n)&&n.GetString()==UpdateRules.AssetName);
        if(asset.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Release has no Windows package.");
        var url=asset.GetProperty("browser_download_url").GetString();
        UpdateRules.ValidateDownloadUrl(url);
        var expected=UpdateRules.RequireSha256(asset.TryGetProperty("digest",out var d)?d.GetString():null);
        if(asset.TryGetProperty("size",out var declared)&&declared.TryGetInt64(out var declaredSize)&&declaredSize>UpdateRules.MaxPackageBytes)throw new InvalidDataException("Update package is unexpectedly large.");
        var parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SermonStudio","versions");
        Directory.CreateDirectory(parent);
        var target=Path.Combine(parent,tag);var staging=target+"-"+Guid.NewGuid().ToString("N");var zip=staging+".zip";
        try{
            if(Directory.Exists(target))throw new IOException("This version is already installed at "+target+".");
            using var client=new HttpClient{Timeout=TimeSpan.FromMinutes(30)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SermonStudio/1.0");
            using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>UpdateRules.MaxPackageBytes)throw new InvalidDataException("Update package is unexpectedly large.");
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using(var stream=await response.Content.ReadAsStreamAsync())
            await using(var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous)){
                var buffer=new byte[81920];long total=0;int read;
                while((read=await stream.ReadAsync(buffer))>0){
                    total+=read;if(total>UpdateRules.MaxPackageBytes)throw new InvalidDataException("Update package is unexpectedly large.");
                    hash.AppendData(buffer,0,read);await output.WriteAsync(buffer.AsMemory(0,read));
                }
                await output.FlushAsync();
            }
            if(!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update checksum mismatch. The download was discarded.");
            await Task.Run(()=>UpdateRules.SafeExtract(zip,staging));
            if(!UpdateRules.IsCompletePackage(staging))throw new InvalidDataException("Incomplete application package.");
            Directory.Move(staging,target);
            if(_transcription!=null || _modelDownload!=null || _generation!=null)throw new InvalidOperationException("Update installed at "+target+"; finish active work, then restart from there.");
            if(!FlushPendingSave())throw new IOException("Update installed at "+target+", but the open sermon could not be saved. Save it, then start the new version manually.");
            if(_selectedId==null&&HasDraftContent()&&!Save())throw new IOException("Update installed at "+target+", but the new sermon could not be saved. Save it, then start the new version manually.");
            var next=Process.Start(new ProcessStartInfo(Path.Combine(target,"SermonStudio.exe")){WorkingDirectory=target,UseShellExecute=true});
            if(next==null)throw new IOException("Unable to launch updated version.");
            _closeConfirmed=true;
            Application.Current.Exit();
        }finally{try{if(File.Exists(zip))File.Delete(zip);if(Directory.Exists(staging))Directory.Delete(staging,true);}catch{}}
    }
}
