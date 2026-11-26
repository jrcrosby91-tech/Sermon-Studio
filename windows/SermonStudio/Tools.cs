using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SermonStudio;
public sealed partial class MainWindow
{
    private readonly string _modelFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SermonStudio","models");
    private ComboBox? _modelChoice;
    private CancellationTokenSource? _modelDownload;
    private void ConfigureTools(StackPanel panel)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory,"engines");
        if(Directory.Exists(bundled)) {
            WhisperPath.Text = Directory.EnumerateFiles(bundled,"whisper-cli.exe",SearchOption.AllDirectories).FirstOrDefault() ?? "";
            FfmpegPath.Text = Directory.EnumerateFiles(bundled,"ffmpeg.exe",SearchOption.AllDirectories).FirstOrDefault() ?? "";
        }
        _modelChoice = new ComboBox { Header="Offline model", Width=210 };
        foreach(var name in new[]{"tiny.en","base.en","small.en","medium.en","large-v3-turbo"}) _modelChoice.Items.Add(name);
        var preference=Path.Combine(Path.GetDirectoryName(_modelFolder)!,"offline-model.txt");
        _modelChoice.SelectedIndex=1;
        try{
            if(File.Exists(preference)){
                var saved=File.ReadAllText(preference).Trim();
                if(_modelChoice.Items.Contains(saved))_modelChoice.SelectedItem=saved;
            }
        }catch(Exception ex){App.Log("Read offline model preference: "+ex.Message);}
        void SelectModel(bool persist){
            if(_modelChoice.SelectedItem is not string model)return;
            WhisperModel.Text=Path.Combine(_modelFolder,"ggml-"+model+".bin");
            if(!persist)return;
            try{
                Directory.CreateDirectory(Path.GetDirectoryName(preference)!);
                File.WriteAllText(preference+".tmp",model);
                File.Move(preference+".tmp",preference,true);
            }catch(Exception ex){App.Log("Save offline model preference: "+ex.Message);Status.Text="Could not save model preference: "+ex.Message;}
        }
        _modelChoice.SelectionChanged += (_,_)=>SelectModel(true);
        SelectModel(false); panel.Children.Add(_modelChoice);
        void Add(string label,RoutedEventHandler handler) { var b=new Button{Content=label}; b.Click+=handler; panel.Children.Add(b); }
        Add("Download selected model",DownloadModel_Click);
        Add("Cancel model download",(_,_)=>_modelDownload?.Cancel());
        Add("Compare transcripts",Compare_Click);
        Add("Show timestamps",Timestamps_Click);
        Add("Check for Windows updates",Updates_Click);
        Add("Export Mac-compatible archive",MacArchive_Click);
        Add("Print / Save PDF",Pdf_Click);
    }
    private async void DownloadModel_Click(object sender,RoutedEventArgs e)
    {
        if(_modelDownload!=null || _modelChoice?.SelectedItem is not string model) return;
        Directory.CreateDirectory(_modelFolder);
        var path=Path.Combine(_modelFolder,"ggml-"+model+".bin");
        if(File.Exists(path)){WhisperModel.Text=path;Status.Text="Model already downloaded.";return;}
        _modelDownload=new CancellationTokenSource();
        var token=_modelDownload.Token;
        try {
            using var client=new HttpClient{Timeout=Timeout.InfiniteTimeSpan};
            using var response=await client.GetAsync("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-"+model+".bin",HttpCompletionOption.ResponseHeadersRead,token);
            response.EnsureSuccessStatusCode();
            await using(var source=await response.Content.ReadAsStreamAsync(token))
            await using(var dest=File.Create(path+".part")) {
                var buffer=new byte[131072]; long total=0; int n;
                while((n=await source.ReadAsync(buffer.AsMemory(),token))>0){
                    await dest.WriteAsync(buffer.AsMemory(0,n),token); total+=n;
                    Status.Text=$"Downloading {model}: {total/1048576} MB";
                }
                if(response.Content.Headers.ContentLength is long expected && total!=expected) throw new IOException("Incomplete download.");
            }
            File.Move(path+".part",path,true); if(Equals(_modelChoice?.SelectedItem,model))WhisperModel.Text=path; Status.Text=model+" downloaded and ready for offline transcription.";
        } catch(OperationCanceledException){Status.Text="Model download canceled.";}
        catch(Exception ex){Status.Text="Download failed: "+ex.Message;}
        finally{_modelDownload.Dispose();_modelDownload=null;try{File.Delete(path+".part");}catch{}}
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
        foreach(Match m in Regex.Matches(_srt,@"(?m)^(\d{2}:\d{2}:\d{2},\d{3}) --> [^\r\n]+\r?\n([^\r\n]+)")){
            var button=new Button{Content=m.Groups[1].Value+"  "+m.Groups[2].Value,Tag=m.Groups[1].Value};
            button.Click+=(_,_)=>{if(_player!=null && TimeSpan.TryParse(((string)button.Tag).Replace(',','.'),System.Globalization.CultureInfo.InvariantCulture,out var time))_player.PlaybackSession.Position=time;};
            list.Items.Add(button);
        }
        await new ContentDialog{XamlRoot=((FrameworkElement)Content).XamlRoot,Title="Recording timestamps",Content=list,CloseButtonText="Close"}.ShowAsync();
    }
    private bool _checkingUpdates;
    private TextBlock? _updateNotice;
    private static (int Run,int Attempt) BuildNumber(string tag){
        var match=Regex.Match(tag,@"^windows-test-(\d+)-(\d+)$");
        return match.Success&&int.TryParse(match.Groups[1].Value,out int run)&&int.TryParse(match.Groups[2].Value,out int attempt)?(run,attempt):(0,0);
    }
    private async void Updates_Click(object sender,RoutedEventArgs e)=>await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool interactive){
        if(_checkingUpdates)return;_checkingUpdates=true;
        try{
            using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};client.DefaultRequestHeaders.UserAgent.ParseAdd("SermonStudio/1.0");
            using var json=JsonDocument.Parse(await client.GetStringAsync("https://api.github.com/repos/jrcrosby91-tech/Sermon-Studio/releases?per_page=30"));
            var release=json.RootElement.EnumerateArray().Where(x=>BuildNumber(x.GetProperty("tag_name").GetString()??"").Run>0&&!x.GetProperty("draft").GetBoolean()).OrderByDescending(x=>BuildNumber(x.GetProperty("tag_name").GetString()??"")).FirstOrDefault();
            if(release.ValueKind==JsonValueKind.Undefined){if(interactive)Status.Text="No Windows release found.";return;}
            var tag=release.GetProperty("tag_name").GetString()??"";
            var path=Path.Combine(AppContext.BaseDirectory,"build-tag.txt");
            var installed=File.Exists(path)?BuildNumber(File.ReadAllText(path).Trim()):(0,0);
            if(installed!=(0,0)&&BuildNumber(tag).CompareTo(installed)<=0){if(_updateNotice!=null)_updateNotice.Text="Windows build is up to date.";if(interactive)Status.Text="Already up to date.";return;}
            if(_updateNotice!=null)_updateNotice.Text="Available Windows test build: "+tag;
            if(!interactive)return;
            var result=await new ContentDialog{XamlRoot=((FrameworkElement)Content).XamlRoot,Title="Windows test release",Content=tag+" — install only when ready. Your local library will be retained.",PrimaryButtonText="Download and install",CloseButtonText="Close",DefaultButton=ContentDialogButton.Close}.ShowAsync();
            if(result==ContentDialogResult.Primary){Status.Text="Downloading verified update…";await InstallReleaseAsync(release);}
        }catch(Exception ex){if(interactive)Status.Text="Update check failed: "+ex.Message;else if(_updateNotice!=null)_updateNotice.Text="Automatic update check unavailable.";}
        finally{_checkingUpdates=false;}
    }
    private static void WriteRtf(string path,string text)
    {
        var b=new StringBuilder(@"{\rtf1\ansi\uc1 ");
        foreach(char c in text){if(c=='\n')b.Append(@"\par ");else if(c=='\r')continue;else if(c=='\\'||c=='{'||c=='}')b.Append('\\').Append(c);else if(c>127)b.Append(@"\u").Append((short)c).Append('?');else b.Append(c);}
        File.WriteAllText(path,b.Append('}').ToString(),Encoding.ASCII);
    }
}
