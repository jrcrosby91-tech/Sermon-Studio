using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.Media.Core;
using Windows.Media.Playback;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace SermonStudio;
public sealed partial class MainWindow
{
    private CancellationTokenSource? _transcription;
    private string? _audioPath;
    private string _originalText = "";
    private string _cleanedText = "";
    private bool _showCleaned;
    private MediaPlayer? _player;
    private async Task<string?> PickFile(params string[] extensions)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        foreach (var ext in extensions) picker.FileTypeFilter.Add(ext);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    private async void ImportAudio_Click(object sender, RoutedEventArgs e)
    {
        try {
            var path = await PickFile(".wav",".mp3",".m4a",".mp4",".flac",".ogg");
            if (path == null) return;
            _audioPath = path;
            _player ??= new MediaPlayer();
            if (_audioHost != null && !_audioHost.Children.Contains(AudioPlayer)) _audioHost.Children.Add(AudioPlayer);
            _player.Source = MediaSource.CreateFromUri(new Uri(path));
            AudioPlayer.SetMediaPlayer(_player);
            Status.Text = "Recording loaded. Select Transcribe to process locally.";
        } catch(Exception ex) { Status.Text = ex.Message; }
    }
    private async void Transcribe_Click(object sender, RoutedEventArgs e)
    {
        if (_transcription != null) return;
        if (_audioPath == null) { Status.Text = "Import an audio recording first."; return; }
        var whisper = WhisperPath.Text.Trim();
        var model = WhisperModel.Text.Trim();
        if (!File.Exists(whisper) || !File.Exists(model)) {
            Status.Text = "Set paths to whisper-cli.exe and a downloaded GGML Whisper model.";
            return;
        }
        var audio = _audioPath;
        var output = Path.Combine(Path.GetTempPath(), "sermon-" + Guid.NewGuid().ToString("N"));
        _transcription = new CancellationTokenSource();
        TranscriptionProgress.IsIndeterminate = true;
        try {
            // whisper.cpp accepts WAV input reliably. Other formats require local FFmpeg conversion.
            var input = audio;
            if (!audio.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) {
                if (!File.Exists(FfmpegPath.Text.Trim())) throw new InvalidOperationException("Set the FFmpeg executable path for non-WAV recordings.");
                input = output + ".wav";
                await RunLocal(FfmpegPath.Text.Trim(), new[]{"-y","-i",audio,"-ar","16000","-ac","1","-c:a","pcm_s16le",input}, _transcription.Token);
            }
            Status.Text = "Transcribing locally with Whisper…";
            await RunLocal(whisper,new[]{"-m",model,"-f",input,"-otxt","-osrt","-ovtt","-of",output},_transcription.Token);
            Transcript.Text = await File.ReadAllTextAsync(output+".txt");
            _originalText = Transcript.Text; _cleanedText = ""; _showCleaned = false;
            _srt = File.Exists(output+".srt") ? await File.ReadAllTextAsync(output+".srt") : "";
            _vtt = File.Exists(output+".vtt") ? await File.ReadAllTextAsync(output+".vtt") : "";
            Status.Text = "Offline transcription complete. Save the sermon to retain timestamps.";
        } catch(OperationCanceledException) { Status.Text = "Transcription canceled."; }
          catch(Exception ex) { Status.Text = "Transcription failed: "+ex.Message; }
        finally {
            _transcription.Dispose(); _transcription = null; TranscriptionProgress.IsIndeterminate = false;
            foreach(var ext in new[]{".wav",".txt",".srt",".vtt"}) try { File.Delete(output+ext); } catch {}
        }
    }
    private static async Task RunLocal(string exe, string[] args, CancellationToken token)
    {
        var info = new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true};
        foreach(var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Unable to start local engine.");
        var stderr = process.StandardError.ReadToEndAsync();
        var stdout = process.StandardOutput.ReadToEndAsync();
        try { await process.WaitForExitAsync(token); }
        catch(OperationCanceledException) { if(!process.HasExited) process.Kill(true); await process.WaitForExitAsync(); throw; }
        await stdout;
        var error = await stderr;
        if(process.ExitCode!=0) throw new InvalidOperationException(error.Length>1500?error[^1500..]:error);
    }
    private void Cancel_Click(object sender,RoutedEventArgs e) => _transcription?.Cancel();
    private void Clean_Click(object sender,RoutedEventArgs e)
    {
        if(!_showCleaned) _originalText=Transcript.Text;
        _cleanedText=Regex.Replace(_originalText,@"\b(um|uh|erm)\b[ ,]*","",RegexOptions.IgnoreCase);
        _cleanedText=Regex.Replace(_cleanedText,@"[ \t]+"," ").Trim();
        _showCleaned=true; Transcript.Text=_cleanedText;
        Status.Text="Cleaned transcript displayed. Original retained.";
    }
    private void Original_Click(object sender,RoutedEventArgs e)
    {
        if(!_showCleaned) return;
        _cleanedText=Transcript.Text; _showCleaned=false; Transcript.Text=_originalText;
    }
    private void Find_Click(object sender,RoutedEventArgs e)
    {
        var q=FindText.Text;
        if(string.IsNullOrWhiteSpace(q)) return;
        var start=Transcript.SelectionStart+Transcript.SelectionLength;
        var index=Transcript.Text.IndexOf(q,Math.Min(start,Transcript.Text.Length),StringComparison.OrdinalIgnoreCase);
        if(index<0) index=Transcript.Text.IndexOf(q,StringComparison.OrdinalIgnoreCase);
        if(index<0){Status.Text="Phrase not found.";return;}
        Transcript.Focus(FocusState.Programmatic); Transcript.Select(index,q.Length);
        Status.Text="Passage found.";
    }
    private void Replace_Click(object sender,RoutedEventArgs e)
    {
        if(!string.IsNullOrEmpty(FindText.Text)) Transcript.Text=Transcript.Text.Replace(FindText.Text,Replacement.Text,StringComparison.OrdinalIgnoreCase);
    }
    private void Scripture_Click(object sender,RoutedEventArgs e)
    {
        var pattern=@"\b(?:Genesis|Exodus|Leviticus|Numbers|Deuteronomy|Joshua|Judges|Ruth|Psalms?|Proverbs|Isaiah|Jeremiah|Matthew|Mark|Luke|John|Acts|Romans|(?:[12]\s+)?Corinthians|Galatians|Ephesians|Philippians|Colossians|Hebrews|James|Revelation)\s+\d+(?::\d+(?:[-–]\d+)?)?";
        var refs=Regex.Matches(Transcript.Text,pattern,RegexOptions.IgnoreCase).Select(m=>m.Value).Distinct(StringComparer.OrdinalIgnoreCase);
        ScriptureResults.Text=string.Join("\n",refs);
        if(ScriptureResults.Text.Length==0) ScriptureResults.Text="No explicit references detected.";
    }
    private string _srt="", _vtt="";
    private void ExportMore_Click(object sender,RoutedEventArgs e)
    {
        try {
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Sermon Studio");
            Directory.CreateDirectory(folder);
            var name=Regex.Replace(SermonTitle.Text,@"[\\/:*?""<>|]","_").Trim().TrimEnd('.');
            if(string.IsNullOrWhiteSpace(name)) name="Sermon";
            var text=Transcript.Text;
            File.WriteAllText(Path.Combine(folder,name+".txt"),text);
            File.WriteAllText(Path.Combine(folder,name+".md"),"# "+SermonTitle.Text+"\n\n"+text);
            WriteRtf(Path.Combine(folder,name+".rtf"),text);
            WriteRtf(Path.Combine(folder,name+"-Outline.rtf"),Outline.Text);
            WriteDocx(Path.Combine(folder,name+".docx"),SermonTitle.Text,text);
            WriteDocx(Path.Combine(folder,name+"-Outline.docx"),SermonTitle.Text+" — Outline",Outline.Text);
            if(_srt.Length>0) File.WriteAllText(Path.Combine(folder,name+".srt"),_srt);
            if(_vtt.Length>0) File.WriteAllText(Path.Combine(folder,name+".vtt"),_vtt);
            Status.Text="Exported TXT, Markdown, DOCX and available timestamps to "+folder;
        } catch(Exception ex){Status.Text="Export failed: "+ex.Message;}
    }
    private static void WriteDocx(string path,string title,string body)
    {
        XNamespace w="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XNamespace xml=XNamespace.Xml;
        // Strip characters XML 1.0 forbids (e.g. stray control codes from transcripts); they make the writer throw.
        static string XmlSafe(string s)=>new string(s.Where(c=>XmlConvert.IsXmlChar(c)||char.IsSurrogate(c)).ToArray());
        var paragraphs=(title+"\n"+body).Split('\n').Select(line =>
            new XElement(w+"p", new XElement(w+"r", new XElement(w+"t", new XAttribute(xml+"space","preserve"), XmlSafe(line.TrimEnd('\r'))))));
        var doc=new XDocument(new XElement(w+"document",new XAttribute(XNamespace.Xmlns+"w",w),new XElement(w+"body",paragraphs)));
        using var zip=new ZipArchive(File.Create(path),ZipArchiveMode.Create);
        void Add(string name,string data){using var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false));writer.Write(data);}
        Add("[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
        Add("_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Add("word/document.xml",doc.ToString());
    }
    private void Backup_Click(object sender,RoutedEventArgs e)
    {
        try {
            if(_selectedId!=null) Save();
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Sermon Studio");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(folder,"SermonStudio-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".json");
            File.WriteAllText(path,JsonSerializer.Serialize(_library,new JsonSerializerOptions{WriteIndented=true}));
            Status.Text="Backup saved: "+path;
        }catch(Exception ex){Status.Text=ex.Message;}
    }
    private async void Restore_Click(object sender,RoutedEventArgs e)
    {
        try {
            var path=await PickFile(".json"); if(path==null)return;
            var raw=JsonSerializer.Deserialize<List<SermonRecord?>>(await File.ReadAllTextAsync(path)) ?? throw new InvalidDataException("Invalid archive.");
            if(raw.Any(x=>x==null||string.IsNullOrWhiteSpace(x.Id))) throw new InvalidDataException("Invalid sermon record.");
            var records=NormalizeRecords(raw);
            foreach(var item in records) if(!_library.Any(x=>x.Id==item.Id)) _library.Add(item);
            Directory.CreateDirectory(Path.GetDirectoryName(_libraryPath)!);
            File.WriteAllText(_libraryPath+".tmp",JsonSerializer.Serialize(_library)); File.Move(_libraryPath+".tmp",_libraryPath,true);
            RefreshLibrary(); Status.Text="Backup merged; existing sermons retained.";
        }catch(Exception ex){Status.Text="Restore failed: "+ex.Message;}
    }
}
