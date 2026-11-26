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
    // Ollama is only ever contacted on the loopback interface; never route it through a system proxy.
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly TimeSpan OllamaTimeout = TimeSpan.FromMinutes(20);
    private readonly DispatcherQueueTimer _autosave;
    private readonly PreferenceStore _prefs;
    private readonly WorkspaceGuard _guard = new();
    private List<SermonRecord> _library = new();
    private string? _selectedId;
    private bool _loading;
    private bool _archiveReadFailed;
    private string? _unreadableCopy;
    private bool _sessionBackupTaken;
    private bool _lastSaveFailed;
    private bool _closeConfirmed;
    private bool _closePromptOpen;
    private CancellationTokenSource? _generation;
    /// <summary>Writes pending visual-editor changes into the Markdown outline. Set by BuildInterface.</summary>
    private Action? _syncVisual;
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
    private Button? _cancelGenerationButton;

    public MainWindow()
    {
        _loading = true;
        _prefs = new PreferenceStore(Path.GetDirectoryName(_libraryPath)!, App.Log);
        BuildInterface();
        try { var icon=Path.Combine(AppContext.BaseDirectory,"SermonStudio.ico"); if(File.Exists(icon)) AppWindow.SetIcon(icon); } catch(Exception ex) { System.Diagnostics.Debug.WriteLine("Window icon: "+ex.Message); }
        _autosave = DispatcherQueue.CreateTimer();
        _autosave.Interval = TimeSpan.FromMilliseconds(900);
        _autosave.Tick += (_, _) => { _autosave.Stop(); if (_selectedId != null && !_loading) Save(); };
        AtomicFile.CleanupTemporaryFiles(_libraryPath, TimeSpan.FromHours(1));
        _ = Task.Run(() => AtomicFile.CleanupStaleTempAudio(Path.GetTempPath(), TimeSpan.FromDays(1)));
        var loaded = LibraryFile.Load(_libraryPath);
        _library = loaded.Records;
        if (loaded.Failed)
        {
            _archiveReadFailed = true;
            _unreadableCopy = loaded.PreservedCopy;
            var backup = LibraryFile.SessionBackupPath(_libraryPath);
            Status.Text = "Library could not be loaded: " + loaded.Error
                + (_unreadableCopy != null ? " The original file is untouched and a copy was kept at " + _unreadableCopy + "." : " The original file is untouched; saving is blocked.")
                + (File.Exists(backup) ? " A previous-session backup exists at " + backup + " and can be loaded with Restore library." : "");
            App.Log("Library load failed: " + loaded.Error);
        }
        RefreshLibrary();
        _loading = false;
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) => Shutdown();
    }

    internal static List<SermonRecord> NormalizeRecords(IEnumerable<SermonRecord?>? records)=>ArchiveRules.NormalizeRecords(records);

    /// <summary>
    /// Shows a dialog without risking a crash: WinUI throws if another ContentDialog is already open,
    /// which would otherwise surface as an unhandled exception in an async event handler.
    /// </summary>
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        try
        {
            dialog.XamlRoot ??= ((FrameworkElement)Content).XamlRoot;
            return await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            App.Log("Dialog unavailable: " + ex.GetType().Name);
            Status.Text = "Close the open dialog first.";
            return ContentDialogResult.None;
        }
    }

    /// <summary>Called from the unhandled-exception handler: preserves unsaved editor content before the process ends.</summary>
    internal void EmergencyRecovery()
    {
        try
        {
            if (_closeConfirmed) return;
            if (_autosave.IsRunning && !_archiveReadFailed) { _autosave.Stop(); if (Save()) return; }
            if (_lastSaveFailed || (_selectedId == null && HasDraftContent())) WriteRecoveryCopy();
        }
        catch (Exception ex) { App.Log("Emergency recovery failed: " + ex.GetType().Name); }
    }

    private bool HasDraftContent() =>
        !string.IsNullOrWhiteSpace(Transcript.Text) || !string.IsNullOrWhiteSpace(Outline.Text)
        || (!string.IsNullOrWhiteSpace(SermonTitle.Text) && SermonTitle.Text != "Untitled sermon");

    /// <summary>
    /// Saves pending edits before the window closes. If a save fails, a new sermon was never saved, or work is
    /// still running, closing is postponed and the user chooses what happens, so nothing is lost silently.
    /// </summary>
    private async void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed) return;
        if (_closePromptOpen) { args.Cancel = true; return; }
        _syncVisual?.Invoke();
        if (_autosave.IsRunning) { _autosave.Stop(); Save(); }
        bool unsavedDraft = _selectedId == null && HasDraftContent();
        bool busy = _transcription != null || _modelDownload != null || _generation != null;
        if (!_lastSaveFailed && !unsavedDraft && !busy) return;
        args.Cancel = true;
        _closePromptOpen = true;
        try
        {
            string message = _lastSaveFailed
                ? "The open sermon could not be saved (" + Status.Text + "). Save a recovery copy to Documents\\Sermon Studio before closing?"
                : unsavedDraft ? "This new sermon has not been saved to your library yet."
                : "A transcription, download or outline generation is still running. Closing will cancel it.";
            var dialog = new ContentDialog
            {
                XamlRoot = ((FrameworkElement)Content).XamlRoot,
                Title = "Close Sermon Studio?",
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = _lastSaveFailed ? "Save recovery copy and close" : unsavedDraft ? "Save and close" : "Cancel work and close",
                SecondaryButtonText = _lastSaveFailed || unsavedDraft ? "Close without saving" : "",
                CloseButtonText = "Keep working",
                DefaultButton = ContentDialogButton.Primary,
            };
            var result = await ShowDialogAsync(dialog);
            if (result == ContentDialogResult.None) return;
            if (result == ContentDialogResult.Primary)
            {
                if (_lastSaveFailed)
                {
                    var copy = WriteRecoveryCopy();
                    if (copy == null) return;
                }
                else if (unsavedDraft && !Save()) return;
            }
            _closeConfirmed = true;
            Close();
        }
        catch (Exception ex) { App.Log("Close prompt failed: " + ex.GetType().Name); }
        finally { _closePromptOpen = false; }
    }

    /// <summary>Writes the sermon currently in the editors to a standalone archive file. Returns its path, or null on failure.</summary>
    private string? WriteRecoveryCopy()
    {
        try
        {
            _syncVisual?.Invoke();
            var record = CurrentEditorRecord(_library.FirstOrDefault(x => x.Id == _selectedId) is SermonRecord existing ? ArchiveRules.Clone(existing) : new SermonRecord());
            var path = ExportPaths.ReserveNewFile(ExportPaths.DefaultFolder(), "Recovery-" + ExportPaths.SanitizeFileName(record.Title) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), ".json");
            AtomicFile.WriteAllText(path, ArchiveRules.SerializeLibrary(new[] { record }));
            Status.Text = "Recovery copy saved: " + path + ". Use Restore library to bring it back.";
            return path;
        }
        catch (Exception ex) { Status.Text = "Recovery copy failed: " + ex.Message; return null; }
    }

    private void Shutdown()
    {
        try
        {
            if (_autosave.IsRunning) { _autosave.Stop(); Save(); }
            _transcription?.Cancel();
            KillRunningEngines();
            _modelDownload?.Cancel();
            _generation?.Cancel();
            if (_player != null) { _player.Pause(); _player.Source = null; _player.Dispose(); _player = null; }
            _http.Dispose();
        }
        catch (Exception ex) { App.Log("Shutdown: " + ex.GetType().Name); }
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
            (_filterFrom?.Date==null&&_filterTo?.Date==null||DateTime.TryParse(x.Date,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date)&&(_filterFrom?.Date==null||date.Date>=_filterFrom.Date.Value.Date)&&(_filterTo?.Date==null||date.Date<=_filterTo.Date.Value.Date)));
        if(_libraryStats!=null)_libraryStats.Text=$"{_library.Count} sermons · {_library.Count(x=>x.Favorite)} favorites · {_library.Count(x=>!string.IsNullOrWhiteSpace(x.Outline))} outlines · {_library.Select(x=>x.Series).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count()} series";
        if(_recentSermons!=null){_recentSermons.ItemsSource=_library.Where(x=>x.LastOpened!=default).OrderByDescending(x=>x.LastOpened).Take(8).ToList();_recentSermons.SelectedIndex=-1;}
        var items=(_librarySort?.SelectedIndex switch {1=>filtered.OrderBy(x=>x.Date,StringComparer.Ordinal),2=>filtered.OrderBy(x=>x.Title,StringComparer.OrdinalIgnoreCase),3=>filtered.OrderByDescending(x=>x.LastEdited),_=>filtered.OrderByDescending(x=>x.Date,StringComparer.Ordinal)}).ToList();
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

    private async Task SwitchAfterDraftPromptAsync(SermonRecord target)
    {
        try
        {
            if (!await ConfirmLeaveDraftAsync()) return;
            if (_selectedId == null) { var loading = _loading; _loading = true; Transcript.Text = ""; Outline.Text = ""; SermonTitle.Text = "Untitled sermon"; _loading = loading; }
            if (_library.Contains(target)) LibraryList.SelectedItem = (LibraryList.ItemsSource as IEnumerable<SermonRecord>)?.FirstOrDefault(x => x.Id == target.Id);
        }
        catch (Exception ex) { Status.Text = "Could not open sermon: " + ex.Message; }
    }

    /// <summary>Stops work bound to the open sermon. Call only after pending edits were saved.</summary>
    private void LeaveCurrentSermon()
    {
        _guard.Advance();
        _generation?.Cancel();
        ResetRecording();
    }

    private void Library_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LibraryList.SelectedItem is not SermonRecord item) return;
        if (item.Id == _selectedId) return;
        if(_transcription!=null){RefreshLibrary();Status.Text="Finish or cancel transcription before switching sermons.";return;}
        if (_selectedId == null && HasDraftContent())
        {
            // Never drop an unsaved new sermon silently: ask first, then reopen the chosen sermon.
            RefreshLibrary();
            _ = SwitchAfterDraftPromptAsync(item);
            return;
        }
        if (!FlushPendingSave()) { RefreshLibrary(); Status.Text = "The open sermon could not be saved. Retry Save sermon before switching."; return; }
        _loading = true;
        LeaveCurrentSermon();
        _selectedId = item.Id;
        var opened = LibraryFile.Transaction(_library, new[] { item.Id }, () => item.LastOpened = DateTimeOffset.UtcNow, PersistLibrary);
        // Saving the previous sermon rebuilds the list; re-select the sermon the user clicked.
        if (LibraryList.SelectedItem is not SermonRecord current || current.Id != item.Id)
            LibraryList.SelectedItem = (LibraryList.ItemsSource as IEnumerable<SermonRecord>)?.FirstOrDefault(x => x.Id == item.Id);
        SermonTitle.Text = item.Title;
        if(_sermonDate!=null) _sermonDate.Date=DateTimeOffset.TryParse(item.Date,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeLocal,out var date)?date:null;
        if(_speaker!=null) _speaker.Text=item.Speaker??"";
        if(_primaryScripture!=null) _primaryScripture.Text=item.PrimaryScripture??"";
        if(_sermonTags!=null)_sermonTags.Text=item.Tags??"";
        if(_series!=null)_series.Text=item.Series??"";if(_collection!=null)_collection.Text=item.Collection??"";
        if(_sermonFavorite!=null)_sermonFavorite.IsChecked=item.Favorite;
        if(_saveIndicator!=null) _saveIndicator.Text="Saved in library";
        _lastSaveFailed = false;
        _showCleaned = false; _originalText = item.Original; _cleanedText = item.Cleaned;
        _srt = item.Srt; _vtt = item.Vtt;
        if(item.Segments is {Count: >0}) { if(_srt.Length==0)_srt=MakeSubtitles(item.Segments,false); if(_vtt.Length==0)_vtt=MakeSubtitles(item.Segments,true); }
        Transcript.Text = item.Original;
        Outline.Text = item.Outline;
        _loading = false;
        Status.Text = opened == null ? "Loaded sermon from local library." : "Loaded sermon. Recent history could not be saved: " + opened.Message;
    }

    /// <summary>
    /// Asks what to do with a new sermon that was never saved. Returns true when it is safe to leave it
    /// (saved, discarded by the user, or empty).
    /// </summary>
    private async Task<bool> ConfirmLeaveDraftAsync()
    {
        if (_selectedId != null || !HasDraftContent()) return true;
        var choice = await ShowDialogAsync(new ContentDialog
        {
            XamlRoot = ((FrameworkElement)Content).XamlRoot,
            Title = "Save this new sermon?",
            Content = new TextBlock { Text = "The sermon you are working on has not been saved to your library yet.", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "Save sermon",
            SecondaryButtonText = "Discard draft",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        });
        if (choice == ContentDialogResult.None) return false;
        if (choice == ContentDialogResult.Primary) return Save();
        return true;
    }

    private async void New_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if(_transcription!=null){Status.Text="Finish or cancel transcription first.";return;}
            // Save first: if saving fails the current sermon, recording and outline stay exactly as they are.
            if (!FlushPendingSave()) { Status.Text = "The open sermon could not be saved. Retry Save sermon before starting a new one."; return; }
            if (!await ConfirmLeaveDraftAsync()) return;
            if (_transcription != null) return;
            LeaveCurrentSermon();
            ResetEditors();
            Status.Text = "New sermon. Select Save sermon to add it to your library.";
        }
        catch (Exception ex) { Status.Text = "Could not start a new sermon: " + ex.Message; }
    }

    private void ResetEditors()
    {
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
        _lastSaveFailed = false;
        _showCleaned = false; _originalText = ""; _cleanedText = ""; _srt = ""; _vtt = "";
        Transcript.Text = "";
        Outline.Text = "";
        _loading = false;
    }

    private void CancelGeneration_Click(object sender, RoutedEventArgs e) => _generation?.Cancel();

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        _syncVisual?.Invoke();
        if (_generation != null) { Status.Text = "An outline is already being generated."; return; }
        if (string.IsNullOrWhiteSpace(Transcript.Text))
        {
            Status.Text = "Enter a transcript first.";
            return;
        }
        if (Generator.SelectedIndex == 1)
        {
            var model = OllamaModel.Text.Trim();
            if (!PreferenceStore.IsValidOllamaModel(model)) { Status.Text = "Choose a local Ollama model, such as " + PreferenceStore.DefaultOllamaModel + "."; return; }
            var source = Transcript.Text; var priorOutline = Outline.Text;
            var ticket = _guard.Capture(null, source, priorOutline);
            using var generation = new CancellationTokenSource(OllamaTimeout);
            _generation = generation;
            if (_cancelGenerationButton != null) _cancelGenerationButton.IsEnabled = true;
            Status.Text = "Generating outline locally with Ollama… (Cancel generation to stop)";
            try
            {
                using var response = await _http.PostAsJsonAsync("http://127.0.0.1:11434/api/generate",
                    new { model, stream = false,
                        prompt = "Create a faithful, structured Christian sermon outline with introduction, main points, scripture references, applications and conclusion. Use Markdown: ## for headings, - for bullets, > for scripture quotations and **bold** for key phrases. Do not invent quotations or verses. Use only this transcript:\n" + OutlineMarkdown.NormalizeNewlines(source) }, generation.Token);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(generation.Token));
                var result = json.RootElement.TryGetProperty("response", out var text) ? text.GetString() ?? "" : "";
                _syncVisual?.Invoke();
                if (!_guard.IsCurrent(ticket, null, Transcript.Text, Outline.Text)) { Status.Text = "Outline result discarded because the sermon or editor changed."; return; }
                if (string.IsNullOrWhiteSpace(result)) { Status.Text = "Ollama returned an empty outline; your outline was not changed."; return; }
                Outline.Text = result.Trim();
                Status.Text = "Ollama outline generated locally. Review before sharing.";
            }
            catch (OperationCanceledException) { Status.Text = _guard.IsCurrent(ticket, null, Transcript.Text, Outline.Text) ? "Outline generation canceled or timed out. Your outline was not changed." : "Outline generation stopped because the sermon changed."; }
            catch (HttpRequestException ex) { Status.Text = "Ollama unavailable (" + ex.Message + "). Use Settings › Set up / check Ollama."; }
            catch (Exception ex) { Status.Text = "Outline generation failed: " + ex.Message; }
            finally { _generation = null; if (_cancelGenerationButton != null) _cancelGenerationButton.IsEnabled = false; }
            return;
        }
        var sentences = Regex.Split(OutlineMarkdown.NormalizeNewlines(Transcript.Text, " "), @"[.!?]+")
            .Select(x => x.Trim()).Where(x => x.Length > 18).ToList();
        if (sentences.Count == 0) { Status.Text = "Transcript has no complete sentences."; return; }
        if (!string.IsNullOrWhiteSpace(Outline.Text))
        {
            var replace = await ShowDialogAsync(new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot, Title = "Replace outline?", Content = "Generating a new outline replaces the current outline. Undo is available in the outline editor.", PrimaryButtonText = "Replace", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close });
            if (replace != ContentDialogResult.Primary) return;
        }
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

    /// <summary>Saves any pending autosave. Returns false if the open sermon has unsaved changes that could not be written.</summary>
    private bool FlushPendingSave(){
        _syncVisual?.Invoke();
        if(_autosave.IsRunning){_autosave.Stop();return Save();}
        return !_lastSaveFailed;
    }

    /// <summary>Copies the editors into <paramref name="item"/>. Line endings are stored as LF for macOS/web compatibility.</summary>
    private SermonRecord CurrentEditorRecord(SermonRecord item)
    {
        item.Title = SermonTitle.Text;
        if(_sermonDate?.Date is DateTimeOffset date) item.Date=date.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        if(_speaker!=null) item.Speaker=_speaker.Text;
        if(_primaryScripture!=null) item.PrimaryScripture=_primaryScripture.Text;
        item.Original = OutlineMarkdown.NormalizeNewlines(_showCleaned ? _originalText : Transcript.Text);
        item.Cleaned = OutlineMarkdown.NormalizeNewlines(_showCleaned ? Transcript.Text : _cleanedText);
        if(item.Srt != _srt || item.Segments == null) item.Segments=ParseSegments(_srt);
        item.Srt = _srt; item.Vtt = _vtt;
        item.Outline = OutlineMarkdown.NormalizeNewlines(Outline.Text);
        if(_sermonTags!=null)item.Tags=_sermonTags.Text;
        if(_series!=null)item.Series=_series.Text;if(_collection!=null)item.Collection=_collection.Text;
        if(_sermonFavorite!=null)item.Favorite=_sermonFavorite.IsChecked==true;
        item.LastEdited=DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(item.Date)) item.Date = DateTime.Today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture);
        return item;
    }

    /// <summary>Saves the open sermon. Returns true on success; on failure the library is rolled back and the editors keep the user's text.</summary>
    private bool Save()
    {
        _syncVisual?.Invoke();
        if(_archiveReadFailed){Status.Text="Library could not be read; saving is blocked to protect the original archive. Use Restore library or Save recovery copy.";if(_saveIndicator!=null)_saveIndicator.Text="Save failed";_lastSaveFailed=true;return false;}
        var previousId=_selectedId;
        var item = _library.FirstOrDefault(x => x.Id == _selectedId);
        var error = LibraryFile.Transaction(_library, new[] { _selectedId }, () =>
        {
            if (item == null) { item = new SermonRecord(); _library.Add(item); }
            CurrentEditorRecord(item);
        }, PersistLibrary);
        if (error != null)
        {
            _selectedId = previousId;
            RefreshLibrary();
            Status.Text = "Save failed: " + error.Message + " Your text is still in the editor.";
            if(_saveIndicator!=null) _saveIndicator.Text="Save failed";
            _lastSaveFailed = true;
            App.Log("Save failed: " + error.GetType().Name);
            return false;
        }
        _selectedId = item!.Id;
        _lastSaveFailed = false;
        RefreshLibrary();
        Status.Text = "Sermon and outline saved locally.";
        if(_saveIndicator!=null) _saveIndicator.Text="Saved at "+DateTime.Now.ToString("t");
        return true;
    }

    private void PersistLibrary(){
        if(_archiveReadFailed)throw new IOException("Original archive could not be read; writes are blocked.");
        LibraryFile.Save(_libraryPath, _library, ref _sessionBackupTaken);
    }

    private async void ManageSermon(string action){
        if(LibraryList.SelectedItem is not SermonRecord item){Status.Text="Select a sermon first.";return;}
        if(_transcription!=null){Status.Text="Finish transcription first.";return;}
        if(_archiveReadFailed){Status.Text="Library could not be read; archive changes are blocked.";return;}
        if(!FlushPendingSave()){Status.Text="The open sermon could not be saved. Retry Save sermon first.";return;}
        var input=new TextBox{Text=action=="Duplicate"?item.Title+" (copy)":item.Title};
        var dialog=new ContentDialog{XamlRoot=LibraryList.XamlRoot,Title=action+" sermon",Content=action=="Delete"?(object)("Delete “"+item.Title+"”? A recovery backup will be created first."):input,PrimaryButtonText=action,CloseButtonText="Cancel",DefaultButton=ContentDialogButton.Close};
        if(await ShowDialogAsync(dialog)!=ContentDialogResult.Primary)return;
        if(action!="Delete"&&string.IsNullOrWhiteSpace(input.Text))return;
        if(!FlushPendingSave()){Status.Text="The open sermon could not be saved. Retry Save sermon first.";return;}
        if(!_library.Contains(item)){Status.Text="That sermon is no longer in the library.";RefreshLibrary();return;}
        var error=LibraryFile.Transaction(_library,new[]{item.Id},()=>{
            if(action=="Delete"){
                // Recovery file is written before anything is removed; if it fails, nothing is deleted.
                var recovery=_libraryPath+".recovery-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".json";
                AtomicFile.WriteAllText(recovery,ArchiveRules.SerializeLibrary(_library));
                _library.Remove(item);
            }else if(action=="Duplicate"){
                var copy=ArchiveRules.Clone(item);
                copy.Id=Guid.NewGuid().ToString();copy.Title=input.Text.Trim();copy.LastOpened=default;copy.LastEdited=DateTimeOffset.UtcNow;
                if(copy.Segments!=null)foreach(var segment in copy.Segments)segment.Id=Guid.NewGuid().ToString();
                _library.Add(copy);
            }else{item.Title=input.Text.Trim();item.LastEdited=DateTimeOffset.UtcNow;}
        },PersistLibrary);
        if(error!=null){RefreshLibrary();Status.Text="Archive change failed: "+error.Message+" Nothing was changed.";return;}
        if(action=="Delete"&&_selectedId==item.Id){LeaveCurrentSermon();ResetEditors();}
        if(action=="Rename"&&_selectedId==item.Id){var loading=_loading;_loading=true;SermonTitle.Text=item.Title;_loading=loading;}
        RefreshLibrary();Status.Text=action+" completed.";
    }

    private void BulkArchive(bool filteredOnly){
        try{
            if(!FlushPendingSave()){Status.Text="The open sermon could not be saved. Retry Save sermon before exporting.";return;}
            var records=filteredOnly?(LibraryList.ItemsSource as IEnumerable<SermonRecord>??Enumerable.Empty<SermonRecord>()).ToList():_library.ToList();
            if(records.Count==0){Status.Text="No sermons to export.";return;}
            var parent=ExportPaths.DefaultFolder();
            var folder=Path.Combine(parent,ExportPaths.UniqueBaseName(parent,"Archive-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")));
            Directory.CreateDirectory(folder);
            AtomicFile.WriteAllText(Path.Combine(folder,"sermons.json"),ArchiveRules.SerializeLibrary(records));
            foreach(var record in records){
                var name=ExportPaths.UniqueBaseName(folder,ExportPaths.SanitizeFileName(record.Title,"Untitled sermon",70),"-Transcript.txt","-Outline.md","-Outline.docx");
                File.WriteAllText(Path.Combine(folder,name+"-Transcript.txt"),OutlineMarkdown.NormalizeNewlines(record.Original,"\r\n"),new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(folder,name+"-Outline.md"),OutlineMarkdown.NormalizeNewlines(record.Outline),new UTF8Encoding(false));
                DocumentWriters.WriteDocx(Path.Combine(folder,name+"-Outline.docx"),record.Title,record.Outline,true);
            }
            Status.Text="Exported "+records.Count+" sermons to "+folder;
        }catch(Exception ex){Status.Text="Archive export failed: "+ex.Message;}
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        _syncVisual?.Invoke();
        if (string.IsNullOrWhiteSpace(Outline.Text)) { Status.Text = "Create an outline before exporting."; return; }
        try
        {
            var path = ExportPaths.ReserveNewFile(ExportPaths.DefaultFolder(), ExportPaths.SanitizeFileName(SermonTitle.Text) + "-Outline", ".md");
            File.WriteAllText(path, "# " + SermonTitle.Text + "\n\n" + OutlineMarkdown.NormalizeNewlines(Outline.Text) + "\n", new UTF8Encoding(false));
            Status.Text = "Outline exported: " + path;
        }
        catch (Exception ex) { Status.Text = "Export failed: " + ex.Message; }
    }
}
