using System.Globalization;
using Microsoft.Win32;

namespace FileScanner.Core;

// "Exportar CSV…": a folder and its subfolders up to three levels deep, largest first, for Excel or a ticket.
public static class CsvExport
{
    public const int Depth = 3;

    public sealed record Row(string Path, long Size, long Files, DateTime NewestWriteUtc);

    // Immutable copy of the tree: taken on the UI thread and written from the thread pool. Loose files get no
    // row of their own; their size is already in their folder's row.
    public static List<Row> Rows(DirNode folder, int depth = Depth)
    {
        var rows = new List<Row>();
        Collect(folder, depth, rows);
        rows.Sort((a, b) => b.Size != a.Size ? b.Size.CompareTo(a.Size) : StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));
        return rows;
    }

    static void Collect(DirNode node, int depth, List<Row> rows)
    {
        // "This PC" has an empty path, so its name is written.
        rows.Add(new Row(node.Path.Length > 0 ? node.Path : node.Name, node.Size, node.FileCount, node.NewestWriteUtc));
        if (depth == 0) return;
        foreach (var child in node.ChildrenOrEmpty)
            if (!child.IsFiles) Collect(child, depth - 1, rows);
    }

    // Excel opens a CSV with the list separator of the Windows region settings (sList): ";" with Spanish formats,
    // "," with US ones. Read from Windows because the app language can differ from the region format.
    public static string Separator =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\International", "sList", null) as string is { Length: > 0 } list
            ? list : CultureInfo.CurrentCulture.TextInfo.ListSeparator;

    // The path is quoted (inner quotes doubled) in case it has the separator. The date is the newest change inside, in local time.
    public static void Write(TextWriter writer, IEnumerable<Row> rows)
    {
        string sep = Separator;
        writer.WriteLine(string.Join(sep, Strings.Core.CsvPath, Strings.Core.CsvSize, Strings.Core.CsvFiles, Strings.Core.CsvModified));
        foreach (var row in rows)
        {
            string modified = row.NewestWriteUtc == default ? "" : row.NewestWriteUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            writer.WriteLine(string.Create(CultureInfo.InvariantCulture, $"\"{row.Path.Replace("\"", "\"\"")}\"{sep}{row.Size}{sep}{row.Files}{sep}{modified}"));
        }
    }
}
