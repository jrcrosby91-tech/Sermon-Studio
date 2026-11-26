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
            Log("Creating MainWindow");
            _window = new MainWindow();
            _window.Activate();
            Log("MainWindow activated");
        } catch(Exception ex) { Log(ex.ToString()); throw; }
    }
}
