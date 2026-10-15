using System.Globalization;
using FileScanner.Check;

// Core checks (Checks.cs), or with an argument, diagnostics on this PC (Tools.cs).
switch (args)
{
    case ["bench"]:
        Tools.Bench();
        break;
    case ["duplicates"]:
        await Tools.DuplicateReportAsync();
        break;
    case ["leftovers"]:
        Tools.LeftoverReport();
        break;
    default:
        // Checks compare against Strings.* texts and number formats: run in the app's default language, whatever the PC's.
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        Checks.Treemap();
        Checks.Rings();
        await Checks.ScannerAsync();
        await Checks.ScannerLinksAsync();
        Checks.Recommendations();
        Checks.PartialSelection();
        Checks.KeepSelection();
        Checks.Script();
        Checks.Categories();
        Checks.FolderSelection();
        await Checks.MultipleRootsAsync();
        Checks.LeftoverFolders();
        Checks.RemoveFolder();
        await Checks.DuplicatesAsync();
        Checks.Computer();
        Checks.Scope();
        await Checks.CacheAsync();
        await Checks.FileTypesAsync();
        Checks.FolderListOrder();
        Checks.FolderSearchTop();
        Checks.GrowthSinceCache();
        Checks.CsvExportRows();
        Checks.AgeBandsOf();
        Checks.SettingsFile();
        Checks.StringsComplete();
        break;
}
