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

    public MainWindow()
    {
        InitializeComponent();
        _autosave = DispatcherQueue.CreateTimer();
        _autosave.Interval = TimeSpan.FromMilliseconds(900);
        _autosave.Tick += (_, _) => { _autosave.Stop(); if (_selectedId != null && !_loading) Save(); };
        try
        {
            if (File.Exists(_libraryPath))
                _library = JsonSerializer.Deserialize<List<SermonRecord>>(File.ReadAllText(_libraryPath)) ?? new();
            RefreshLibrary();
        }
        catch (Exception ex) { Status.Text = "Library could not be loaded: " + ex.Message; }
    }

    private void RefreshLibrary()
    {
        _loading = true;
        LibraryList.ItemsSource = null;
        LibraryList.ItemsSource = _library.OrderByDescending(x => x.Date).ToList();
        _loading = false;
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
            PageTitle.Text = item.Content?.ToString() ?? "Sermon Studio";
    }

    private void Library_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LibraryList.SelectedItem is not SermonRecord item) return;
        _autosave.Stop();
        _loading = true;
        _selectedId = item.Id;
        SermonTitle.Text = item.Title;
        Transcript.Text = item.Original;
        Outline.Text = item.Outline;
        _loading = false;
        Status.Text = "Loaded sermon from local library.";
    }

    private void New_Click(object sender, RoutedEventArgs e)
    {
        _autosave.Stop();
        _loading = true;
        _selectedId = null;
        LibraryList.SelectedItem = null;
        SermonTitle.Text = "Untitled sermon";
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
            Status.Text = "Contacting local Ollama service…";
            try
            {
                using var response = await _http.PostAsJsonAsync("http://127.0.0.1:11434/api/generate",
                    new { model = OllamaModel.Text.Trim(), stream = false,
                        prompt = "Create a faithful, structured Christian sermon outline with introduction, main points, scripture references, applications and conclusion. Do not invent quotations or verses. Use only this transcript:\n" + Transcript.Text });
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
        if (_selectedId == null) { Status.Text = "Unsaved draft — select Save sermon."; return; }
        Status.Text = "Saving changes…";
        _autosave.Stop();
        _autosave.Start();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => Save();

    private void Save()
    {
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
            item.Original = Transcript.Text;
            item.Outline = Outline.Text;
            if (string.IsNullOrWhiteSpace(item.Date)) item.Date = DateTime.Today.ToString("yyyy-MM-dd");
            Directory.CreateDirectory(Path.GetDirectoryName(_libraryPath)!);
            File.WriteAllText(_libraryPath, JsonSerializer.Serialize(_library, new JsonSerializerOptions { WriteIndented = true }));
            RefreshLibrary();
            Status.Text = "Sermon and outline saved locally.";
        }
        catch (Exception ex) { Status.Text = "Save failed: " + ex.Message; }
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
    public string Original { get; set; } = "";
    public string Cleaned { get; set; } = "";
    public string Outline { get; set; } = "";
}
