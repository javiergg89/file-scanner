using System.Globalization;

namespace FileScanner.Core;

// Which folders get scanned: the one passed at startup (Explorer context menu) and the scope button text.
public static class ScanScope
{
    // First command-line argument that is an existing folder, in DiskScanner.Targets format
    // (no trailing backslash except for a drive, "C:\"). For a drive, a quoted "%1" arrives as C:" because \"
    // escapes the quote; without the backslash, "C:" would be that drive's current folder.
    public static string? LaunchFolder(IEnumerable<string> args) =>
        args.Select(a => a.Trim().Trim('"'))
            .Select(a => a.EndsWith(':') ? a + '\\' : a)
            .Where(a => a.Length > 0 && Directory.Exists(a))
            .Select(a => Path.GetFullPath(a) is var full && full.Length > 3 ? full.TrimEnd('\\') : full)
            .FirstOrDefault();

    // "User folder", "User folder + 2", the name of the only chosen folder, or "3 folders".
    public static string Label(IReadOnlyList<string> roots, string home)
    {
        bool withHome = roots.Contains(home, StringComparer.OrdinalIgnoreCase);
        return (withHome, roots.Count) switch
        {
            (true, 1) => Strings.Core.ScopeHome,
            (true, var n) => string.Format(CultureInfo.CurrentCulture, Strings.Core.ScopeHomePlus, n - 1),
            (false, 1) => Path.GetFileName(roots[0]) is { Length: > 0 } name ? name : roots[0].TrimEnd('\\'),
            (false, var n) => string.Format(CultureInfo.CurrentCulture, Strings.Core.ScopeFolders, n),
        };
    }
}
