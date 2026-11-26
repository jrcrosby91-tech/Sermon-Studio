using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace SermonStudio;
public sealed partial class MainWindow
{
    private void BuildInterface()
    {
        Title = "Sermon Studio";
        App.Log("Building interface: toolbar");
        var root = new Grid { Padding = new Thickness(16), RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        PageTitle = new TextBlock { Text = "Sermon Studio", FontSize = 30 };
        root.Children.Add(PageTitle);
        var tools = new StackPanel { Spacing = 6 };
        Grid.SetRow(tools, 1); root.Children.Add(tools);
        StackPanel Bar() {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            tools.Children.Add(new ScrollViewer { Content = bar, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled });
            return bar;
        }
        void ButtonFor(Panel panel, string title, RoutedEventHandler handler) {
            var button = new Button { Content = title }; button.Click += handler; panel.Children.Add(button);
        }
        TextBox Field(Panel panel, string header, string value = "", double width = 180) {
            var field = new TextBox { Header = header, Text = value, Width = width }; panel.Children.Add(field); return field;
        }
        var top = Bar();
        SermonTitle = Field(top,"Sermon title","Untitled sermon",240);
        Generator = new ComboBox { Header = "Outline generator", Width = 180 };
        Generator.Items.Add("Original (default)"); Generator.Items.Add("Ollama (optional)"); Generator.SelectedIndex = 0; top.Children.Add(Generator);
        ButtonFor(top,"Generate outline",Generate_Click); ButtonFor(top,"Save sermon",Save_Click); ButtonFor(top,"New",New_Click); ButtonFor(top,"Export outline",Export_Click);
        var actions = Bar();
        ButtonFor(actions,"Import audio",ImportAudio_Click); ButtonFor(actions,"Transcribe",Transcribe_Click); ButtonFor(actions,"Cancel",Cancel_Click);
        ButtonFor(actions,"Clean",Clean_Click); ButtonFor(actions,"Original",Original_Click);
        FindText = Field(actions,"Find","",130); ButtonFor(actions,"Find",Find_Click);
        Replacement = Field(actions,"Replace with","",130); ButtonFor(actions,"Replace all",Replace_Click);
        ButtonFor(actions,"Scripture",Scripture_Click); ButtonFor(actions,"Export documents",ExportMore_Click);
        TranscriptionProgress = new ProgressBar { Height = 4 }; tools.Children.Add(TranscriptionProgress);
        App.Log("Building interface: audio player");
        AudioPlayer = new MediaPlayerElement { AreTransportControlsEnabled = true, Height = 80 }; tools.Children.Add(AudioPlayer);
        App.Log("Building interface: editors");
        var workspace = new Grid { ColumnSpacing = 10 };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        Grid.SetRow(workspace,2); root.Children.Add(workspace);
        TextBox Editor(string header,int column) {
            var editor = new TextBox { Header = header, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Stretch };
            Grid.SetColumn(editor,column); workspace.Children.Add(editor); return editor;
        }
        Transcript = Editor("Transcript",0); Outline = Editor("Sermon outline",1);
        Transcript.TextChanged += Editor_TextChanged; Outline.TextChanged += Outline_TextChanged; SermonTitle.TextChanged += Editor_TextChanged;
        App.Log("Building interface: library");
        var side = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer { Content = side }; Grid.SetColumn(scroll,2); workspace.Children.Add(scroll);
        side.Children.Add(new TextBlock { Text = "Sermon library", FontSize = 20 });
        LibraryList = new ListView { DisplayMemberPath = "Title", Height = 180 }; LibraryList.SelectionChanged += Library_SelectionChanged; side.Children.Add(LibraryList);
        OllamaModel = Field(side,"Ollama model","llama3.2:3b",210);
        WhisperPath = Field(side,"Whisper CLI executable","",210);
        WhisperModel = Field(side,"GGML model file","",210);
        FfmpegPath = Field(side,"FFmpeg executable","",210);
        ButtonFor(side,"Backup archive",Backup_Click); ButtonFor(side,"Restore archive",Restore_Click);
        side.Children.Add(new TextBlock { Text = "Scripture references" });
        ScriptureResults = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; side.Children.Add(ScriptureResults);
        Status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap }; Grid.SetRow(Status,3); root.Children.Add(Status);
        App.Log("Attaching interface");
        Content = root;
    }
}
