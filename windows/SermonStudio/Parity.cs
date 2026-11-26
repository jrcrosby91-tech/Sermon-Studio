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
    private void ResetRecording(){_audioPath=null;if(_player!=null){_player.Pause();_player.Source=null;}}
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
            if(_transcription!=null){Status.Text="Finish or cancel transcription first.";return;}
            var ticket=_guard.Capture(null);
            var path = await PickFile(".wav",".mp3",".m4a",".mp4",".flac",".ogg");
            if (path == null||!_guard.IsCurrent(ticket,null)||_transcription!=null) return;
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
        if (!string.IsNullOrWhiteSpace(Transcript.Text))
        {
            var replace = await ShowDialogAsync(new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot, Title = "Replace transcript?", Content = "Transcribing replaces the current transcript text for this sermon.", PrimaryButtonText = "Transcribe and replace", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close });
            if (replace != ContentDialogResult.Primary || _transcription != null || _audioPath == null) return;
        }
        var audio = _audioPath;
        var ffmpeg = FfmpegPath.Text.Trim();
        var ticket = _guard.Capture(null, audio);
        var output = Path.Combine(Path.GetTempPath(), "sermon-" + Guid.NewGuid().ToString("N"));
        _transcription = new CancellationTokenSource();
        TranscriptionProgress.IsIndeterminate = true;
        try {
            // whisper.cpp accepts WAV input reliably. Other formats require local FFmpeg conversion.
            var input = audio;
            if (File.Exists(ffmpeg) || !audio.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) {
                if (!File.Exists(ffmpeg)) throw new InvalidOperationException("Set the FFmpeg executable path for non-WAV recordings.");
                input = output + ".wav";
                Status.Text = "Preparing audio locally with FFmpeg…";
                await RunLocal(ffmpeg, new[]{"-nostdin","-hide_banner","-loglevel","error","-y","-i",audio,"-vn","-ar","16000","-ac","1","-c:a","pcm_s16le",input}, _transcription.Token);
            }
            Status.Text = "Transcribing locally with Whisper…";
            await RunLocal(whisper,new[]{"-m",model,"-f",input,"-otxt","-osrt","-ovtt","-of",output},_transcription.Token);
            var text = await File.ReadAllTextAsync(output+".txt");
            var srt = File.Exists(output+".srt") ? await File.ReadAllTextAsync(output+".srt") : "";
            var vtt = File.Exists(output+".vtt") ? await File.ReadAllTextAsync(output+".vtt") : "";
            // Never place a transcript into a different sermon than the one it was started for.
            if (!_guard.IsCurrent(ticket, null, _audioPath ?? "")) { Status.Text = "Transcription result discarded because the open sermon changed."; return; }
            // Set timestamps before the text so the autosave triggered by the text change stores both together.
            _srt = srt; _vtt = vtt;
            _originalText = text.Trim(); _cleanedText = ""; _showCleaned = false;
            Transcript.Text = _originalText;
            if (_selectedId == null) Status.Text = "Offline transcription complete. Select Save sermon to keep it and its timestamps.";
            else { _autosave.Stop(); if (Save()) Status.Text = "Offline transcription complete and saved with timestamps."; }
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
        _syncVisual?.Invoke();
        var refs=ScriptureReferences.Distinct(Transcript.Text+"\n"+Outline.Text);
        ScriptureResults.Text=string.Join("\n",refs);
        if(ScriptureResults.Text.Length==0) ScriptureResults.Text="No explicit references detected.";
    }
    private string _srt="", _vtt="";
    private void ExportMore_Click(object sender,RoutedEventArgs e)
    {
        _syncVisual?.Invoke();
        try {
            var folder=ExportPaths.DefaultFolder();
            var title=SermonTitle.Text;
            var exports=new List<string>{".txt",".md",".rtf","-Outline.rtf",".docx","-Outline.docx"};
            if(_srt.Length>0)exports.Add(".srt");
            if(_vtt.Length>0)exports.Add(".vtt");
            // One shared, unused base name for the whole set: existing exports are never overwritten.
            var name=ExportPaths.UniqueBaseName(folder,ExportPaths.SanitizeFileName(title),exports.ToArray());
            var text=Transcript.Text;var outline=Outline.Text;
            File.WriteAllText(Path.Combine(folder,name+".txt"),OutlineMarkdown.NormalizeNewlines(text,"\r\n"),new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(folder,name+".md"),"# "+title+"\n\n"+OutlineMarkdown.NormalizeNewlines(text)+"\n",new UTF8Encoding(false));
            DocumentWriters.WriteRtf(Path.Combine(folder,name+".rtf"),title,text,false);
            DocumentWriters.WriteRtf(Path.Combine(folder,name+"-Outline.rtf"),title+" — Outline",outline,true);
            DocumentWriters.WriteDocx(Path.Combine(folder,name+".docx"),title,text,false);
            DocumentWriters.WriteDocx(Path.Combine(folder,name+"-Outline.docx"),title+" — Outline",outline,true);
            if(_srt.Length>0) File.WriteAllText(Path.Combine(folder,name+".srt"),_srt,new UTF8Encoding(false));
            if(_vtt.Length>0) File.WriteAllText(Path.Combine(folder,name+".vtt"),_vtt,new UTF8Encoding(false));
            Status.Text="Exported TXT, Markdown, RTF, DOCX"+(_srt.Length>0?" and subtitles":"")+" as “"+name+"” to "+folder;
        } catch(Exception ex){Status.Text="Export failed: "+ex.Message;}
    }
    private void Backup_Click(object sender,RoutedEventArgs e)
    {
        try {
            if(!FlushPendingSave()){Status.Text="The open sermon could not be saved; the backup would miss your latest changes. Retry Save sermon first.";return;}
            var path=ExportPaths.ReserveNewFile(ExportPaths.DefaultFolder(),"SermonStudio-backup-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),".json");
            AtomicFile.WriteAllText(path,ArchiveRules.SerializeLibrary(_library));
            Status.Text="Backup of "+_library.Count+" sermons saved: "+path;
        }catch(Exception ex){Status.Text="Backup failed: "+ex.Message;}
    }
    private async void Restore_Click(object sender,RoutedEventArgs e)
    {
        try {
            if(_transcription!=null||!FlushPendingSave()){Status.Text="Finish transcription or resolve the save failure before restoring.";return;}
            // When the library was unreadable, its original was preserved as a separate copy at startup, so a restore
            // may safely start a new library. Without that copy, writes stay blocked.
            bool replacing=_archiveReadFailed;
            if(replacing&&_unreadableCopy==null){Status.Text="Original archive unreadable and could not be preserved. Restore is blocked to protect it.";return;}
            var path=await PickFile(".json"); if(path==null)return;
            var plan=ArchiveRules.PlanRestore(_library,await File.ReadAllTextAsync(path));
            if(_transcription!=null||!FlushPendingSave())return;
            if(plan.Additions.Count==0){Status.Text="No new sermons to restore; "+plan.Skipped+" already in your library.";return;}
            if(replacing)_archiveReadFailed=false;
            var error=LibraryFile.Transaction(_library,Array.Empty<string>(),()=>{
                if(File.Exists(_libraryPath))File.Copy(_libraryPath,_libraryPath+".before-restore-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".json");
                _library.AddRange(plan.Additions);
            },PersistLibrary);
            if(error!=null){if(replacing)_archiveReadFailed=true;RefreshLibrary();Status.Text="Restore failed: "+error.Message+" Your library was not changed.";return;}
            _lastSaveFailed=false;
            RefreshLibrary(); Status.Text=$"Restored {plan.Additions.Count} sermons; skipped {plan.Skipped} already in your library."+(replacing?" The unreadable original remains at "+_unreadableCopy+".":"");
        }catch(Exception ex){Status.Text="Restore failed: "+ex.Message;}
    }
}
