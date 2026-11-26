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
            var log = Path.Combine(folder,"startup.log");
            // Keep the diagnostic log small. It never contains sermon text, only events and exception types.
            var info = new FileInfo(log);
            if (info.Exists && info.Length > 1_000_000) File.Move(log, log + ".old", true);
            File.AppendAllText(log, DateTime.Now.ToString("O")+" "+message+Environment.NewLine);
        } catch {}
    }
    public App()
    {
        Log("App constructor");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("Fatal: "+e.ExceptionObject);
        UnhandledException += (_, e) => { Log("XAML exception: "+e.Exception); (_window as MainWindow)?.EmergencyRecovery(); };
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
