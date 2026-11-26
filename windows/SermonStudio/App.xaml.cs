using Microsoft.UI.Xaml;
namespace SermonStudio;
public partial class App : Application
{
    private Window? _window;
    internal static void Log(string message)
    {
        try {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SermonStudio");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder,"startup.log"), DateTime.Now.ToString("O")+" "+message+Environment.NewLine);
        } catch {}
    }
    public App()
    {
        Log("App constructor");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("Fatal: "+e.ExceptionObject);
        UnhandledException += (_, e) => Log("XAML exception: "+e.Exception);
        try { InitializeComponent(); } catch(Exception ex) { Log(ex.ToString()); throw; }
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try {
            if (Environment.GetCommandLineArgs().Contains("--safe-mode"))
            {
                Log("Safe mode: creating minimal window");
                _window = new Window { Title = "Sermon Studio diagnostics" };
                var text = new Microsoft.UI.Xaml.Controls.TextBlock { Text = "WinUI diagnostic window — safe mode", FontSize = 24, Margin = new Thickness(24) };
                text.Loaded += (_, _) => Log("Safe mode content loaded");
                _window.Content = text;
                _window.Activate();
                Log("Safe mode activated");
                return;
            }
            Log("Creating MainWindow");
            _window = new MainWindow();
            _window.Activate();
            Log("MainWindow activated");
        } catch(Exception ex) { Log(ex.ToString()); throw; }
    }
}
