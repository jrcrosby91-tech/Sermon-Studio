using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace SermonStudio;
public sealed partial class MainWindow
{
    private StackPanel? _audioHost;
    private void BuildInterface()
    {
        Title = "Sermon Studio";
        SolidColorBrush Brush(byte r,byte g,byte b)=>new(Windows.UI.Color.FromArgb(255,r,g,b));
        var gold=Brush(190,151,69);
        var navy=Brush(20,36,55);
        var cards=new List<Border>();
        var headings=new List<TextBlock>();
        var navButtons=new List<Button>();
        Border Card(UIElement child) {
            var b=new Border{Child=child,CornerRadius=new CornerRadius(12),BorderThickness=new Thickness(1),Padding=new Thickness(14)};
            cards.Add(b);return b;
        }
        App.Log("Building interface: toolbar");
        var root = new Grid { Padding = new Thickness(16), RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        PageTitle = new TextBlock { Text = "Sermon Studio", FontSize = 32, FontFamily=new FontFamily("Georgia") };
        root.Children.Add(PageTitle);
        var tools = new StackPanel { Spacing = 6 };
        Grid.SetRow(tools, 1); root.Children.Add(tools);
        StackPanel Bar() {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            tools.Children.Add(new ScrollViewer { Content = bar, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled });
            return bar;
        }
        void ButtonFor(Panel panel, string title, RoutedEventHandler handler) {
            var button = new Button { Content = title, CornerRadius=new CornerRadius(7), Padding=new Thickness(12,8,12,8) }; if(title=="Generate outline"||title=="Transcribe"){button.Background=gold;button.Foreground=navy;} button.Click += handler; panel.Children.Add(button);
        }
        TextBox Field(Panel panel, string header, string value = "", double width = 180) {
            var field = new TextBox { Header = header, Text = value, Width = width }; panel.Children.Add(field); return field;
        }
        var top = Bar();
        SermonTitle = Field(top,"Sermon title","Untitled sermon",240);
        Generator = new ComboBox { Header = "Outline generator", Width = 180 };
        Generator.Items.Add("Original (default)"); Generator.Items.Add("Ollama (optional)"); Generator.SelectedIndex = 0; top.Children.Add(Generator);
        ButtonFor(top,"Generate outline",Generate_Click); ButtonFor(top,"Save sermon",Save_Click); ButtonFor(top,"New",New_Click); var exports=new DropDownButton{Content="Export"};var menu=new MenuFlyout();
        void ExportItem(string label,RoutedEventHandler handler){var item=new MenuFlyoutItem{Text=label};item.Click+=handler;menu.Items.Add(item);}
        ExportItem("Outline • Markdown",Export_Click);ExportItem("PDF document",Pdf_Click);ExportItem("Word, RTF and transcript files",ExportMore_Click);exports.Flyout=menu;top.Children.Add(exports);
        var actions = Bar();
        ButtonFor(actions,"Import audio",ImportAudio_Click); ButtonFor(actions,"Transcribe",Transcribe_Click); ButtonFor(actions,"Cancel",Cancel_Click);
        ButtonFor(actions,"Clean",Clean_Click); ButtonFor(actions,"Original",Original_Click);
        FindText = Field(actions,"Find","",130); ButtonFor(actions,"Find",Find_Click);
        Replacement = Field(actions,"Replace with","",130); ButtonFor(actions,"Replace all",Replace_Click);
        ButtonFor(actions,"Scripture",Scripture_Click); ButtonFor(actions,"Compare",Compare_Click);
        TranscriptionProgress = new ProgressBar { Height = 4 }; tools.Children.Add(TranscriptionProgress);
        App.Log("Building interface: audio player");
        AudioPlayer = new MediaPlayerElement { AreTransportControlsEnabled = true, Height = 80 };
        // Attach media controls only when an audio file is imported.
        var playback=new StackPanel{Spacing=4};
        _audioHost = playback;
        App.Log("Building interface: editors");
        var workspace = new Grid { ColumnSpacing = 10 };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1,GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        Grid.SetRow(workspace,2); root.Children.Add(workspace);
        var editorCards=new List<Border>();
        TextBox Editor(string header,int column) {
            var editor = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Stretch, FontFamily=new FontFamily("Georgia"), FontSize=17, BorderThickness=new Thickness(0), Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent), Padding=new Thickness(8) };
            var panel=new Grid{RowSpacing=12};
            panel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});panel.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});
            var heading=new TextBlock{Text=header,FontFamily=new FontFamily("Georgia"),FontSize=23};headings.Add(heading);panel.Children.Add(heading);
            Grid.SetRow(editor,1);panel.Children.Add(editor);
            var card=Card(panel);editorCards.Add(card);Grid.SetColumn(card,column);workspace.Children.Add(card);return editor;
        }
        Transcript = Editor("Transcript",0); Outline = Editor("Sermon outline",1);
        var transcriptPanel=(Grid)editorCards[0].Child;
        var outlinePanel=(Grid)editorCards[1].Child;
        transcriptPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(playback,2);transcriptPanel.Children.Add(playback);
        var timed=new StackPanel{Spacing=8};var timedScroll=new ScrollViewer{Content=timed,Visibility=Visibility.Collapsed};
        Grid.SetRow(timedScroll,1);transcriptPanel.Children.Add(timedScroll);
        var preview=new StackPanel{Spacing=8};var previewScroll=new ScrollViewer{Content=preview,Visibility=Visibility.Collapsed};
        Grid.SetRow(previewScroll,1);outlinePanel.Children.Add(previewScroll);
        var timedToggle=new Button{Content="Timestamp view",HorizontalAlignment=HorizontalAlignment.Right};
        transcriptPanel.Children.Add(timedToggle);
        timedToggle.Click+=(_,_)=>{
            bool show=timedScroll.Visibility!=Visibility.Visible;
            timed.Children.Clear();
            if(show){
                foreach(var segment in ParseSegments(_srt)){
                    var row=new Grid{ColumnSpacing=10};row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                    var stamp=new Button{Content=TimeSpan.FromSeconds(segment.Start).ToString(@"hh\:mm\:ss"),Foreground=gold};
                    stamp.Click+=(_,_)=>{if(_player!=null)_player.PlaybackSession.Position=TimeSpan.FromSeconds(segment.Start);else Status.Text="Import the original recording to seek.";};
                    var words=new TextBlock{Text=segment.Text,TextWrapping=TextWrapping.Wrap,FontSize=17,FontFamily=new FontFamily("Georgia")};Grid.SetColumn(words,1);row.Children.Add(stamp);row.Children.Add(words);timed.Children.Add(row);
                }
                if(timed.Children.Count==0)timed.Children.Add(new TextBlock{Text="No timestamps available."});
            }
            timedScroll.Visibility=show?Visibility.Visible:Visibility.Collapsed;Transcript.Visibility=show?Visibility.Collapsed:Visibility.Visible;timedToggle.Content=show?"Edit transcript":"Timestamp view";
        };
        var previewToggle=new Button{Content="Formatted view",HorizontalAlignment=HorizontalAlignment.Right};outlinePanel.Children.Add(previewToggle);
        previewToggle.Click+=(_,_)=>{
            bool show=previewScroll.Visibility!=Visibility.Visible;preview.Children.Clear();
            if(show)foreach(var raw in Outline.Text.Split('\n')){
                var line=raw.Trim();if(line.Length==0)continue;
                bool heading=line.StartsWith("#")||System.Text.RegularExpressions.Regex.IsMatch(line,@"^[IVXLCDM]+\.\s");
                var text=new TextBlock{Text=line.TrimStart('#',' ').Replace("**",""),TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Georgia"),FontSize=heading?22:17};
                if(heading){text.Foreground=gold;text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;}
                if(line.StartsWith("- ")||line.StartsWith("* ")) {text.Text="• "+line.Substring(2);text.Margin=new Thickness(18,0,0,0);}
                preview.Children.Add(text);
            }
            previewScroll.Visibility=show?Visibility.Visible:Visibility.Collapsed;Outline.Visibility=show?Visibility.Collapsed:Visibility.Visible;previewToggle.Content=show?"Edit outline":"Formatted view";
        };
        var divider = new Border { Width=8, HorizontalAlignment=HorizontalAlignment.Right, Background=new SolidColorBrush(Microsoft.UI.Colors.SlateGray), ManipulationMode=ManipulationModes.TranslateX };
        Grid.SetColumn(divider,0); workspace.Children.Add(divider);
        editorCards[0].Margin=new Thickness(0,0,12,0);
        divider.ManipulationDelta += (_,e) => {
            var total=workspace.ColumnDefinitions[0].ActualWidth+workspace.ColumnDefinitions[1].ActualWidth;
            if(total<240)return;
            var left=Math.Clamp(workspace.ColumnDefinitions[0].ActualWidth+e.Delta.Translation.X,120,total-120);
            workspace.ColumnDefinitions[0].Width=new GridLength(left/total,GridUnitType.Star);
            workspace.ColumnDefinitions[1].Width=new GridLength(1-left/total,GridUnitType.Star);
        };
        Transcript.TextChanged += Editor_TextChanged; Outline.TextChanged += Outline_TextChanged; SermonTitle.TextChanged += Editor_TextChanged;
        App.Log("Building interface: library");
        var side = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer { Content = side }; Grid.SetColumn(scroll,2); workspace.Children.Add(scroll);
        side.Children.Add(new TextBlock { Text = "Sermon library", FontSize = 20 });
        LibraryList = new ListView { DisplayMemberPath = "Title", Height = 180 }; LibraryList.SelectionChanged += Library_SelectionChanged; side.Children.Add(LibraryList);
        var settings=new StackPanel{Spacing=12};
        var settingsScroll=new ScrollViewer{Content=Card(settings),Visibility=Visibility.Collapsed};Grid.SetColumn(settingsScroll,2);workspace.Children.Add(settingsScroll);
        settings.Children.Add(new TextBlock{Text="Transcription and local AI",FontSize=24,FontFamily=new FontFamily("Georgia")});
        OllamaModel = Field(settings,"Ollama model","llama3.2:3b",210);
        WhisperPath = Field(settings,"Whisper CLI executable","",210);
        WhisperModel = Field(settings,"GGML model file","",210);
        FfmpegPath = Field(settings,"FFmpeg executable","",210);
        ButtonFor(side,"Backup archive",Backup_Click); ButtonFor(side,"Restore archive",Restore_Click);
        side.Children.Add(new TextBlock { Text = "Scripture references" });
        ScriptureResults = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; side.Children.Add(ScriptureResults);
        ConfigureTools(settings);
        Status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap }; Grid.SetRow(Status,3); root.Children.Add(Status);
        App.Log("Attaching interface");
        root.Loaded += (_, _) => App.Log("Root Loaded");
        EventHandler<object>? firstLayout=null;
        firstLayout=(_,_)=>{root.LayoutUpdated-=firstLayout;App.Log("First layout completed");};
        root.LayoutUpdated += firstLayout;
        var shell = new Grid();
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var sidebar = new StackPanel { Padding = new Thickness(16), Spacing = 12, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 25, 43, 65)) };
        var logo=new Canvas{Width=88,Height=70,HorizontalAlignment=HorizontalAlignment.Center};
        void Stroke(double x1,double y1,double x2,double y2){logo.Children.Add(new Microsoft.UI.Xaml.Shapes.Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=gold,StrokeThickness=3});}
        Stroke(4,25,24,19);Stroke(24,19,44,28);Stroke(44,28,64,19);Stroke(64,19,84,25);Stroke(84,25,84,61);Stroke(84,61,64,55);Stroke(64,55,44,65);Stroke(44,65,24,55);Stroke(24,55,4,61);Stroke(4,61,4,25);Stroke(44,28,44,65);Stroke(44,2,44,24);Stroke(34,10,54,10);sidebar.Children.Add(logo);
        sidebar.Children.Add(new TextBlock { Text = "Sermon Studio", TextAlignment=TextAlignment.Center, FontFamily=new FontFamily("Georgia"), FontSize = 26, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Margin = new Thickness(0,12,0,20) });
        void Navigate(string name, Action action) {
            var navContent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};navContent.Children.Add(new FontIcon{Glyph=name switch {"New sermon"=>"\uE710","Transcript editor"=>"\uE70F","Sermon outlines"=>"\uE8A5","Sermon library"=>"\uE8B7","Settings"=>"\uE713","Scripture references"=>"\uE82D","Transcribe audio"=>"\uE720",_=>"\uE8A9"},FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=18});navContent.Children.Add(new TextBlock{Text=name});
            var button = new Button { Content = navContent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment=HorizontalAlignment.Left, Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground=new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness=new Thickness(0), CornerRadius=new CornerRadius(8), Padding=new Thickness(12,12,12,12) };navButtons.Add(button);
            button.Click += (_, _) => { foreach(var b in navButtons){b.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);b.Foreground=new SolidColorBrush(Microsoft.UI.Colors.White);}button.Background=gold;button.Foreground=navy;PageTitle.Text = name; tools.Visibility=Visibility.Visible; action(); actions.Visibility=name=="Sermon library"?Visibility.Collapsed:Visibility.Visible; };
            sidebar.Children.Add(button);
        }
        void Show(bool transcript, bool outline, bool library) {
            settingsScroll.Visibility=Visibility.Collapsed;
            editorCards[0].Visibility=transcript?Visibility.Visible:Visibility.Collapsed;
            editorCards[1].Visibility=outline?Visibility.Visible:Visibility.Collapsed;
            divider.Visibility=transcript && outline?Visibility.Visible:Visibility.Collapsed;
            timedScroll.Visibility=Visibility.Collapsed;previewScroll.Visibility=Visibility.Collapsed;timedToggle.Content="Timestamp view";previewToggle.Content="Formatted view";
            Transcript.Visibility = transcript ? Visibility.Visible : Visibility.Collapsed;
            Outline.Visibility = outline ? Visibility.Visible : Visibility.Collapsed;
            scroll.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
            workspace.ColumnDefinitions[0].Width = transcript ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            workspace.ColumnDefinitions[1].Width = outline ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            workspace.ColumnDefinitions[2].Width = library ? new GridLength(transcript || outline ? 230 : 1, transcript || outline ? GridUnitType.Pixel : GridUnitType.Star) : new GridLength(0);
        }
        Navigate("New sermon", () => { New_Click(this, null!); Show(true,true,true); });
        Navigate("Transcript editor", () => Show(true,false,true));
        Navigate("Sermon outlines", () => Show(true,true,false));
        Navigate("Sermon library", () => Show(false,false,true));
        Navigate("Full workspace", () => Show(true,true,true));
        Navigate("Settings", () => { Show(false,false,true); scroll.Visibility=Visibility.Collapsed;settingsScroll.Visibility=Visibility.Visible;tools.Visibility=Visibility.Collapsed; });
        Navigate("Transcribe audio",()=>Show(true,false,false));
        Navigate("Scripture references",()=>{Show(true,false,true);Scripture_Click(this,null!);});
        var theme = new ComboBox { Header="Appearance" }; theme.Items.Add("System");theme.Items.Add("Light");theme.Items.Add("Dark");theme.SelectedIndex=0;
        var settingsPath=Path.Combine(Path.GetDirectoryName(_libraryPath)!,"appearance.txt");
        try { if(File.Exists(settingsPath)&&int.TryParse(File.ReadAllText(settingsPath),out var saved)&&saved>=0&&saved<=2)theme.SelectedIndex=saved; }catch{}
        void ApplyTheme(){
            shell.RequestedTheme=theme.SelectedIndex==1?ElementTheme.Light:theme.SelectedIndex==2?ElementTheme.Dark:ElementTheme.Default;
            Paint();
        }
        void Paint(){
            bool dark=shell.ActualTheme==ElementTheme.Dark;
            root.Background=dark?Brush(16,23,32):Brush(242,239,232);
            foreach(var c in cards){c.Background=dark?Brush(25,34,45):Brush(255,252,245);c.BorderBrush=dark?Brush(62,76,92):Brush(213,207,194);}
            foreach(var h in headings)h.Foreground=dark?Brush(238,220,173):navy;
        }
        shell.ActualThemeChanged+=(_,_)=>Paint();
        ApplyTheme();
        theme.SelectionChanged += (_,_)=>{ApplyTheme();try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,theme.SelectedIndex.ToString());}catch{}};
        sidebar.RequestedTheme=ElementTheme.Dark;
        settings.Children.Add(theme);
        Show(true,true,false);
        PageTitle.Text="Sermon outlines";
        navButtons[2].Background=gold;navButtons[2].Foreground=navy;
        shell.Children.Add(sidebar); Grid.SetColumn(root,1); shell.Children.Add(root);
        Content = shell;
    }
    private void OnFirstLayout(object? sender, object e)
    {
        if (sender is FrameworkElement root) root.LayoutUpdated -= OnFirstLayout;
        App.Log("First layout completed");
    }
}
