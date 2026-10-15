using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml;

namespace FileScanner;

public partial class App : Application
{
    readonly Settings _settings = Settings.Load(Settings.DefaultFile);
    // "--demo": shows DemoData instead of scanning, for screenshots. Read from the command line so it is set
    // before any static field that depends on it.
    public static readonly bool Demo = Environment.GetCommandLineArgs().Contains("--demo");
    Window? _window;

    public App()
    {
        // Before any window or text exists: the generated Strings classes read CurrentUICulture on each lookup,
        // and background threads take the Default* cultures.
        var culture = _settings.Culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        // WinUI's own texts (text box context menu, InfoBar close button) follow this one, not CurrentUICulture.
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture.Name;
        InitializeComponent();
        UnhandledException += LogCrash;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The demo tree must point nowhere: if its home exists here, file reads in the tabs would list real files.
        if (Demo && Directory.Exists(DemoData.Home))
        {
            Exit();
            return;
        }
        // The Explorer context menu (added by the installer) launches the app with the folder as an argument.
        _window = new MainWindow(_settings, ScanScope.LaunchFolder(Environment.GetCommandLineArgs().Skip(1)));
        _window.Activate();
    }

    // An unhandled exception closes the app and Windows only logs "0xc000027b" without saying which one.
    // The full exception goes to %LOCALAPPDATA%\FileScanner\crash.log. It is not marked handled,
    // so the app still closes; the log is there to find and fix the cause.
    static void LogCrash(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileScanner");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{e.Exception}\n\n");
    }
}
