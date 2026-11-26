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
        var root = new Grid { Padding = new Thickness(20), RowSpacing = 12 };
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
            tools.Children.Add(Card(new ScrollViewer { Content = bar, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled }));
            return bar;
        }
        void ButtonFor(Panel panel, string title, RoutedEventHandler handler) {
            var button = new Button { Content = title, CornerRadius=new CornerRadius(7), Padding=new Thickness(12,8,12,8), MinHeight=36, VerticalAlignment=VerticalAlignment.Bottom }; if(title=="Generate outline"||title=="Transcribe"){button.Background=gold;button.Foreground=navy;} button.Click += handler; panel.Children.Add(button);
        }
        TextBox Field(Panel panel, string header, string value = "", double width = 180) {
            var field = new TextBox { Header = header, Text = value, Width = width }; panel.Children.Add(field); return field;
        }
        var top = Bar();
        var preferencePath=Path.Combine(Path.GetDirectoryName(_libraryPath)!,"workspace.txt");
        double split=.5; string preferredPage="Sermon outlines";
        try { var prefs=File.ReadAllLines(preferencePath); if(prefs.Length>0 && double.TryParse(prefs[0],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var ratio)&&double.IsFinite(ratio))split=Math.Clamp(ratio,.2,.8); if(prefs.Length>1)preferredPage=prefs[1]; }catch{}
        void RememberWorkspace(){try{Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!);File.WriteAllLines(preferencePath,new[]{split.ToString(System.Globalization.CultureInfo.InvariantCulture),preferredPage});}catch{}}
        SermonTitle = Field(top,"Sermon title","Untitled sermon",240);
        _sermonDate=new CalendarDatePicker{Header="Sermon date",Date=DateTimeOffset.Now,Width=155};top.Children.Add(_sermonDate);
        _speaker=Field(top,"Speaker","",150);_primaryScripture=Field(top,"Primary scripture","",170);
        _series=Field(top,"Series","",150);_series.TextChanged+=Editor_TextChanged;
        _collection=Field(top,"Collection","",150);_collection.TextChanged+=Editor_TextChanged;
        _sermonTags=Field(top,"Tags","",160);_sermonTags.TextChanged+=Editor_TextChanged;
        _sermonFavorite=new CheckBox{Content="Favorite",VerticalAlignment=VerticalAlignment.Bottom};top.Children.Add(_sermonFavorite);_sermonFavorite.Checked+=(_,_)=>Editor_TextChanged(_sermonFavorite,null!);_sermonFavorite.Unchecked+=(_,_)=>Editor_TextChanged(_sermonFavorite,null!);
        _speaker.TextChanged+=Editor_TextChanged;_primaryScripture.TextChanged+=Editor_TextChanged;_sermonDate.DateChanged+=(_,_)=>Editor_TextChanged(_sermonDate,null!);
        Generator = new ComboBox { Header = "Outline generator", Width = 180 };
        Generator.Items.Add("Original (default)"); Generator.Items.Add("Ollama (optional)"); Generator.SelectedIndex = 0;
        ButtonFor(top,"Save sermon",Save_Click); ButtonFor(top,"New",New_Click); var exports=new DropDownButton{Content="Export"};var menu=new MenuFlyout();
        void ExportItem(string label,RoutedEventHandler handler){var item=new MenuFlyoutItem{Text=label};item.Click+=handler;menu.Items.Add(item);}
        ExportItem("Outline • Markdown",Export_Click);ExportItem("PDF document",Pdf_Click);ExportItem("Word, RTF and transcript files",ExportMore_Click);exports.Flyout=menu;top.Children.Add(exports);
        var generation = Bar(); generation.Children.Add(Generator); ButtonFor(generation,"Generate outline",Generate_Click);
        var audioActions = Bar();
        ButtonFor(audioActions,"Import audio",ImportAudio_Click); ButtonFor(audioActions,"Transcribe",Transcribe_Click); ButtonFor(audioActions,"Cancel",Cancel_Click);
        var actions = Bar();
        ButtonFor(actions,"Clean",Clean_Click); ButtonFor(actions,"Original",Original_Click);
        var search = new Microsoft.UI.Xaml.Controls.VariableSizedWrapGrid{Orientation=Orientation.Horizontal,ItemWidth=145,ItemHeight=48};
        FindText = Field(search,"","",130);FindText.PlaceholderText="Find text";
        Replacement = Field(search,"","",130);Replacement.PlaceholderText="Replace with";
        ButtonFor(actions,"Scripture",Scripture_Click); ButtonFor(actions,"Compare",Compare_Click);
        TranscriptionProgress = new ProgressBar { Height = 4 }; tools.Children.Add(TranscriptionProgress);
        App.Log("Building interface: audio player");
        AudioPlayer = new MediaPlayerElement { AreTransportControlsEnabled = true, Height = 80 };
        // Attach media controls only when an audio file is imported.
        var playback=new StackPanel{Spacing=4};
        _audioHost = playback;
        var audioControls=new StackPanel{Orientation=Orientation.Horizontal,Spacing=8};
        var audioTime=new TextBlock{Text="No recording loaded",VerticalAlignment=VerticalAlignment.Center};
        void Skip(double seconds){try{if(_player==null)return;var session=_player.PlaybackSession;session.Position=TimeSpan.FromSeconds(Math.Clamp(session.Position.TotalSeconds+seconds,0,Math.Max(0,session.NaturalDuration.TotalSeconds)));}catch(Exception ex){Status.Text=ex.Message;}}
        ButtonFor(audioControls,"−15 s",(_,_)=>Skip(-15));ButtonFor(audioControls,"+15 s",(_,_)=>Skip(15));
        var speed=new ComboBox{Width=90};foreach(var rate in new[]{0.75,1.0,1.25,1.5,2.0})speed.Items.Add(rate);speed.SelectedIndex=1;
        speed.SelectionChanged+=(_,_)=>{if(_player!=null&&speed.SelectedItem is double rate)_player.PlaybackSession.PlaybackRate=rate;};audioControls.Children.Add(speed);audioControls.Children.Add(audioTime);
        playback.Children.Add(new ScrollViewer{Content=audioControls,HorizontalScrollMode=ScrollMode.Enabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollMode=ScrollMode.Disabled});
        var seek=new Slider{Minimum=0,Maximum=1,IsEnabled=false};playback.Children.Add(seek);bool updatingSeek=false;
        seek.ValueChanged+=(_,e)=>{if(!updatingSeek&&_player!=null&&seek.IsEnabled)_player.PlaybackSession.Position=TimeSpan.FromSeconds(e.NewValue);};
        var wave=new Canvas{Height=54,Background=Brush(43,55,70)};
        var waveStatus=new TextBlock{Text="Import audio to display its waveform",FontSize=12};
        playback.Children.Add(wave);playback.Children.Add(waveStatus);
        double[] peaks=Array.Empty<double>();string? wavePath=null;CancellationTokenSource? waveCancel=null;
        double drawnWidth=-1;double[]? drawnPeaks=null;int drawnProgress=-1;
        void DrawWave(){
            double width=wave.ActualWidth;if(width<=0||peaks.Length==0){wave.Children.Clear();drawnPeaks=null;return;}
            double fraction=0;try{if(_player!=null&&_player.PlaybackSession.NaturalDuration.TotalSeconds>0)fraction=_player.PlaybackSession.Position.TotalSeconds/_player.PlaybackSession.NaturalDuration.TotalSeconds;}catch{}
            int progress=(int)Math.Clamp(fraction*peaks.Length,0,peaks.Length);if(drawnWidth==width&&ReferenceEquals(drawnPeaks,peaks)&&drawnProgress==progress)return;
            drawnWidth=width;drawnPeaks=peaks;drawnProgress=progress;wave.Children.Clear();
            for(int i=0;i<peaks.Length;i++){double h=Math.Max(1,peaks[i]*48);var bar=new Microsoft.UI.Xaml.Shapes.Rectangle{Width=Math.Max(.5,width/peaks.Length-1),Height=h,Fill=i/(double)peaks.Length<=fraction?gold:Brush(125,145,165)};Canvas.SetLeft(bar,i*width/peaks.Length);Canvas.SetTop(bar,(54-h)/2);wave.Children.Add(bar);}
        }
        wave.SizeChanged+=(_,_)=>DrawWave();
        wave.PointerPressed+=(_,e)=>{if(_player!=null&&wave.ActualWidth>0&&peaks.Length>0){var fraction=Math.Clamp(e.GetCurrentPoint(wave).Position.X/wave.ActualWidth,0,1);_player.PlaybackSession.Position=TimeSpan.FromSeconds(fraction*_player.PlaybackSession.NaturalDuration.TotalSeconds);}};
        async Task LoadWave(string path,CancellationToken token){
            var temp=Path.Combine(Path.GetTempPath(),"sermon-wave-"+Guid.NewGuid().ToString("N")+".pcm");
            try{
                var ffmpeg=FfmpegPath.Text.Trim();if(!File.Exists(ffmpeg))throw new Exception("FFmpeg is required for waveform generation.");
                waveStatus.Text="Analyzing recording locally…";
                await RunLocal(ffmpeg,new[]{"-y","-i",path,"-vn","-ac","1","-ar","8000","-f","s16le",temp},token);
                var result=await Task.Run(()=>{
                    using var stream=File.OpenRead(temp);using var reader=new BinaryReader(stream);long samples=stream.Length/2;var values=new double[160];if(samples==0)return Array.Empty<double>();
                    for(long i=0;i<samples;i++){if((i&8191)==0)token.ThrowIfCancellationRequested();int bucket=(int)Math.Min(159,i*160/samples);values[bucket]=Math.Max(values[bucket],Math.Abs((int)reader.ReadInt16())/32768.0);}
                    return values;
                },token);
                if(!token.IsCancellationRequested&&wavePath==path){peaks=result;waveStatus.Text="Recording waveform · click to seek";DrawWave();}
            }catch(OperationCanceledException){}catch(Exception ex){if(!token.IsCancellationRequested&&wavePath==path)waveStatus.Text="Waveform unavailable: "+ex.Message;}finally{try{File.Delete(temp);}catch{}}
        }
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
        Transcript.PlaceholderText="Paste or type a transcript here, or import a recording to transcribe locally.";
        Outline.PlaceholderText="Generate an outline from your transcript, or start writing your own sermon outline.";
        var transcriptPanel=(Grid)editorCards[0].Child;
        transcriptPanel.RowDefinitions.Insert(1,new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(search,1);transcriptPanel.Children.Add(search);
        Grid.SetRow((FrameworkElement)transcriptPanel.Children[transcriptPanel.Children.Count-1],1);Grid.SetRow(Transcript,2);
        var outlinePanel=(Grid)editorCards[1].Child;
        transcriptPanel.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(playback,3);transcriptPanel.Children.Add(playback);
        var timed=new StackPanel{Spacing=8};var timedScroll=new ScrollViewer{Content=timed,Visibility=Visibility.Collapsed};
        Grid.SetRow(timedScroll,2);transcriptPanel.Children.Add(timedScroll);
        var preview=new StackPanel{Spacing=8};var previewScroll=new ScrollViewer{Content=preview,Visibility=Visibility.Collapsed};
        Grid.SetRow(previewScroll,1);outlinePanel.Children.Add(previewScroll);
        var segmentRows=new List<(double Start,double End,Border Row)>();
        Border? activeRow=null;string renderedSrt="";
        var timedToggle=new Button{Content="Timestamp view",HorizontalAlignment=HorizontalAlignment.Right};
        transcriptPanel.Children.Add(timedToggle);
        void RenderTimestamps(){
            timed.Children.Clear();segmentRows.Clear();activeRow=null;renderedSrt=_srt;
            {
                foreach(var segment in ParseSegments(_srt)){
                    var row=new Grid{ColumnSpacing=10};row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                    var stamp=new Button{Content=TimeSpan.FromSeconds(segment.Start).ToString(@"hh\:mm\:ss"),Foreground=gold};
                    stamp.Click+=(_,_)=>{if(_player!=null)_player.PlaybackSession.Position=TimeSpan.FromSeconds(segment.Start);else Status.Text="Import the original recording to seek.";};
                    var words=new TextBlock{Text=segment.Text,TextWrapping=TextWrapping.Wrap,FontSize=17,FontFamily=new FontFamily("Georgia")};Grid.SetColumn(words,1);row.Children.Add(stamp);row.Children.Add(words);var segmentCard=new Border{Child=row,Padding=new Thickness(8),CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1)};timed.Children.Add(segmentCard);segmentRows.Add((segment.Start,segment.End,segmentCard));
                }
                if(timed.Children.Count==0)timed.Children.Add(new TextBlock{Text="No timestamps available."});
            }
        }
        timedToggle.Click+=(_,_)=>{
            bool show=timedScroll.Visibility!=Visibility.Visible;if(show)RenderTimestamps();
            timedScroll.Visibility=show?Visibility.Visible:Visibility.Collapsed;Transcript.Visibility=show?Visibility.Collapsed:Visibility.Visible;timedToggle.Content=show?"Edit transcript":"Timestamp view";
        };
        var follow=new CheckBox{Content="Follow audio",IsChecked=true,MinWidth=0};search.Children.Add(follow);
        var audioTimer=DispatcherQueue.CreateTimer();audioTimer.Interval=TimeSpan.FromMilliseconds(250);
        audioTimer.Tick+=(_,_)=>{try{
            if(wavePath!=_audioPath){waveCancel?.Cancel();waveCancel?.Dispose();waveCancel=new();wavePath=_audioPath;peaks=Array.Empty<double>();DrawWave();if(wavePath!=null)_=LoadWave(wavePath,waveCancel.Token);else waveStatus.Text="Import audio to display its waveform";}
            if(timedScroll.Visibility==Visibility.Visible&&renderedSrt!=_srt)RenderTimestamps();
            if(_player==null){seek.IsEnabled=false;audioTime.Text="No recording loaded";if(activeRow!=null){activeRow.Background=null;activeRow.BorderBrush=null;activeRow=null;}return;}
            var session=_player.PlaybackSession;var duration=session.NaturalDuration.TotalSeconds;var position=session.Position.TotalSeconds;
            updatingSeek=true;seek.Maximum=Math.Max(1,duration);seek.Value=Math.Clamp(position,0,seek.Maximum);seek.IsEnabled=duration>0;updatingSeek=false;
            audioTime.Text=session.Position.ToString(@"hh\:mm\:ss")+" / "+session.NaturalDuration.ToString(@"hh\:mm\:ss");
            DrawWave();
            if(timedScroll.Visibility!=Visibility.Visible)return;
            var next=segmentRows.FirstOrDefault(x=>position>=x.Start&&position<x.End).Row;
            if(next!=activeRow){if(activeRow!=null){activeRow.Background=null;activeRow.BorderBrush=null;}activeRow=next;if(next!=null){next.Background=new SolidColorBrush(Windows.UI.Color.FromArgb(45,190,151,69));next.BorderBrush=gold;if(follow.IsChecked==true)next.StartBringIntoView();}}
        }catch{updatingSeek=false;}};audioTimer.Start();Closed+=(_,_)=>{audioTimer.Stop();waveCancel?.Cancel();};
        var divider = new Border { Width=5, CornerRadius=new CornerRadius(3), HorizontalAlignment=HorizontalAlignment.Right, Background=new SolidColorBrush(Microsoft.UI.Colors.SlateGray), ManipulationMode=Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX };
        var previewToggle=new Button{Content="Formatted view",HorizontalAlignment=HorizontalAlignment.Right};outlinePanel.Children.Add(previewToggle);
        outlinePanel.RowDefinitions.Insert(1,new RowDefinition{Height=GridLength.Auto});
        Grid.SetRow(Outline,2);Grid.SetRow(previewScroll,2);
        var formatBar=new VariableSizedWrapGrid{Orientation=Orientation.Horizontal,ItemWidth=135,ItemHeight=44};
        Grid.SetRow(formatBar,1);outlinePanel.Children.Add(formatBar);
        var rich=new RichEditBox{FontFamily=new FontFamily("Georgia"),FontSize=17,TextWrapping=TextWrapping.Wrap,Visibility=Visibility.Collapsed};
        Grid.SetRow(rich,2);outlinePanel.Children.Add(rich);
        bool syncingOutline=false;
        bool visualDirty=false;
        void LoadVisual(){
            if(syncingOutline)return;syncingOutline=true;
            try{
                var plain=new System.Text.StringBuilder();
                var spans=new List<(int Start,int End,bool Bold,bool Italic,float Size)>();
                foreach(var raw in Outline.Text.Replace("\r","").Split('\n')){
                    var line=raw;float size=17;bool heading=line.StartsWith("## "),sub=line.StartsWith("### "),quote=line.StartsWith("> ");
                    if(heading||sub){size=sub?19:23;line=line.TrimStart('#',' ');}
                    if(quote)line="❯ "+line.Substring(2);
                    if(line.StartsWith("- ")||line.StartsWith("* "))line="• "+line.Substring(2);
                    foreach(var part in System.Text.RegularExpressions.Regex.Split(line,@"(\*\*\*[^*]+\*\*\*|\*\*[^*]+\*\*|\*[^*]+\*)")){
                        int start=plain.Length;bool bold=heading||sub||part.StartsWith("**")&&part.EndsWith("**");bool italic=quote||part.StartsWith("***")&&part.EndsWith("***")||part.StartsWith("*")&&!part.StartsWith("**")&&part.EndsWith("*");
                        plain.Append(part.StartsWith("*")&&part.EndsWith("*")&&part.Length>2?part.Trim('*'):part);if(plain.Length>start)spans.Add((start,plain.Length,bold,italic,size));
                    }
                    plain.Append('\r');
                }
                if(plain.Length>0)plain.Length--;
                rich.Document.SetText(Microsoft.UI.Text.TextSetOptions.None,plain.ToString());
                foreach(var span in spans){var format=rich.Document.GetRange(span.Start,span.End).CharacterFormat;format.Bold=span.Bold?Microsoft.UI.Text.FormatEffect.On:Microsoft.UI.Text.FormatEffect.Off;format.Italic=span.Italic?Microsoft.UI.Text.FormatEffect.On:Microsoft.UI.Text.FormatEffect.Off;format.Size=span.Size;}
            }finally{syncingOutline=false;visualDirty=false;}
        }
        void StoreVisual(){
            if(syncingOutline||!visualDirty||rich.Visibility!=Visibility.Visible)return;syncingOutline=true;
            try{
                rich.Document.GetText(Microsoft.UI.Text.TextGetOptions.None,out var plain);
                if(plain.EndsWith("\r"))plain=plain[..^1];
                var output=new System.Text.StringBuilder();int offset=0;
                foreach(var line in plain.Split('\r')){
                    if(offset>0)output.Append('\n');
                    if(line.Length>0){
                        var first=rich.Document.GetRange(offset,offset+1).CharacterFormat;
                        bool heading=first.Size>=22,sub=!heading&&first.Size>=19;
                        if(heading)output.Append("## ");else if(sub)output.Append("### ");
                        int i=0;bool quote=line.StartsWith("❯ ");if(quote){output.Append("> ");i=2;}else if(line.StartsWith("• ")){output.Append("- ");i=2;}
                        while(i<line.Length){
                            var format=rich.Document.GetRange(offset+i,offset+i+1).CharacterFormat;
                            bool bold=!heading&&!sub&&format.Bold==Microsoft.UI.Text.FormatEffect.On,italic=!quote&&format.Italic==Microsoft.UI.Text.FormatEffect.On;
                            int end=i+1;while(end<line.Length){var next=rich.Document.GetRange(offset+end,offset+end+1).CharacterFormat;if((!heading&&!sub&&next.Bold==Microsoft.UI.Text.FormatEffect.On)!=bold||(!quote&&next.Italic==Microsoft.UI.Text.FormatEffect.On)!=italic)break;end++;}
                            var marker=bold&&italic?"***":bold?"**":italic?"*":"";output.Append(marker).Append(line.Substring(i,end-i)).Append(marker);i=end;
                        }
                    }
                    offset+=line.Length+1;
                }
                var updated=output.ToString();if(Outline.Text!=updated)Outline.Text=updated;visualDirty=false;
            }finally{syncingOutline=false;}
        }
        rich.TextChanged+=(_,_)=>{if(!syncingOutline){visualDirty=true;StoreVisual();}};
        void OutlineEdit(){previewScroll.Visibility=Visibility.Collapsed;Outline.Visibility=Visibility.Collapsed;if(rich.Visibility!=Visibility.Visible){LoadVisual();rich.Visibility=Visibility.Visible;}previewToggle.Content="Reading view";rich.Focus(FocusState.Programmatic);}
        void VisualFormat(Action<Microsoft.UI.Text.ITextSelection> action){OutlineEdit();action(rich.Document.Selection);visualDirty=true;StoreVisual();}
        ButtonFor(formatBar,"Heading",(_,_)=>VisualFormat(sel=>{sel.CharacterFormat.Size=23;sel.CharacterFormat.Bold=Microsoft.UI.Text.FormatEffect.On;}));
        ButtonFor(formatBar,"Subheading",(_,_)=>VisualFormat(sel=>{sel.CharacterFormat.Size=19;sel.CharacterFormat.Bold=Microsoft.UI.Text.FormatEffect.On;}));
        ButtonFor(formatBar,"Normal",(_,_)=>VisualFormat(sel=>{sel.CharacterFormat.Size=17;sel.CharacterFormat.Bold=Microsoft.UI.Text.FormatEffect.Off;sel.CharacterFormat.Italic=Microsoft.UI.Text.FormatEffect.Off;}));
        ButtonFor(formatBar,"Bold",(_,_)=>VisualFormat(sel=>sel.CharacterFormat.Bold=Microsoft.UI.Text.FormatEffect.Toggle));
        ButtonFor(formatBar,"Italic",(_,_)=>VisualFormat(sel=>sel.CharacterFormat.Italic=Microsoft.UI.Text.FormatEffect.Toggle));
        ButtonFor(formatBar,"Bullets",(_,_)=>VisualFormat(sel=>sel.Text="• "+sel.Text));
        ButtonFor(formatBar,"Numbered",(_,_)=>VisualFormat(sel=>sel.Text=string.Join("\r",sel.Text.Split('\r').Select((line,i)=>(i+1)+". "+line))));
        ButtonFor(formatBar,"Scripture",(_,_)=>VisualFormat(sel=>{sel.Text="❯ "+sel.Text;sel.CharacterFormat.Italic=Microsoft.UI.Text.FormatEffect.On;}));
        ButtonFor(formatBar,"Undo",(_,_)=>{OutlineEdit();rich.Document.Undo();visualDirty=true;StoreVisual();});
        ButtonFor(formatBar,"Redo",(_,_)=>{OutlineEdit();rich.Document.Redo();visualDirty=true;StoreVisual();});
        ButtonFor(formatBar,"Markdown",(_,_)=>{StoreVisual();rich.Visibility=Visibility.Collapsed;previewScroll.Visibility=Visibility.Collapsed;Outline.Visibility=Visibility.Visible;previewToggle.Content="Visual editor";});
        void LocateSource(string passage){
            var query=System.Text.RegularExpressions.Regex.Replace(passage,@"^[#>*\s\d.]+","").Replace("**","").Trim();
            if(query.Length==0)return;
            var index=Transcript.Text.IndexOf(query,StringComparison.OrdinalIgnoreCase);
            if(index<0){var words=System.Text.RegularExpressions.Regex.Matches(query,@"\w+").Select(m=>m.Value).ToArray();for(int n=Math.Min(12,words.Length);n>=3&&index<0;n--){var pattern=string.Join(@"\W+",words.Take(n).Select(System.Text.RegularExpressions.Regex.Escape));var match=System.Text.RegularExpressions.Regex.Match(Transcript.Text,pattern,System.Text.RegularExpressions.RegexOptions.IgnoreCase);if(match.Success){index=match.Index;query=match.Value;}}}if(index<0){Status.Text="No matching source passage found.";return;}
            if(editorCards[0].Visibility!=Visibility.Visible){Status.Text="Open Sermon outlines or Full workspace to view the source.";return;}
            timedScroll.Visibility=Visibility.Collapsed;Transcript.Visibility=Visibility.Visible;timedToggle.Content="Timestamp view";
            Transcript.Focus(FocusState.Programmatic);Transcript.Select(index,query.Length);Status.Text="Source passage located.";
        }
        ButtonFor(formatBar,"Find source",(_,_)=>LocateSource(rich.Visibility==Visibility.Visible?rich.Document.Selection.Text:Outline.SelectedText));
        bool writingMode=false;
        Action? toggleFocus=null;
        ButtonFor(formatBar,"Focus mode",(_,_)=>toggleFocus?.Invoke());
        var scripturePattern=new System.Text.RegularExpressions.Regex(@"\b(?:[123]\s+)?(?:Genesis|Exodus|Leviticus|Numbers|Deuteronomy|Joshua|Judges|Ruth|Samuel|Kings|Chronicles|Ezra|Nehemiah|Esther|Job|Psalms?|Proverbs|Ecclesiastes|Song of Solomon|Isaiah|Jeremiah|Lamentations|Ezekiel|Daniel|Hosea|Joel|Amos|Obadiah|Jonah|Micah|Nahum|Habakkuk|Zephaniah|Haggai|Zechariah|Malachi|Matthew|Mark|Luke|John|Acts|Romans|Corinthians|Galatians|Ephesians|Philippians|Colossians|Thessalonians|Timothy|Titus|Philemon|Hebrews|James|Peter|Jude|Revelation)\s+\d+:\d+(?:[-–]\d+)?\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var verseCache=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        void AddScriptureRuns(TextBlock text,string content){
            int offset=0;
            foreach(System.Text.RegularExpressions.Match match in scripturePattern.Matches(content)){
                if(match.Index>offset)text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run{Text=content.Substring(offset,match.Index-offset)});
                var reference=match.Value;
                var link=new Microsoft.UI.Xaml.Documents.Hyperlink{NavigateUri=new Uri("https://www.biblegateway.com/passage/?search="+Uri.EscapeDataString(reference)+"&version=KJV")};
                link.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run{Text=reference,Foreground=gold});
                var host=new Microsoft.UI.Xaml.Documents.InlineUIContainer();
                var label=new TextBlock();label.Inlines.Add(link);
                var tip=new ToolTip{Content="KJV · Loading passage (internet required)…"};
                ToolTipService.SetToolTip(label,tip);
                tip.Opened+=async(_,_)=>{
                    if(verseCache.TryGetValue(reference,out var cached)){tip.Content=new TextBlock{Text=cached,TextWrapping=TextWrapping.Wrap,MaxWidth=420};return;}
                    try{
                        using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(12)};
                        var json=await client.GetStringAsync("https://bible-api.com/"+Uri.EscapeDataString(reference)+"?translation=kjv");
                        using var doc=System.Text.Json.JsonDocument.Parse(json);
                        var passage=doc.RootElement.GetProperty("text").GetString()?.Trim()??"Passage unavailable.";
                        var value=reference+" (KJV)\n\n"+passage;verseCache[reference]=value;tip.Content=new TextBlock{Text=value,TextWrapping=TextWrapping.Wrap,MaxWidth=420};
                    }catch{tip.Content="Passage unavailable. Click the reference to open KJV online.";}
                };
                host.Child=label;text.Inlines.Add(host);offset=match.Index+match.Length;
            }
            if(offset<content.Length)text.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run{Text=content.Substring(offset)});
        }
        void RenderOutline(){
            preview.Children.Clear();
            foreach(var raw in Outline.Text.Split('\n')){
                var line=raw.Trim();if(line.Length==0)continue;
                bool heading=line.StartsWith("#")||System.Text.RegularExpressions.Regex.IsMatch(line,@"^[IVXLCDM]+\.\s")||(line.Length<65&&line.Any(char.IsLetter)&&line==line.ToUpperInvariant());
                bool scripture=line.StartsWith(">");bool bullet=line.StartsWith("- ")||line.StartsWith("* ");
                var content=line.TrimStart('#','>',' ');if(bullet)content="• "+line.Substring(2);
                var text=new TextBlock{TextWrapping=TextWrapping.Wrap,FontFamily=new FontFamily("Georgia"),FontSize=heading?(line.StartsWith("###")?19:23):17,IsTextSelectionEnabled=true};
                if(heading||scripture){text.Foreground=gold;if(heading)text.FontWeight=Microsoft.UI.Text.FontWeights.SemiBold;}
                if(bullet||scripture)text.Margin=new Thickness(18,0,0,0);
                foreach(var part in System.Text.RegularExpressions.Regex.Split(content,@"(\*\*\*[^*]+\*\*\*|\*\*[^*]+\*\*|\*[^*]+\*)")){
                    if(part.Length==0)continue;
                    if(scripturePattern.IsMatch(part)){AddScriptureRuns(text,part.Trim('*'));continue;}
                    var run=new Microsoft.UI.Xaml.Documents.Run{Text=part.Trim('*')};
                    if(part.StartsWith("**")&&part.EndsWith("**"))run.FontWeight=Microsoft.UI.Text.FontWeights.Bold;
                    if((part.StartsWith("***")||!part.StartsWith("**"))&&part.StartsWith("*")&&part.EndsWith("*"))run.FontStyle=Windows.UI.Text.FontStyle.Italic;
                    text.Inlines.Add(run);
                }
                if(scripture)text.FontStyle=Windows.UI.Text.FontStyle.Italic;
                preview.Children.Add(text);
            }
            if(preview.Children.Count==0)preview.Children.Add(new TextBlock{Text="Write or generate an outline to preview its formatting.",TextWrapping=TextWrapping.Wrap});
        }
        previewToggle.Click+=(_,_)=>{
            if(previewScroll.Visibility==Visibility.Visible||Outline.Visibility==Visibility.Visible){OutlineEdit();return;}
            StoreVisual();rich.Visibility=Visibility.Collapsed;
            RenderOutline();previewScroll.Visibility=Visibility.Visible;Outline.Visibility=Visibility.Collapsed;previewToggle.Content="Visual editor";
        };
        Outline.TextChanged+=(_,_)=>{if(syncingOutline)return;if(rich.Visibility==Visibility.Visible)LoadVisual();if(previewScroll.Visibility==Visibility.Visible)RenderOutline();};
        Grid.SetColumn(divider,0); workspace.Children.Add(divider);
        editorCards[0].Margin=new Thickness(0,0,9,0);
        divider.ManipulationDelta += (_,e) => {
            var total=workspace.ColumnDefinitions[0].ActualWidth+workspace.ColumnDefinitions[1].ActualWidth;
            if(total<240)return;
            var left=Math.Clamp(workspace.ColumnDefinitions[0].ActualWidth+e.Delta.Translation.X,120,total-120);
            split=Math.Clamp(left/total,.2,.8);
            workspace.ColumnDefinitions[0].Width=new GridLength(split,GridUnitType.Star);
            workspace.ColumnDefinitions[1].Width=new GridLength(1-split,GridUnitType.Star);
        };
        divider.ManipulationCompleted+=(_,_)=>RememberWorkspace();
        void EditMode(){timedScroll.Visibility=Visibility.Collapsed;Transcript.Visibility=Visibility.Visible;timedToggle.Content="Timestamp view";}
        ButtonFor(search,"Undo",(_,_)=>{EditMode();if(Transcript.CanUndo)Transcript.Undo();});ButtonFor(search,"Redo",(_,_)=>{EditMode();if(Transcript.CanRedo)Transcript.Redo();});
        var matchCount=new TextBlock{VerticalAlignment=VerticalAlignment.Bottom};search.Children.Add(matchCount);
        var caseSensitive=new CheckBox{Content="Match case",MinWidth=0};search.Children.Add(caseSensitive);
        StringComparison Comparison()=>caseSensitive.IsChecked==true?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase;
        void FindMatch(bool previous){EditMode();var q=FindText.Text;var body=Transcript.Text;if(string.IsNullOrEmpty(q))return;int index=-1;if(previous){int start=Transcript.SelectionStart-1;if(start>=0&&body.Length>0)index=body.LastIndexOf(q,Math.Min(start,body.Length-1),Comparison());if(index<0)index=body.LastIndexOf(q,Comparison());}else{index=body.IndexOf(q,Math.Min(body.Length,Transcript.SelectionStart+Transcript.SelectionLength),Comparison());if(index<0)index=body.IndexOf(q,Comparison());}if(index>=0){Transcript.Focus(FocusState.Programmatic);Transcript.Select(index,q.Length);Status.Text="Passage found.";}else Status.Text="Phrase not found.";}
        ButtonFor(search,"Previous",(_,_)=>FindMatch(true));ButtonFor(search,"Next",(_,_)=>FindMatch(false));
        ButtonFor(search,"Replace",(_,_)=>{EditMode();if(!string.IsNullOrEmpty(FindText.Text)&&string.Equals(Transcript.SelectedText,FindText.Text,Comparison())){int start=Transcript.SelectionStart;Transcript.SelectedText=Replacement.Text;Transcript.Select(start+Replacement.Text.Length,0);}FindMatch(false);});
        ButtonFor(search,"Replace all",(_,_)=>{EditMode();if(!string.IsNullOrEmpty(FindText.Text)){Transcript.Text=Transcript.Text.Replace(FindText.Text,Replacement.Text,Comparison());Status.Text="Matching passages replaced.";}});
        void CountMatches(){var q=FindText.Text;matchCount.Text=string.IsNullOrWhiteSpace(q)?"":System.Text.RegularExpressions.Regex.Matches(Transcript.Text,System.Text.RegularExpressions.Regex.Escape(q),(caseSensitive.IsChecked==true?System.Text.RegularExpressions.RegexOptions.None:System.Text.RegularExpressions.RegexOptions.IgnoreCase)).Count+" matches";}
        caseSensitive.Checked+=(_,_)=>CountMatches();caseSensitive.Unchecked+=(_,_)=>CountMatches();
        var matchTimer=DispatcherQueue.CreateTimer();matchTimer.Interval=TimeSpan.FromMilliseconds(250);matchTimer.IsRepeating=false;matchTimer.Tick+=(_,_)=>CountMatches();
        void QueueMatches(){matchTimer.Stop();matchTimer.Start();}
        FindText.TextChanged+=(_,_)=>QueueMatches();Transcript.TextChanged+=(_,_)=>QueueMatches();Closed+=(_,_)=>matchTimer.Stop();
        FindText.KeyDown+=(_,e)=>{if(e.Key==Windows.System.VirtualKey.Enter){if(timedScroll.Visibility==Visibility.Visible){timedScroll.Visibility=Visibility.Collapsed;Transcript.Visibility=Visibility.Visible;timedToggle.Content="Timestamp view";}FindMatch(false);e.Handled=true;}};
        Transcript.TextChanged += Editor_TextChanged; Outline.TextChanged += Outline_TextChanged; SermonTitle.TextChanged += Editor_TextChanged;
        App.Log("Building interface: library");
        var side = new StackPanel { Spacing = 8 };
        var scroll = new ScrollViewer { Content = Card(side) }; Grid.SetColumn(scroll,2); workspace.Children.Add(scroll);
        side.Children.Add(new TextBlock { Text = "Sermon library", FontSize = 20 });
        var libraryHint=new TextBlock{Text="Select a saved sermon to open it. New sermons appear here after you save.",TextWrapping=TextWrapping.Wrap,Opacity=.75};side.Children.Add(libraryHint);
        _libraryStats=new TextBlock{TextWrapping=TextWrapping.Wrap,FontSize=17};side.Children.Add(_libraryStats);
        _recentSermons=new ComboBox{Header="Recently opened sermons",DisplayMemberPath="LibraryHeading",HorizontalAlignment=HorizontalAlignment.Stretch};side.Children.Add(_recentSermons);
        _librarySearch=new TextBox{PlaceholderText="Search sermons, scripture, tags…"};side.Children.Add(_librarySearch);
        _librarySort=new ComboBox{Header="Sort by",HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var label in new[]{"Newest sermon","Oldest sermon","Title A–Z","Recently edited"})_librarySort.Items.Add(label);_librarySort.SelectedIndex=0;side.Children.Add(_librarySort);
        _libraryFavorites=new CheckBox{Content="Favorites only"};side.Children.Add(_libraryFavorites);
        _libraryCount=new TextBlock{Opacity=.75};side.Children.Add(_libraryCount);
        LibraryList=new ListView{MinHeight=180,SelectionMode=ListViewSelectionMode.Single};
        LibraryList.ItemTemplate=(DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""<DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><Border Padding="12" Margin="0,4" CornerRadius="8" BorderThickness="1" BorderBrush="{ThemeResource SystemControlForegroundBaseLowBrush}"><StackPanel Spacing="5"><TextBlock Text="{Binding LibraryHeading}" FontSize="19" FontWeight="SemiBold" TextWrapping="Wrap"/><TextBlock Text="{Binding LibraryDetails}" Opacity="0.8" TextWrapping="Wrap"/><TextBlock Text="{Binding LibraryPreview}" Opacity="0.7" TextWrapping="Wrap" MaxLines="3"/></StackPanel></Border></DataTemplate>""");
        LibraryList.SelectionChanged+=Library_SelectionChanged;side.Children.Add(LibraryList);
        _librarySearch.TextChanged+=(_,_)=>RefreshLibrary();_librarySort.SelectionChanged+=(_,_)=>RefreshLibrary();_libraryFavorites.Checked+=(_,_)=>RefreshLibrary();_libraryFavorites.Unchecked+=(_,_)=>RefreshLibrary();
        var filters=new StackPanel{Spacing=6};
        _filterSpeaker=Field(filters,"Speaker","",220);_filterTag=Field(filters,"Tag","",220);_filterSeries=Field(filters,"Series","",220);_filterCollection=Field(filters,"Collection","",220);
        _filterFrom=new CalendarDatePicker{Header="From date"};_filterTo=new CalendarDatePicker{Header="Through date"};filters.Children.Add(_filterFrom);filters.Children.Add(_filterTo);
        foreach(var field in new[]{_filterSpeaker,_filterTag,_filterSeries,_filterCollection})field.TextChanged+=(_,_)=>RefreshLibrary();
        _filterFrom.DateChanged+=(_,_)=>RefreshLibrary();_filterTo.DateChanged+=(_,_)=>RefreshLibrary();
        ButtonFor(filters,"Clear filters",(_,_)=>{_filterSpeaker.Text=_filterTag.Text=_filterSeries.Text=_filterCollection.Text=_librarySearch.Text="";_filterFrom.Date=null;_filterTo.Date=null;_libraryFavorites.IsChecked=false;RefreshLibrary();});
        var filterPanel=new Expander{Header="Advanced filters",Content=filters,HorizontalAlignment=HorizontalAlignment.Stretch};side.Children.Insert(side.Children.IndexOf(LibraryList),filterPanel);
        Action? openLibrarySermon=null;
        _recentSermons.SelectionChanged+=(_,_)=>{if(_loading||_recentSermons.SelectedItem is not SermonRecord record)return;_librarySearch.Text="";_filterSpeaker.Text=_filterTag.Text=_filterSeries.Text=_filterCollection.Text="";_filterFrom.Date=null;_filterTo.Date=null;_libraryFavorites.IsChecked=false;RefreshLibrary();LibraryList.SelectedItem=record;openLibrarySermon?.Invoke();};
        var manage=new StackPanel{Spacing=6};side.Children.Add(manage);
        ButtonFor(manage,"Rename selected",(_,_)=>ManageSermon("Rename"));ButtonFor(manage,"Duplicate selected",(_,_)=>ManageSermon("Duplicate"));ButtonFor(manage,"Delete selected",(_,_)=>ManageSermon("Delete"));
        var archivePanel=new StackPanel{Spacing=6};ButtonFor(archivePanel,"Export all sermons",(_,_)=>BulkArchive(false));ButtonFor(archivePanel,"Export filtered sermons",(_,_)=>BulkArchive(true));ButtonFor(archivePanel,"Backup library",Backup_Click);ButtonFor(archivePanel,"Restore library",Restore_Click);
        side.Children.Add(new Expander{Header="Archive and backup",Content=archivePanel,HorizontalAlignment=HorizontalAlignment.Stretch});
        ButtonFor(side,"Open selected sermon",(_,_)=>{if(LibraryList.SelectedItem is SermonRecord)openLibrarySermon?.Invoke();else Status.Text="Select a sermon first.";});
        var settings=new StackPanel{Spacing=12};
        var settingsScroll=new ScrollViewer{Content=Card(settings),Visibility=Visibility.Collapsed};Grid.SetColumn(settingsScroll,2);workspace.Children.Add(settingsScroll);
        settings.Children.Add(new TextBlock{Text="Transcription and local AI",FontSize=24,FontFamily=new FontFamily("Georgia")});
        OllamaModel = Field(settings,"Ollama model","llama3.2:3b",210);
        var aiStatus=new TextBlock{Text="Optional local AI. Model downloads may require several GB.",TextWrapping=TextWrapping.Wrap};
        var setup=new Button{Content="Set up / check Ollama"};
        var cancelSetup=new Button{Content="Cancel setup",IsEnabled=false};
        System.Threading.CancellationTokenSource? setupCancel=null;
        settings.Children.Add(setup);settings.Children.Add(cancelSetup);settings.Children.Add(aiStatus);
        cancelSetup.Click+=(_,_)=>setupCancel?.Cancel();
        setup.Click+=async(_,_)=>{
            setup.IsEnabled=false;cancelSetup.IsEnabled=true;setupCancel=new();
            var token=setupCancel.Token;
            try{
                var model=OllamaModel.Text.Trim();
                if(!System.Text.RegularExpressions.Regex.IsMatch(model,@"^[a-zA-Z0-9_.-]+(:[a-zA-Z0-9_.-]+)?$")||model.Contains("cloud",StringComparison.OrdinalIgnoreCase))throw new Exception("Choose a local model, such as llama3.2:3b.");
                using var local=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(3)};
                async Task<bool> Ready(){try{using var response=await local.GetAsync("http://127.0.0.1:11434/api/version",token);return response.IsSuccessStatusCode;}catch when(!token.IsCancellationRequested){return false;}}
                if(!await Ready()){
                    var exe=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Ollama","ollama.exe");
                    if(!File.Exists(exe)){
                        aiStatus.Text="Opening official installer download. Complete installation, then select setup again.";
                        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://ollama.com/download/windows"));return;
                    }
                    var start=new System.Diagnostics.ProcessStartInfo(exe,"serve"){UseShellExecute=false,CreateNoWindow=true};start.Environment["OLLAMA_HOST"]="127.0.0.1:11434";start.Environment["OLLAMA_NO_CLOUD"]="1";
                    using var process=System.Diagnostics.Process.Start(start);
                    aiStatus.Text="Starting local service…";
                    for(int i=0;i<30&&!await Ready();i++)await Task.Delay(1000,token);
                    if(!await Ready())throw new Exception("Start Ollama from the Start menu and retry.");
                }
                using var client=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=System.Threading.Timeout.InfiniteTimeSpan};
                using var show=await client.PostAsync("http://127.0.0.1:11434/api/show",System.Net.Http.Json.JsonContent.Create(new{model}),token);
                if(!show.IsSuccessStatusCode){
                    aiStatus.Text="Downloading "+model+"…";
                    using var request=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:11434/api/pull"){Content=System.Net.Http.Json.JsonContent.Create(new{model,stream=true})};
                    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
                    using var reader=new StreamReader(await response.Content.ReadAsStreamAsync(token));bool success=false;
                    while(await reader.ReadLineAsync(token) is string line){using var doc=System.Text.Json.JsonDocument.Parse(line);var obj=doc.RootElement;if(obj.TryGetProperty("error",out var error))throw new Exception(error.GetString());if(obj.TryGetProperty("status",out var state)){aiStatus.Text=state.GetString()??"Downloading";success=aiStatus.Text=="success";}if(obj.TryGetProperty("total",out var total)&&obj.TryGetProperty("completed",out var done)&&total.GetDouble()>0)aiStatus.Text+=" · "+(done.GetDouble()/total.GetDouble()).ToString("P0");}
                    if(!success)throw new Exception("Model download did not finish.");
                }
                aiStatus.Text="Ready: "+model+" is available locally.";
            }catch(OperationCanceledException){aiStatus.Text="Setup canceled.";}catch(Exception ex){aiStatus.Text="Setup failed: "+ex.Message;}finally{setupCancel.Dispose();setupCancel=null;setup.IsEnabled=true;cancelSetup.IsEnabled=false;}
        };
        WhisperPath = Field(settings,"Whisper CLI executable","",210);
        WhisperModel = Field(settings,"GGML model file","",210);
        FfmpegPath = Field(settings,"FFmpeg executable","",210);
        ButtonFor(settings,"Backup archive",Backup_Click); ButtonFor(settings,"Restore archive",Restore_Click);
        side.Children.Add(new TextBlock { Text = "Scripture references" });
        ScriptureResults = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }; side.Children.Add(ScriptureResults);
        ConfigureTools(settings);
        Status = new TextBlock { Text = "Ready", TextWrapping = TextWrapping.Wrap };
        var footer=new Grid{ColumnSpacing=12};footer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
        var counts=new TextBlock{Opacity=.75};footer.Children.Add(Status);
        var footerRight=new StackPanel{Orientation=Orientation.Horizontal,Spacing=16};_saveIndicator=new TextBlock{Text="Unsaved draft",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};footerRight.Children.Add(_saveIndicator);footerRight.Children.Add(counts);Grid.SetColumn(footerRight,1);footer.Children.Add(footerRight);
        void CountWords(){counts.Text=$"{System.Text.RegularExpressions.Regex.Matches(Transcript.Text,@"\S+").Count:N0} transcript words  ·  {System.Text.RegularExpressions.Regex.Matches(Outline.Text,@"\S+").Count:N0} outline words";}
        var countTimer=DispatcherQueue.CreateTimer();countTimer.Interval=TimeSpan.FromMilliseconds(300);countTimer.IsRepeating=false;countTimer.Tick+=(_,_)=>CountWords();
        void QueueCount(){countTimer.Stop();countTimer.Start();}
        Transcript.TextChanged+=(_,_)=>QueueCount();Outline.TextChanged+=(_,_)=>QueueCount();CountWords();Closed+=(_,_)=>countTimer.Stop();
        Grid.SetRow(footer,3);root.Children.Add(footer);
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
        Stroke(4,25,24,19);Stroke(24,19,44,28);Stroke(44,28,64,19);Stroke(64,19,84,25);Stroke(84,25,84,61);Stroke(84,61,64,55);Stroke(64,55,44,65);Stroke(44,65,24,55);Stroke(24,55,4,61);Stroke(4,61,4,25);Stroke(44,28,44,65);Stroke(44,2,44,24);Stroke(34,10,54,10);Stroke(13,34,20,31);Stroke(20,31,36,39);Stroke(13,44,20,41);Stroke(20,41,36,49);Stroke(52,39,68,31);Stroke(68,31,75,34);Stroke(52,49,68,41);Stroke(68,41,75,44);sidebar.Children.Add(logo);
        sidebar.Children.Add(new TextBlock { Text = "Sermon Studio", TextAlignment=TextAlignment.Center, FontFamily=new FontFamily("Georgia"), FontSize = 26, Foreground = new SolidColorBrush(Microsoft.UI.Colors.White), Margin = new Thickness(0,12,0,20) });
        var pageActions=new Dictionary<string,Action>();
        void Navigate(string name, Action action) {
            var navContent=new StackPanel{Orientation=Orientation.Horizontal,Spacing=10};navContent.Children.Add(new FontIcon{Glyph=name switch {"New sermon"=>"\uE710","Transcript editor"=>"\uE70F","Sermon outlines"=>"\uE8A5","Sermon library"=>"\uE8B7","Settings"=>"\uE713","Scripture references"=>"\uE82D","Transcribe audio"=>"\uE720",_=>"\uE8A9"},FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=18});navContent.Children.Add(new TextBlock{Text=name});
            var button = new Button { Content = navContent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment=HorizontalAlignment.Left, Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent), Foreground=new SolidColorBrush(Microsoft.UI.Colors.White), BorderThickness=new Thickness(0), CornerRadius=new CornerRadius(8), Padding=new Thickness(12,12,12,12) };navButtons.Add(button);Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,name);
            void OpenPage(){ foreach(var b in navButtons){b.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);b.Foreground=new SolidColorBrush(Microsoft.UI.Colors.White);}button.Background=gold;button.Foreground=navy;PageTitle.Text = name; tools.Visibility=Visibility.Visible; action(); SetToolbar(name); if(name!="New sermon"){preferredPage=name;RememberWorkspace();} }
            pageActions[name]=OpenPage;button.Click+=(_,_)=>OpenPage();
            sidebar.Children.Add(button);
        }
        void SetToolbar(string page){
            ((FrameworkElement)tools.Children[0]).Visibility=page=="Settings"||page=="Sermon library"?Visibility.Collapsed:Visibility.Visible;
            ((FrameworkElement)tools.Children[1]).Visibility=page=="Sermon outlines"||page=="Full workspace"||page=="New sermon"?Visibility.Visible:Visibility.Collapsed;
            ((FrameworkElement)tools.Children[2]).Visibility=page=="Transcribe audio"||page=="Full workspace"||page=="New sermon"?Visibility.Visible:Visibility.Collapsed;
            ((FrameworkElement)tools.Children[3]).Visibility=page=="Transcript editor"||page=="Full workspace"?Visibility.Visible:Visibility.Collapsed;
        }
        void Show(bool transcript, bool outline, bool library) {
            writingMode=false;
            settingsScroll.Visibility=Visibility.Collapsed;
            editorCards[0].Visibility=transcript?Visibility.Visible:Visibility.Collapsed;
            editorCards[1].Visibility=outline?Visibility.Visible:Visibility.Collapsed;
            divider.Visibility=transcript && outline?Visibility.Visible:Visibility.Collapsed;
            timedScroll.Visibility=Visibility.Collapsed;previewScroll.Visibility=Visibility.Collapsed;timedToggle.Content="Timestamp view";previewToggle.Content="Formatted view";
            Transcript.Visibility = transcript ? Visibility.Visible : Visibility.Collapsed;
            Outline.Visibility=Visibility.Collapsed;rich.Visibility=Visibility.Collapsed;if(outline)OutlineEdit();
            scroll.Visibility = library ? Visibility.Visible : Visibility.Collapsed;
            workspace.ColumnDefinitions[0].Width = transcript ? new GridLength(outline?split:1, GridUnitType.Star) : new GridLength(0);
            workspace.ColumnDefinitions[1].Width = outline ? new GridLength(transcript?1-split:1, GridUnitType.Star) : new GridLength(0);
            workspace.ColumnDefinitions[2].Width = library ? new GridLength(transcript || outline ? 230 : 1, transcript || outline ? GridUnitType.Pixel : GridUnitType.Star) : new GridLength(0);
        }
        Navigate("New sermon", () => { New_Click(this, null!); Show(true,true,true); });
        Navigate("Sermon library", () => Show(false,false,true));
        Navigate("Transcribe audio",()=>Show(true,false,false));
        Navigate("Transcript editor", () => Show(true,false,false));
        Navigate("Sermon outlines", () => Show(true,true,false));
        Navigate("Scripture references",()=>{Show(true,false,true);Scripture_Click(this,null!);});
        Navigate("Full workspace", () => Show(true,true,true));
        openLibrarySermon=()=>pageActions["Sermon outlines"]();
        Navigate("Settings", () => { Show(false,false,true); scroll.Visibility=Visibility.Collapsed;settingsScroll.Visibility=Visibility.Visible;tools.Visibility=Visibility.Collapsed; });
        var theme = new ComboBox { Header="Appearance" }; theme.Items.Add("System");theme.Items.Add("Light");theme.Items.Add("Dark");theme.SelectedIndex=0;
        var settingsPath=Path.Combine(Path.GetDirectoryName(_libraryPath)!,"appearance.txt");
        try { if(File.Exists(settingsPath)&&int.TryParse(File.ReadAllText(settingsPath),out var saved)&&saved>=0&&saved<=2)theme.SelectedIndex=saved; }catch{}
        void ApplyTheme(){
            shell.RequestedTheme=theme.SelectedIndex==1?ElementTheme.Light:theme.SelectedIndex==2?ElementTheme.Dark:ElementTheme.Default;
            Paint();
        }
        void Paint(){
            bool dark=shell.ActualTheme==ElementTheme.Dark;
            if(new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast){root.ClearValue(Grid.BackgroundProperty);foreach(var c in cards){c.ClearValue(Border.BackgroundProperty);c.ClearValue(Border.BorderBrushProperty);}foreach(var h in headings)h.ClearValue(TextBlock.ForegroundProperty);return;}
            root.Background=dark?Brush(16,23,32):Brush(242,239,232);
            foreach(var c in cards){c.Background=dark?Brush(25,34,45):Brush(255,252,245);c.BorderBrush=dark?Brush(62,76,92):Brush(213,207,194);}
            foreach(var h in headings)h.Foreground=dark?Brush(238,220,173):navy;
        }
        shell.ActualThemeChanged+=(_,_)=>Paint();
        ApplyTheme();
        theme.SelectionChanged += (_,_)=>{ApplyTheme();try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);File.WriteAllText(settingsPath,theme.SelectedIndex.ToString());}catch{}};
        sidebar.RequestedTheme=ElementTheme.Dark;
        var appearancePanel=new StackPanel{Spacing=10};appearancePanel.Children.Add(theme);
        var fontChoice=new ComboBox{Header="Editor text size"};foreach(var size in new[]{14,17,20,23,26})fontChoice.Items.Add(size);fontChoice.SelectedIndex=1;
        var fontPath=Path.Combine(Path.GetDirectoryName(_libraryPath)!,"editor-size.txt");
        try{if(File.Exists(fontPath)&&int.TryParse(File.ReadAllText(fontPath),out var savedSize)&&fontChoice.Items.Contains(savedSize))fontChoice.SelectedItem=savedSize;}catch{}
        void ApplyEditorSize(){if(fontChoice.SelectedItem is int size){Transcript.FontSize=size;Outline.FontSize=size;rich.FontSize=size;}}
        ApplyEditorSize();fontChoice.SelectionChanged+=(_,_)=>{ApplyEditorSize();try{Directory.CreateDirectory(Path.GetDirectoryName(fontPath)!);File.WriteAllText(fontPath,fontChoice.SelectedItem?.ToString()??"17");}catch{}};
        appearancePanel.Children.Add(fontChoice);
        settings.Children.Insert(0,new Expander{Header="Appearance and accessibility",Content=appearancePanel,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch});
        var updatePanel=new StackPanel{Spacing=8};
        var autoCheck=new CheckBox{Content="Check for updates on startup",IsChecked=false};
        var updatePrefs=Path.Combine(Path.GetDirectoryName(_libraryPath)!,"auto-update-check.txt");
        try{autoCheck.IsChecked=File.Exists(updatePrefs)&&File.ReadAllText(updatePrefs)=="1";}catch{}
        void SaveUpdatePreference(){try{Directory.CreateDirectory(Path.GetDirectoryName(updatePrefs)!);File.WriteAllText(updatePrefs,autoCheck.IsChecked==true?"1":"0");}catch{}}
        autoCheck.Checked+=(_,_)=>SaveUpdatePreference();autoCheck.Unchecked+=(_,_)=>SaveUpdatePreference();
        updatePanel.Children.Add(autoCheck);_updateNotice=new TextBlock{Text="Updates require your approval.",TextWrapping=TextWrapping.Wrap};updatePanel.Children.Add(_updateNotice);
        ButtonFor(updatePanel,"Check now",Updates_Click);
        settings.Children.Add(new Expander{Header="Updates",Content=updatePanel,IsExpanded=true,HorizontalAlignment=HorizontalAlignment.Stretch});
        var about=new StackPanel{Spacing=8};var tagPath=Path.Combine(AppContext.BaseDirectory,"build-tag.txt");string build="Development build";try{if(File.Exists(tagPath))build=File.ReadAllText(tagPath).Trim();}catch{}
        about.Children.Add(new TextBlock{Text="Sermon Studio · Windows\n"+build,TextWrapping=TextWrapping.Wrap});
        about.Children.Add(new TextBlock{Text="Local library: "+_libraryPath,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true});
        ButtonFor(about,"Open library folder",(_,_)=>{try{var folder=Path.GetDirectoryName(_libraryPath)!;Directory.CreateDirectory(folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){Status.Text=ex.Message;}});
        about.Children.Add(new TextBlock{Text="Ctrl+S Save · Ctrl+F Find · Ctrl+L Library · F11 Full screen · Esc Exit focus",TextWrapping=TextWrapping.Wrap});
        settings.Children.Add(new Expander{Header="About and keyboard shortcuts",Content=about,HorizontalAlignment=HorizontalAlignment.Stretch});
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Transcript,"Sermon transcript editor");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(rich,"Visual sermon outline editor");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FindText,"Find transcript text");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Replacement,"Replacement text");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LibraryList,"Saved sermons");
        if(preferredPage=="New sermon"||preferredPage=="Scripture references"||!pageActions.ContainsKey(preferredPage))preferredPage="Sermon outlines";
        pageActions[preferredPage]();
        var sidebarScroll=new ScrollViewer{Content=sidebar,Background=navy,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};shell.Children.Add(sidebarScroll); Grid.SetColumn(root,1); shell.Children.Add(root);
        shell.SizeChanged+=(_,e)=>{if(!writingMode){shell.ColumnDefinitions[0].Width=new GridLength(e.NewSize.Width<900?180:220);root.Padding=new Thickness(e.NewSize.Width<900?10:20);}};
        toggleFocus=()=>{
            if(writingMode){writingMode=false;sidebarScroll.Visibility=Visibility.Visible;shell.ColumnDefinitions[0].Width=new GridLength(220);PageTitle.Visibility=Visibility.Visible;pageActions[preferredPage]();return;}
            writingMode=true;sidebarScroll.Visibility=Visibility.Collapsed;shell.ColumnDefinitions[0].Width=new GridLength(0);PageTitle.Visibility=Visibility.Collapsed;tools.Visibility=Visibility.Collapsed;scroll.Visibility=Visibility.Collapsed;editorCards[0].Visibility=Visibility.Collapsed;divider.Visibility=Visibility.Collapsed;
            workspace.ColumnDefinitions[0].Width=new GridLength(0);workspace.ColumnDefinitions[1].Width=new GridLength(1,GridUnitType.Star);workspace.ColumnDefinitions[2].Width=new GridLength(0);
        };
        var fullScreenButton = new Button { Content = "Full screen (F11)", HorizontalAlignment = HorizontalAlignment.Stretch };
        void ToggleFullScreen()
        {
            try {
                bool full = AppWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen;
                AppWindow.SetPresenter(full ? Microsoft.UI.Windowing.AppWindowPresenterKind.Default : Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                if(full && AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter normal) normal.Maximize();
                fullScreenButton.Content = full ? "Full screen (F11)" : "Exit full screen (F11)";
            } catch(Exception ex) { Status.Text = "Window mode: " + ex.Message; }
        }
        fullScreenButton.Click += (_,_) => ToggleFullScreen();
        sidebar.Children.Add(fullScreenButton);
        var shortcut = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.F11 };
        shortcut.Invoked += (_,e) => { ToggleFullScreen(); e.Handled = true; };
        shell.KeyboardAccelerators.Add(shortcut);
        void Key(Windows.System.VirtualKey key,Windows.System.VirtualKeyModifiers modifiers,Action action){
            var accelerator=new Microsoft.UI.Xaml.Input.KeyboardAccelerator{Key=key,Modifiers=modifiers};
            accelerator.Invoked+=(_,e)=>{action();e.Handled=true;};shell.KeyboardAccelerators.Add(accelerator);
        }
        Key(Windows.System.VirtualKey.S,Windows.System.VirtualKeyModifiers.Control,()=>Save());
        Key(Windows.System.VirtualKey.F,Windows.System.VirtualKeyModifiers.Control,()=>{if(writingMode)toggleFocus?.Invoke();pageActions["Transcript editor"]();FindText.Focus(FocusState.Programmatic);});
        Key(Windows.System.VirtualKey.L,Windows.System.VirtualKeyModifiers.Control,()=>{if(writingMode)toggleFocus?.Invoke();pageActions["Sermon library"]();_librarySearch?.Focus(FocusState.Programmatic);});
        Key(Windows.System.VirtualKey.Escape,Windows.System.VirtualKeyModifiers.None,()=>{if(writingMode)toggleFocus?.Invoke();});
        shell.Loaded+=async(_,_)=>{if(autoCheck.IsChecked==true)await CheckUpdatesAsync(false);};
        Content = shell;
        shell.Loaded += (_,_) => {
            try { if(AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Maximize(); }
            catch(Exception ex) { App.Log("Maximize: " + ex.Message); }
        };
    }
    private void OnFirstLayout(object? sender, object e)
    {
        if (sender is FrameworkElement root) root.LayoutUpdated -= OnFirstLayout;
        App.Log("First layout completed");
    }
}
