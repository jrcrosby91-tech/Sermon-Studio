using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Json;

namespace SermonStudio;

public sealed partial class MainWindow : Window
{
    private readonly string _libraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SermonStudio", "sermons.json");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly DispatcherQueueTimer _autosave;
    private List<SermonRecord> _library = new();
    private string? _selectedId;
    private bool _loading;
    private bool _archiveReadFailed;
    private int _workspaceRevision;
    private CalendarDatePicker? _sermonDate;
    private TextBox? _speaker, _primaryScripture;
    private TextBlock? _saveIndicator, _libraryCount;
    private TextBox? _librarySearch, _sermonTags;
    private ComboBox? _librarySort;
    private TextBox? _series, _collection, _filterSpeaker, _filterTag, _filterSeries, _filterCollection;
    private CalendarDatePicker? _filterFrom, _filterTo;
    private TextBlock? _libraryStats;
    private ComboBox? _recentSermons;
    private CheckBox? _libraryFavorites, _sermonFavorite;

    public MainWindow()
    {
        _loading = true;
        BuildInterface();
        _autosave = DispatcherQueue.CreateTimer();
        _autosave.Interval = TimeSpan.FromMilliseconds(900);
        _autosave.Tick += (_, _) => { _autosave.Stop(); if (_selectedId != null && !_loading) Save(); };
        try
        {
            if (File.Exists(_libraryPath))
                _library = NormalizeRecords(JsonSerializer.Deserialize<List<SermonRecord?>>(File.ReadAllText(_libraryPath),ArchiveOptions));
            RefreshLibrary();
        }
        catch (Exception ex)
        {
            _archiveReadFailed=true;
            // Keep a copy of the unreadable file so the next save does not overwrite the user's sermons.
            var kept = "";
            try { kept = _libraryPath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); File.Copy(_libraryPath, kept, true); }
            catch { kept = ""; }
            Status.Text = "Library could not be loaded: " + ex.Message + (kept.Length > 0 ? " A copy was kept at " + kept : "");
        }
        _loading = false;
        Closed+=(_,_)=>{if(_autosave.IsRunning){_autosave.Stop();Save();}};
    }

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

    private void RefreshLibrary()
    {
        var wasLoading = _loading;
        _loading = true;
        var query=(_librarySearch?.Text??"").Trim();
        var terms=query.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<SermonRecord> filtered=_library.Where(x=>(_libraryFavorites?.IsChecked!=true||x.Favorite)&&terms.All(term=>new[]{x.Title,x.Date,x.Speaker,x.PrimaryScripture,x.Tags,x.Series,x.Collection,x.Original,x.Cleaned,x.Outline}.Any(value=>value?.Contains(term,StringComparison.OrdinalIgnoreCase)==true)));
        bool Contains(string? value,TextBox? field)=>string.IsNullOrWhiteSpace(field?.Text)||(value??"").Contains(field!.Text.Trim(),StringComparison.OrdinalIgnoreCase);
        filtered=filtered.Where(x=>Contains(x.Speaker,_filterSpeaker)&&Contains(x.Tags,_filterTag)&&Contains(x.Series,_filterSeries)&&Contains(x.Collection,_filterCollection)&&
            (_filterFrom?.Date==null&&_filterTo?.Date==null||DateTime.TryParse(x.Date,out var date)&&(_filterFrom?.Date==null||date.Date>=_filterFrom.Date.Value.Date)&&(_filterTo?.Date==null||date.Date<=_filterTo.Date.Value.Date)));
        if(_libraryStats!=null)_libraryStats.Text=$"{_library.Count} sermons · {_library.Count(x=>x.Favorite)} favorites · {_library.Count(x=>!string.IsNullOrWhiteSpace(x.Outline))} outlines · {_library.Select(x=>x.Series).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count()} series";
        if(_recentSermons!=null){_recentSermons.ItemsSource=_library.Where(x=>x.LastOpened!=default).OrderByDescending(x=>x.LastOpened).Take(8).ToList();_recentSermons.SelectedIndex=-1;}
        var items=(_librarySort?.SelectedIndex switch {1=>filtered.OrderBy(x=>x.Date),2=>filtered.OrderBy(x=>x.Title,StringComparer.OrdinalIgnoreCase),3=>filtered.OrderByDescending(x=>x.LastEdited),_=>filtered.OrderByDescending(x=>x.Date)}).ToList();
        if(_libraryCount!=null)_libraryCount.Text=$"{items.Count} of {_library.Count} sermons";
        LibraryList.ItemsSource = items;
        // Keep the current sermon highlighted after autosave/restore rebuilds the list.
        LibraryList.SelectedItem = items.FirstOrDefault(x => x.Id == _selectedId);
        _loading = wasLoading;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
            PageTitle.Text = item.Content?.ToString() ?? "Sermon Studio";
    }

    private void Library_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LibraryList.SelectedItem is not SermonRecord item) return;
        if(_transcription!=null){RefreshLibrary();Status.Text="Finish or cancel transcription before switching sermons.";return;}
        if (_autosave.IsRunning) { _autosave.Stop(); Save(); }
        _loading = true;
        _workspaceRevision++;ResetRecording();
        _selectedId = item.Id;
        item.LastOpened=DateTimeOffset.UtcNow;
        try{PersistLibrary();}catch(Exception ex){Status.Text="Could not save recent history: "+ex.Message;}
        // Saving the previous sermon rebuilds the list; re-select the sermon the user clicked.
        if (LibraryList.SelectedItem is not SermonRecord current || current.Id != item.Id)
            LibraryList.SelectedItem = (LibraryList.ItemsSource as IEnumerable<SermonRecord>)?.FirstOrDefault(x => x.Id == item.Id);
        SermonTitle.Text = item.Title;
        if(_sermonDate!=null) _sermonDate.Date=DateTimeOffset.TryParse(item.Date,out var date)?date:null;
        if(_speaker!=null) _speaker.Text=item.Speaker??"";
        if(_primaryScripture!=null) _primaryScripture.Text=item.PrimaryScripture??"";
        if(_sermonTags!=null)_sermonTags.Text=item.Tags??"";
        if(_series!=null)_series.Text=item.Series??"";if(_collection!=null)_collection.Text=item.Collection??"";
        if(_sermonFavorite!=null)_sermonFavorite.IsChecked=item.Favorite;
        if(_saveIndicator!=null) _saveIndicator.Text="Saved in library";
        _showCleaned = false; _originalText = item.Original; _cleanedText = item.Cleaned;
        _srt = item.Srt; _vtt = item.Vtt;
        if(item.Segments is {Count: >0}) { if(_srt.Length==0)_srt=MakeSubtitles(item.Segments,false); if(_vtt.Length==0)_vtt=MakeSubtitles(item.Segments,true); }
        Transcript.Text = item.Original;
        Outline.Text = item.Outline;
        _loading = false;
        Status.Text = "Loaded sermon from local library.";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if(_transcription!=null){Status.Text="Finish or cancel transcription first.";return;}
        _workspaceRevision++;ResetRecording();
        if (_autosave.IsRunning) { _autosave.Stop(); Save(); }
        _loading = true;
        _selectedId = null;
        LibraryList.SelectedItem = null;
        SermonTitle.Text = "Untitled sermon";
        if(_sermonDate!=null) _sermonDate.Date=DateTimeOffset.Now;
        if(_speaker!=null) _speaker.Text="";
        if(_primaryScripture!=null) _primaryScripture.Text="";
        if(_sermonTags!=null)_sermonTags.Text="";
        if(_series!=null)_series.Text="";if(_collection!=null)_collection.Text="";
        if(_sermonFavorite!=null)_sermonFavorite.IsChecked=false;
        if(_saveIndicator!=null) _saveIndicator.Text="Unsaved draft";
        _showCleaned = false; _originalText = ""; _cleanedText = ""; _srt = ""; _vtt = "";
        Transcript.Text = "";
        Outline.Text = "";
        _loading = false;
        Status.Text = "New sermon. Select Save sermon to add it to your library.";
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Transcript.Text))
        {
            Status.Text = "Enter a transcript first.";
            return;
        }
        if (Generator.SelectedIndex == 1)
        {
            var revision=_workspaceRevision;var source=Transcript.Text;var priorOutline=Outline.Text;
            Status.Text = "Contacting local Ollama service…";
            try
            {
                using var response = await _http.PostAsJsonAsync("http://127.0.0.1:11434/api/generate",
                    new { model = OllamaModel.Text.Trim(), stream = false,
                        prompt = "Create a faithful, structured Christian sermon outline with introduction, main points, scripture references, applications and conclusion. Do not invent quotations or verses. Use only this transcript:\n" + Transcript.Text });
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if(revision!=_workspaceRevision||source!=Transcript.Text||priorOutline!=Outline.Text){Status.Text="Outline result discarded because the sermon or editor changed.";return;}
                Outline.Text = json.RootElement.GetProperty("response").GetString() ?? "";
                Status.Text = "Ollama outline generated locally. Review before sharing.";
            }
            catch (Exception ex) { Status.Text = "Ollama unavailable: " + ex.Message + ". Install Ollama and pull the selected model."; }
            return;
        }
        var sentences = Regex.Split(Transcript.Text.Replace('\n', ' '), @"[.!?]+")
            .Select(x => x.Trim()).Where(x => x.Length > 18).ToList();
        if (sentences.Count == 0) { Status.Text = "Transcript has no complete sentences."; return; }
        var points = new List<string>();
        var size = Math.Max(1, (int)Math.Ceiling(sentences.Count / (double)Math.Min(5, sentences.Count)));
        for (var i = 0; i < sentences.Count; i += size)
        {
            var chunk = sentences.Skip(i).Take(size).ToList();
            points.Add($"{points.Count + 1}. {chunk[0][..Math.Min(100, chunk[0].Length)]}\n   Supporting excerpt: {string.Join(". ", chunk.Skip(1).Take(2))}");
        }
        Outline.Text = string.Join("\n", new[] {
            "SERMON OUTLINE (REVIEW DRAFT)", "Title: " + SermonTitle.Text,
            "Central message: [Review transcript and add main theme]", "",
            "INTRODUCTION", string.Join(". ", sentences.Take(2)), "",
            "MAIN POINTS", string.Join("\n", points), "",
            "APPLICATION", "[Add practical applications]", "",
            "CONCLUSION", string.Join(". ", sentences.TakeLast(2))
        });
        Status.Text = "Original outline generated offline. Review before sharing.";
    }

    private void Outline_TextChanged(object sender, TextChangedEventArgs e) => Editor_TextChanged(sender, e);
    private void Editor_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        if(_saveIndicator!=null) _saveIndicator.Text=_selectedId==null?"Unsaved draft":"Saving changes…";
        if (_selectedId == null) { Status.Text = "Unsaved draft — select Save sermon."; return; }
        Status.Text = "Saving changes…";
        _autosave.Stop();
        _autosave.Start();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if(_archiveReadFailed){Status.Text="Library could not be read; saving is blocked to protect the original archive.";if(_saveIndicator!=null)_saveIndicator.Text="Save failed";return;}
        try
        {
            var item = _library.FirstOrDefault(x => x.Id == _selectedId);
            if (item == null)
            {
                item = new SermonRecord();
                _library.Add(item);
                _selectedId = item.Id;
            }
            item.Title = SermonTitle.Text;
            if(_sermonDate?.Date is DateTimeOffset date) item.Date=date.ToString("yyyy-MM-dd");
            if(_speaker!=null) item.Speaker=_speaker.Text;
            if(_primaryScripture!=null) item.PrimaryScripture=_primaryScripture.Text;
            item.Original = _showCleaned ? _originalText : Transcript.Text;
            item.Cleaned = _showCleaned ? Transcript.Text : _cleanedText;
            if(item.Srt != _srt || item.Segments == null) item.Segments=ParseSegments(_srt);
            item.Srt = _srt; item.Vtt = _vtt;
            item.Outline = Outline.Text;
            if(_sermonTags!=null)item.Tags=_sermonTags.Text;
            if(_series!=null)item.Series=_series.Text;if(_collection!=null)item.Collection=_collection.Text;
            if(_sermonFavorite!=null)item.Favorite=_sermonFavorite.IsChecked==true;
            item.LastEdited=DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(item.Date)) item.Date = DateTime.Today.ToString("yyyy-MM-dd");
            Directory.CreateDirectory(Path.GetDirectoryName(_libraryPath)!);
            File.WriteAllText(_libraryPath + ".tmp", JsonSerializer.Serialize(_library, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_libraryPath + ".tmp", _libraryPath, true);
            RefreshLibrary();
            Status.Text = "Sermon and outline saved locally.";
            if(_saveIndicator!=null) _saveIndicator.Text="Saved at "+DateTime.Now.ToString("t");
        }
        catch (Exception ex) { Status.Text = "Save failed: " + ex.Message; if(_saveIndicator!=null) _saveIndicator.Text="Save failed"; }
    }

    private void PersistLibrary(){
        if(_archiveReadFailed)throw new IOException("Original archive could not be read; writes are blocked.");
        Directory.CreateDirectory(Path.GetDirectoryName(_libraryPath)!);
        File.WriteAllText(_libraryPath+".tmp",JsonSerializer.Serialize(_library,new JsonSerializerOptions{WriteIndented=true}));
        File.Move(_libraryPath+".tmp",_libraryPath,true);
    }
    private async void ManageSermon(string action){
        if(LibraryList.SelectedItem is not SermonRecord item){Status.Text="Select a sermon first.";return;}
        if(_transcription!=null){Status.Text="Finish transcription first.";return;}
        if(_autosave.IsRunning){_autosave.Stop();Save();if(_saveIndicator?.Text=="Save failed")return;}
        var input=new TextBox{Text=action=="Duplicate"?item.Title+" (copy)":item.Title};
        var dialog=new ContentDialog{XamlRoot=LibraryList.XamlRoot,Title=action+" sermon",Content=action=="Delete"?(object)("Delete “"+item.Title+"”? A recovery backup will be created first."):input,PrimaryButtonText=action,CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close};
        if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
        if(action!="Delete"&&string.IsNullOrWhiteSpace(input.Text))return;
        var before=JsonSerializer.Serialize(_library);
        try{
            if(action=="Delete"){
                Directory.CreateDirectory(Path.GetDirectoryName(_libraryPath)!);
                File.WriteAllText(_libraryPath+".recovery-"+Guid.NewGuid().ToString("N")+".json",before);
                _library.Remove(item);
            }else if(action=="Duplicate"){
                var copy=JsonSerializer.Deserialize<SermonRecord>(JsonSerializer.Serialize(item))!;
                copy.Id=Guid.NewGuid().ToString();copy.Title=input.Text.Trim();copy.LastOpened=default;copy.LastEdited=DateTimeOffset.UtcNow;_library.Add(copy);
            }else{item.Title=input.Text.Trim();item.LastEdited=DateTimeOffset.UtcNow;}
            PersistLibrary();
        }catch(Exception ex){_library=JsonSerializer.Deserialize<List<SermonRecord>>(before)!;RefreshLibrary();Status.Text="Archive change failed: "+ex.Message;return;}
        if(action=="Delete"&&_selectedId==item.Id)New_Click(this,null!);
        if(action=="Rename"&&_selectedId==item.Id){var loading=_loading;_loading=true;SermonTitle.Text=item.Title;_loading=loading;}
        RefreshLibrary();Status.Text=action+" completed.";
    }
    private void BulkArchive(bool filteredOnly){
        try{
            if(_autosave.IsRunning){_autosave.Stop();Save();if(_saveIndicator?.Text=="Save failed")return;}
            var records=filteredOnly?(LibraryList.ItemsSource as IEnumerable<SermonRecord>??Enumerable.Empty<SermonRecord>()).ToList():_library.ToList();
            var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"Sermon Studio","Archive-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"sermons.json"),JsonSerializer.Serialize(records,new JsonSerializerOptions{WriteIndented=true}));
            foreach(var record in records){
                var name=Regex.Replace(record.Title,@"[^\p{L}\p{N} _-]","_");if(name.Length>70)name=name[..70];name+="-"+Guid.NewGuid().ToString("N")[..8];
                File.WriteAllText(Path.Combine(folder,name+"-Transcript.txt"),record.Original);
                File.WriteAllText(Path.Combine(folder,name+"-Outline.md"),record.Outline);
                WriteDocx(Path.Combine(folder,name+"-Outline.docx"),record.Title,record.Outline,true);
            }
            Status.Text="Exported "+records.Count+" sermons to "+folder;
        }catch(Exception ex){Status.Text="Archive export failed: "+ex.Message;}
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Outline.Text)) { Status.Text = "Create an outline before exporting."; return; }
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sermon Studio");
            Directory.CreateDirectory(folder);
            var name = Regex.Replace(SermonTitle.Text, @"[\\/:*?""<>|]", "_");
            if (string.IsNullOrWhiteSpace(name)) name = "Sermon";
            var path = Path.Combine(folder, name + "-Outline.md");
            File.WriteAllText(path, "# " + SermonTitle.Text + "\n\n" + Outline.Text, Encoding.UTF8);
            Status.Text = "Outline exported: " + path;
        }
        catch (Exception ex) { Status.Text = "Export failed: " + ex.Message; }
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
