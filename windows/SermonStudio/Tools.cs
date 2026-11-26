using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
namespace SermonStudio;
public sealed partial class MainWindow
{
    private readonly string _modelFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SermonStudio","models");
    private ComboBox? _modelChoice;
    private CancellationTokenSource? _modelDownload;
    private TextBlock? _modelStatus;
    private ProgressBar? _modelProgress;
    private Button? _downloadButton,_cancelDownloadButton;
    private void RefreshModelStatus(){if(_modelChoice?.SelectedItem is not string model||_modelStatus==null)return;var path=Path.Combine(_modelFolder,"ggml-"+model+".bin");_modelStatus.Text=File.Exists(path)?$"Downloaded · {new FileInfo(path).Length/1048576:N0} MB on disk":"Not downloaded · internet required for initial setup";if(_downloadButton!=null)_downloadButton.IsEnabled=_modelDownload==null&&!File.Exists(path);if(_cancelDownloadButton!=null)_cancelDownloadButton.IsEnabled=_modelDownload!=null;}
    private string _bundledWhisper="",_bundledFfmpeg="";
    private void ConfigureTools(StackPanel panel)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory,"engines");
        try{
            if(Directory.Exists(bundled)) {
                _bundledWhisper = Directory.EnumerateFiles(bundled,"whisper-cli.exe",SearchOption.AllDirectories).FirstOrDefault() ?? "";
                _bundledFfmpeg = Directory.EnumerateFiles(bundled,"ffmpeg.exe",SearchOption.AllDirectories).FirstOrDefault() ?? "";
            }
        }catch(Exception ex){App.Log("Locate bundled engines: "+ex.GetType().Name);}
        // A custom path chosen in Advanced engine paths wins only while it still exists; otherwise the bundled engine is used.
        WhisperPath.Text = _prefs.GetExistingFile(PreferenceStore.WhisperPath) ?? _bundledWhisper;
        FfmpegPath.Text = _prefs.GetExistingFile(PreferenceStore.FfmpegPath) ?? _bundledFfmpeg;
        _modelChoice = new ComboBox { Header="Offline model", HorizontalAlignment=HorizontalAlignment.Stretch };
        foreach(var name in PreferenceStore.OfflineModels) _modelChoice.Items.Add(name);
        // Select the saved model before attaching SelectionChanged so initialization never overwrites the preference.
        _modelChoice.SelectedItem=_prefs.GetOfflineModel();
        void SelectModel(bool persist){
            if(_modelChoice.SelectedItem is not string model)return;
            WhisperModel.Text=Path.Combine(_modelFolder,"ggml-"+model+".bin");
            if(!persist)return;
            if(!_prefs.TrySet(PreferenceStore.OfflineModel,model,out var error))Status.Text="Could not save model preference: "+error;
            _prefs.TryClear(PreferenceStore.CustomModelPath,out _);
        }
        _modelChoice.SelectionChanged += (_,_)=>{SelectModel(true);RefreshModelStatus();};
        SelectModel(false);
        var customModel=_prefs.GetExistingFile(PreferenceStore.CustomModelPath);
        if(customModel!=null)WhisperModel.Text=customModel;
        panel.Children.Add(_modelChoice);
        void PersistPath(TextBox box,string name,Func<string> fallback){
            box.LostFocus+=(_,_)=>{
                var value=box.Text.Trim().Trim('"');
                if(value.Length==0){_prefs.TryClear(name,out _);box.Text=fallback();return;}
                if(string.Equals(value,fallback(),StringComparison.OrdinalIgnoreCase)){_prefs.TryClear(name,out _);return;}
                bool exists;try{exists=Path.IsPathFullyQualified(value)&&File.Exists(value);}catch{exists=false;}
                if(!exists){Status.Text="File not found: "+value+". The path was not saved.";return;}
                if(_prefs.TrySet(name,value,out var error))Status.Text="Engine path saved.";else Status.Text="Could not save engine path: "+error;
            };
        }
        PersistPath(WhisperPath,PreferenceStore.WhisperPath,()=>_bundledWhisper);
        PersistPath(FfmpegPath,PreferenceStore.FfmpegPath,()=>_bundledFfmpeg);
        PersistPath(WhisperModel,PreferenceStore.CustomModelPath,()=>_modelChoice.SelectedItem is string m?Path.Combine(_modelFolder,"ggml-"+m+".bin"):"");
        _modelStatus=new TextBlock{TextWrapping=TextWrapping.Wrap};panel.Children.Add(_modelStatus);
        _modelProgress=new ProgressBar{Minimum=0,Maximum=100,Visibility=Visibility.Collapsed};panel.Children.Add(_modelProgress);
        var buttons=new ResponsiveToolbar();panel.Children.Add(buttons);
        _downloadButton=new Button{Content="Download model"};_downloadButton.Click+=DownloadModel_Click;buttons.Children.Add(_downloadButton);
        _cancelDownloadButton=new Button{Content="Cancel download"};_cancelDownloadButton.Click+=(_,_)=>_modelDownload?.Cancel();buttons.Children.Add(_cancelDownloadButton);
        var open=new Button{Content="Open models folder"};open.Click+=(_,_)=>{try{Directory.CreateDirectory(_modelFolder);Process.Start(new ProcessStartInfo(_modelFolder){UseShellExecute=true});}catch(Exception ex){Status.Text=ex.Message;}};buttons.Children.Add(open);
        RefreshModelStatus();
    }
    private async void DownloadModel_Click(object sender,RoutedEventArgs e)
    {
        if(_modelDownload!=null || _modelChoice?.SelectedItem is not string model) return;
        var path=Path.Combine(_modelFolder,"ggml-"+model+".bin");
        var part=path+".part";
        if(File.Exists(path)){WhisperModel.Text=path;Status.Text="Model already downloaded.";return;}
        _modelDownload=new CancellationTokenSource();
        var token=_modelDownload.Token;
        RefreshModelStatus();if(_modelProgress!=null){_modelProgress.Visibility=Visibility.Visible;_modelProgress.IsIndeterminate=true;}
        try {
            Directory.CreateDirectory(_modelFolder);
            var url=DownloadIntegrity.ModelUrl(model);
            // Ask for the redirect only: Hugging Face reports the file's SHA-256 and size there.
            string? expectedSha=null;long? expectedSize=null;
            try{
                using var probe=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(30)};
                using var head=await probe.SendAsync(new HttpRequestMessage(HttpMethod.Head,url),token);
                if(head.Headers.TryGetValues("X-Linked-Etag",out var etag))expectedSha=DownloadIntegrity.ParseLinkedEtag(etag.FirstOrDefault());
                if(head.Headers.TryGetValues("X-Linked-Size",out var size)&&long.TryParse(size.FirstOrDefault(),out var bytes)&&bytes>0)expectedSize=bytes;
            }catch(Exception ex) when(ex is not OperationCanceledException){App.Log("Model checksum lookup failed: "+ex.GetType().Name);}
            using var client=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
            using var response=await client.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();
            var length=response.Content.Headers.ContentLength??expectedSize;
            using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total=0;
            await using(var source=await response.Content.ReadAsStreamAsync(token))
            await using(var dest=new FileStream(part,FileMode.Create,FileAccess.Write,FileShare.None,131072,FileOptions.Asynchronous)) {
                var buffer=new byte[131072]; int n; var lastUpdate=Stopwatch.StartNew();
                while((n=await source.ReadAsync(buffer.AsMemory(),token))>0){
                    await dest.WriteAsync(buffer.AsMemory(0,n),token); hash.AppendData(buffer,0,n); total+=n;
                    // Throttle UI updates: a 1.6 GB model would otherwise update the interface ~12,000 times.
                    if(lastUpdate.ElapsedMilliseconds>=250){
                        lastUpdate.Restart();
                        Status.Text=$"Downloading {model}: {total/1048576:N0} MB"+(length is long l&&l>0?$" of {l/1048576:N0} MB":"");
                        if(_modelStatus!=null)_modelStatus.Text=Status.Text;
                        if(_modelProgress!=null&&length is long known&&known>0){_modelProgress.IsIndeterminate=false;_modelProgress.Value=Math.Min(100,100.0*total/known);}
                    }
                }
                await dest.FlushAsync(token);
                dest.Flush(flushToDisk:true);
            }
            DownloadIntegrity.Verify(total,DownloadIntegrity.HexDigest(hash),length,expectedSha);
            File.Move(part,path,true);
            if(Equals(_modelChoice?.SelectedItem,model))WhisperModel.Text=path;
            Status.Text=model+" downloaded"+(expectedSha!=null?" and verified (SHA-256)":" (size verified; checksum unavailable)")+". Ready for offline transcription.";
            App.Log("Model "+model+" downloaded; sha256 "+(expectedSha!=null?"verified":"unavailable"));
        } catch(OperationCanceledException){Status.Text="Model download canceled.";}
        catch(Exception ex){Status.Text="Download failed: "+ex.Message;}
        finally{_modelDownload?.Dispose();_modelDownload=null;try{if(File.Exists(part))File.Delete(part);}catch{}if(_modelProgress!=null)_modelProgress.Visibility=Visibility.Collapsed;RefreshModelStatus();}
    }
    private async void Compare_Click(object sender,RoutedEventArgs e)
    {
        var grid=new Grid{ColumnSpacing=12};
        grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition());
        var a=new TextBox{Header="Original",Text=_showCleaned?_originalText:Transcript.Text,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=350};
        var b=new TextBox{Header="Cleaned",Text=_showCleaned?Transcript.Text:_cleanedText,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=350};
        grid.Children.Add(a);Grid.SetColumn(b,1);grid.Children.Add(b);
        await new ContentDialog{XamlRoot=((FrameworkElement)Content).XamlRoot,Title="Compare transcripts",Content=grid,CloseButtonText="Close"}.ShowAsync();
    }
    private async void Timestamps_Click(object sender,RoutedEventArgs e)
    {
        var list=new ListView{Height=350};
        foreach(var segment in Subtitles.Parse(_srt)){
            var start=segment.Start;
            var button=new Button{Content=TimeSpan.FromSeconds(start).ToString(@"hh\:mm\:ss")+"  "+segment.Text.Replace('\n',' ')};
            button.Click+=(_,_)=>{if(_player!=null)_player.PlaybackSession.Position=TimeSpan.FromSeconds(start);else Status.Text="Import the original recording to seek.";};
            list.Items.Add(button);
        }
        if(list.Items.Count==0)list.Items.Add(new TextBlock{Text="No timestamps available."});
        await new ContentDialog{XamlRoot=((FrameworkElement)Content).XamlRoot,Title="Recording timestamps",Content=list,CloseButtonText="Close"}.ShowAsync();
    }
    private bool _checkingUpdates;
    private TextBlock? _updateNotice;
    private async void Updates_Click(object sender,RoutedEventArgs e)=>await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool interactive){
        if(_checkingUpdates)return;_checkingUpdates=true;
        try{
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};client.DefaultRequestHeaders.UserAgent.ParseAdd("SermonStudio/1.0");
            using var json=JsonDocument.Parse(await client.GetStringAsync(UpdateRules.ReleasesApi));
            static string Tag(JsonElement x)=>x.TryGetProperty("tag_name",out var t)?t.GetString()??"":"";
            var release=json.RootElement.EnumerateArray().Where(x=>UpdateRules.IsReleaseTag(Tag(x))&&!(x.TryGetProperty("draft",out var draft)&&draft.ValueKind==JsonValueKind.True)).OrderByDescending(x=>UpdateRules.BuildNumber(Tag(x))).FirstOrDefault();
            if(release.ValueKind==JsonValueKind.Undefined){if(interactive)Status.Text="No Windows release found.";return;}
            var tag=release.GetProperty("tag_name").GetString()??"";
            var path=Path.Combine(AppContext.BaseDirectory,"build-tag.txt");
            var installed=File.Exists(path)?File.ReadAllText(path).Trim():null;
            if(!UpdateRules.IsNewer(tag,installed)){if(_updateNotice!=null)_updateNotice.Text="Windows build is up to date.";if(interactive)Status.Text="Already up to date.";return;}
            if(_updateNotice!=null)_updateNotice.Text="Available Windows test build: "+tag;
            if(!interactive)return;
            var result=await new ContentDialog{XamlRoot=((FrameworkElement)Content).XamlRoot,Title="Windows test release",Content=tag+" — install only when ready. Your local library will be retained.",PrimaryButtonText="Download and install",CloseButtonText="Close",DefaultButton=ContentDialogButton.Close}.ShowAsync();
            if(result==ContentDialogResult.Primary){Status.Text="Downloading verified update…";await InstallReleaseAsync(release);}
        }catch(Exception ex){if(interactive)Status.Text="Update check failed: "+ex.Message;else if(_updateNotice!=null)_updateNotice.Text="Automatic update check unavailable.";}
        finally{_checkingUpdates=false;}
    }
}
