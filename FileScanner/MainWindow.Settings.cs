using System.Diagnostics;
using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace FileScanner;

// Settings menu in the title bar: theme and language, saved to settings.json on every change.
public sealed partial class MainWindow
{
    readonly Settings _settings;

    void InitSettings()
    {
        (_settings.Theme switch { AppTheme.Light => ThemeLight, AppTheme.Dark => ThemeDark, _ => ThemeSystem }).IsChecked = true;
        (_settings.Language == "es" ? LanguageSpanish : LanguageEnglish).IsChecked = true;
        ApplyTheme();
        // Fires when Windows switches light/dark or high contrast. Subscribed after InitMap: its handler updates
        // _highContrast before this one runs.
        _uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplyTheme);
    }

    void Theme_Click(object sender, RoutedEventArgs e)
    {
        var item = (RadioMenuFlyoutItem)sender;
        _settings.Theme = item == ThemeLight ? AppTheme.Light : item == ThemeDark ? AppTheme.Dark : AppTheme.System;
        SaveSettings();
        ApplyTheme();
    }

    // Texts are read once when the window loads, so a new language needs a restart.
    void Language_Click(object sender, RoutedEventArgs e)
    {
        _settings.Language = (RadioMenuFlyoutItem)sender == LanguageSpanish ? "es" : "en";
        SaveSettings();
        var culture = _settings.Culture;
        if (culture.TwoLetterISOLanguageName == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) return;
        // In the chosen language: it is the one the user reads.
        var texts = Strings.Settings.ResourceManager;
        ShowStatus(InfoBarSeverity.Informational, texts.GetString(nameof(Strings.Settings.RestartToApply), culture)!,
            texts.GetString(nameof(Strings.Settings.RestartNow), culture), Restart);
    }

    void SaveSettings()
    {
        try
        {
            _settings.Save(Settings.DefaultFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Warning, string.Format(CultureInfo.CurrentCulture, Strings.Settings.SaveFailed, ex.Message));
        }
    }

    // Same arguments as this launch. The new instance starts before this one saves the scan copy: if it loads
    // the previous copy, its rescan at startup brings it up to date.
    async void Restart()
    {
        try
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!);
            foreach (var arg in Environment.GetCommandLineArgs().Skip(1)) start.ArgumentList.Add(arg);
            Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, string.Format(CultureInfo.CurrentCulture, Strings.Settings.RestartFailed, ex.Message));
            return;
        }
        if (StartClosing() is { } save) await save;
        Close();
    }

    // In high contrast the system theme wins: forcing Light or Dark would put its text on the wrong background.
    void ApplyTheme()
    {
        bool dark = _settings.Theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            // The Windows app mode: Background is black in dark mode and white in light mode.
            _ => _uiSettings.GetColorValue(UIColorType.Background).R < 128,
        };
        (Root.RequestedTheme, AppWindow.TitleBar.PreferredTheme) = (_highContrast, dark) switch
        {
            (true, _) => (ElementTheme.Default, TitleBarTheme.UseDefaultAppMode),
            (_, true) => (ElementTheme.Dark, TitleBarTheme.Dark),
            _ => (ElementTheme.Light, TitleBarTheme.Light),
        };
        _backdrop.IsDark = dark;
        _gap.Color = GapColor(dark);
        // The rings read Root.ActualTheme for the border of marked sectors.
        Render();
    }
}
