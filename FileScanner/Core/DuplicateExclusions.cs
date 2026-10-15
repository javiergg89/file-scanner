using System.Text.Json;

namespace FileScanner.Core;

// Duplicate group the user marked as not a duplicate. Hash: SHA-256 of the content; Name and Size are only for display.
public sealed record IgnoredGroup(string Hash, string Name, long Size);

// What the duplicate search skips. Stored in %LOCALAPPDATA%\FileScanner and applied to every search:
// folders (with their subfolders), extensions, and groups marked as "not a duplicate".
public sealed class DuplicateExclusions
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileScanner", "duplicates-exclusions.json");

    public List<string> Folders { get; set; } = [];
    public List<string> Extensions { get; set; } = [];
    public List<IgnoredGroup> Groups { get; set; } = [];

    public int Count => Folders.Count + Extensions.Count + Groups.Count;

    public DuplicateExclusions Copy() => new() { Folders = [.. Folders], Extensions = [.. Extensions], Groups = [.. Groups] };

    public bool ExcludesFolder(string path) => Folders.Exists(f => Paths.IsSameOrUnder(path, f));

    public bool ExcludesFile(string path) =>
        ExcludesFolder(path) || Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public bool Ignores(string hash) => Groups.Exists(g => g.Hash == hash);

    // Absolute path without a trailing backslash; null if the text is not a valid path.
    public static string? NormalizeFolder(string text)
    {
        string trimmed = text.Trim().Trim('"');
        if (trimmed.Length == 0 || !Path.IsPathFullyQualified(trimmed)) return null;
        string full = Path.GetFullPath(trimmed);
        return full.Length > 3 ? full.TrimEnd('\\') : full;
    }

    // "mp4", "*.mp4" and ".MP4" become ".mp4"; null if no valid extension remains.
    public static string? NormalizeExtension(string text)
    {
        string ext = text.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant();
        return ext.Length == 0 || ext.IndexOfAny([.. Path.GetInvalidFileNameChars(), '.', ' ']) >= 0 ? null : "." + ext;
    }

    public bool AddFolder(string folder) => AddUnique(Folders, folder);

    public bool AddExtension(string extension) => AddUnique(Extensions, extension);

    public void Ignore(IgnoredGroup group)
    {
        if (!Ignores(group.Hash)) Groups.Add(group);
    }

    static bool AddUnique(List<string> list, string value)
    {
        if (list.Contains(value, StringComparer.OrdinalIgnoreCase)) return false;
        list.Add(value);
        return true;
    }

    // No file: empty exclusions. A corrupt file throws JsonException so the app can tell the user.
    public static DuplicateExclusions Load(string file)
    {
        if (!File.Exists(file)) return new DuplicateExclusions();
        var exclusions = JsonSerializer.Deserialize<DuplicateExclusions>(File.ReadAllText(file), Json) ?? new DuplicateExclusions();
        // A hand-edited file with "Folders": null overwrites the default empty list.
        exclusions.Folders ??= [];
        exclusions.Extensions ??= [];
        exclusions.Groups ??= [];
        return exclusions;
    }

    public void Save(string file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, file, overwrite: true);
    }
}
