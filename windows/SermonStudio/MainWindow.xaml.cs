using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SermonStudio;
public sealed partial class MainWindow : Window
{
    private readonly string _libraryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SermonStudio", "sermons.json");
    private List<SermonRecord> _library = new();
    private bool _loading;
    public MainWindow()
    {
        InitializeComponent();
        try {
            if (File.Exists(_libraryPath))
                _library = JsonSerializer.Deserialize<List<SermonRecord>>(File.ReadAllText(_libraryPath)) ?? new();
        } catch (Exception ex) { Status.Text = "Library could not be loaded: " + ex.Message; }
    }
    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item) PageTitle.Text = item.Content?.ToString() ?? "Sermon Studio";
    }
    private void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (Generator.SelectedIndex == 1) {
            Status.Text = "Ollama integration is planned; choose Original for now.";
            return;
        }
        var sentences = Regex.Split(Transcript.Text.Replace('\n',' '), @"[.!?]+")
            .Select(s => s.Trim()).Where(s => s.Length > 18).ToList();
        if (sentences.Count == 0) { Status.Text = "Enter a transcript first."; return; }
        var points = new List<string>();
        int size = Math.Max(1, (int)Math.Ceiling(sentences.Count / (double)Math.Min(5, sentences.Count)));
        for (int i = 0; i < sentences.Count; i += size) {
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
        Status.Text = "Outline generated locally. Review before sharing.";
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Save();
    private void Outline_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) Status.Text = "Unsaved outline changes — select Save sermon.";
    }
    private void Save()
    {
        try {
            var item = _library.FirstOrDefault(x => x.Title == SermonTitle.Text);
            if (item is null) { item = new SermonRecord(); _library.Add(item); }
            item.Title = SermonTitle.Text;
            item.Original = Transcript.Text;
            item.Outline = Outline.Text;
            item.Date = DateTime.Today.ToString("yyyy-MM-dd");
            Directory.CreateDirectory(Path.GetDirectoryName(_libraryPath)!);
            File.WriteAllText(_libraryPath, JsonSerializer.Serialize(_library, new JsonSerializerOptions { WriteIndented = true }));
            Status.Text = "Sermon and outline saved locally.";
        } catch (Exception ex) { Status.Text = "Save failed: " + ex.Message; }
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