using System.Globalization;
using System.Text;
using FileScanner.Core;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace FileScanner;

// "Exportar CSV…" in the context menu: sizes of the folder and its subfolders, to open in Excel.
public sealed partial class MainWindow
{
    async Task ExportCsvAsync(DirNode folder)
    {
        if (DemoBlocked()) return;
        var picker = new FileSavePicker(AppWindow.Id)
        {
            SuggestedFileName = string.Format(CultureInfo.CurrentCulture, Strings.Views.ExportFileName, SafeFileName(folder.Name)),
            DefaultFileExtension = ".csv",
        };
        picker.FileTypeChoices.Add(Strings.Views.CsvFileType, [".csv"]);
        var picked = await picker.PickSaveFileAsync();
        if (picked is null) return;

        // Copied here on the UI thread, with the tree as it is after the dialog closes.
        var rows = CsvExport.Rows(folder);
        string path = picked.Path;
        try
        {
            // With BOM: without it Excel reads UTF-8 as ANSI and garbles accented letters.
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                CsvExport.Write(writer, rows);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(InfoBarSeverity.Error, string.Format(CultureInfo.CurrentCulture, Strings.Views.ExportFailed, Path.GetFileName(path), ex.Message));
            return;
        }
        string name = Path.GetFileName(path);
        string message = rows.Count == 1
            ? string.Format(CultureInfo.CurrentCulture, Strings.Views.ExportedOne, name)
            : string.Format(CultureInfo.CurrentCulture, Strings.Views.ExportedMany, rows.Count, name);
        ShowStatus(InfoBarSeverity.Success, message, Strings.Views.OpenItem, () => OpenFile(path));
    }

    // "C:\" or "Este equipo" as a file name: strip the characters Windows does not allow.
    static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string safe = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim('_', ' ');
        return safe.Length > 0 ? safe : Strings.Views.FolderFallbackName;
    }
}
