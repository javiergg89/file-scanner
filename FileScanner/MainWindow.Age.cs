using System.Globalization;
using FileScanner.Core;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace FileScanner;

// Age coloring: each folder gets the AgeBands band of the newest change inside it.
// A large folder untouched for years stands out.
public sealed partial class MainWindow
{
    // One color per AgeBands band, newest to oldest. No green: it marks what is selected for deletion.
    static readonly (string Label, Color Color)[] Ages =
    [
        (Strings.Map.AgeUnderMonth, Color.FromArgb(255, 0x4E, 0x79, 0xA7)),
        (Strings.Map.AgeMonths, Color.FromArgb(255, 0x8B, 0x7C, 0xF6)),
        (Strings.Map.AgeYear, Color.FromArgb(255, 0xED, 0xC9, 0x48)),
        (Strings.Map.AgeYears, Color.FromArgb(255, 0xF2, 0x8E, 0x2B)),
        (Strings.Map.AgeOverYears, Color.FromArgb(255, 0xE1, 0x57, 0x59)),
    ];

    bool AgeColors => ColorModeBox.SelectedIndex == 1;

    // Subscribed here instead of in XAML: setting SelectedIndex would fire SelectionChanged before InitMap runs.
    void InitAgeColors() => ColorModeBox.SelectionChanged += ColorMode_SelectionChanged;

    void ColorMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FillLegend();
        Render();
    }

    // Group name and color of a folder in the current mode: category or age band.
    (string Label, Color Color) GroupOf(DirNode node) =>
        AgeColors ? Ages[AgeBands.Of(node.NewestWriteUtc, DateTime.UtcNow)] : Categories[Treemap.Classify(node)];

    // Tooltip line with the last change; only in age mode, where it explains the color.
    string AgeNote(DirNode node)
    {
        if (!AgeColors) return "";
        return node.NewestWriteUtc == default ? "\n" + Strings.Map.NoChangeDate : "\n" + string.Format(CultureInfo.CurrentCulture, Strings.Map.LastChange, node.NewestWriteUtc.ToLocalTime());
    }
}
