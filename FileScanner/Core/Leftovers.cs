using System.Collections.Concurrent;
using Microsoft.Win32;

namespace FileScanner.Core;

// Program registered in Windows ("Add or remove programs"). Location is its folder, if known.
// Name is empty when only the folder is known (service, PATH entry, MSI component).
public sealed record InstalledApp(string Name, string? Publisher, string? Location);

// Program folders left on disk with no installed program using them.
// Heuristic: portable or unregistered programs also show up, so the app only warns.
public static class Leftovers
{
    const long MinBytes = 20L << 20;
    const int IdleDays = 60;

    // System or shared folders that do not belong to a single program.
    static readonly HashSet<string> System = new(StringComparer.OrdinalIgnoreCase)
    {
        "Common Files", "Internet Explorer", "ModifiableWindowsApps", "WindowsApps", "Reference Assemblies", "dotnet",
        "MSBuild", "Package Cache", "Packages", "Uninstall Information", "IIS", "IIS Express", "ssh", "USOShared",
        "USOPrivate", "SoftwareDistribution", "Comms", "ConnectedDevicesPlatform", "Temp", "CrashDumps", "D3DSCache",
        "Programs", "Publishers", "VirtualStore", "FontCache", "IdentityNexusIntegration", "SquirrelTemp",
        "ElevatedDiagnostics", "Application Data", "Desktop", "Documents", "Start Menu", "Templates", "regid.1991-06.com.microsoft",
        // Package managers: in use, but not registered as installed programs.
        "chocolatey", "scoop",
    };

    // Program Files, Program Files (x86) and ProgramData, in that order.
    public static string[] SystemRoots() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    // Folders whose subfolders are checked: the scanned system roots and the AppData folders in the user profile.
    public static string[] Parents(string home, IEnumerable<string> scannedSystemRoots) =>
    [
        .. scannedSystemRoots,
        Path.Combine(home, "AppData", "Local"),
        Path.Combine(home, "AppData", "Local", "Programs"),
        Path.Combine(home, "AppData", "Roaming"),
    ];

    public static List<DirNode> Find(DirNode root, IReadOnlyList<string> parents, IReadOnlyList<InstalledApp> apps, DateTime nowUtc)
    {
        var names = apps.SelectMany(a => new[] { a.Name, a.Publisher }).OfType<string>().Select(Normalize).Where(n => n.Length >= 3).ToList();
        // A location equal to a checked folder, or above it, would mark all its subfolders as in use.
        var locations = apps.Select(a => a.Location).OfType<string>().Where(l => !parents.Any(p => Paths.IsSameOrUnder(p, l))).ToList();
        var parentSet = new HashSet<string>(parents.Select(p => p.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
        return [.. parents
            .Select(root.Find).OfType<DirNode>()
            .SelectMany(p => p.Children)
            .Where(c => !c.IsFiles && !c.IsPartial && c.Size >= MinBytes && c.NewestWriteUtc < nowUtc.AddDays(-IdleDays)
                && !IsSystem(c.Name) && !parentSet.Contains(c.Path) && !InUse(c, names, locations))
            .OrderByDescending(c => c.Size)];
    }

    static bool IsSystem(string name) =>
        System.Contains(name) || name.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Windows", StringComparison.OrdinalIgnoreCase);

    // In use if an installed program's location is this folder or inside it, or if the folder name matches
    // a program's name or publisher ("Adobe" with "Adobe Inc.", "7-Zip" with "7-Zip 23.01 (x64)").
    static bool InUse(DirNode folder, List<string> names, List<string> locations)
    {
        if (locations.Exists(l => Paths.IsSameOrUnder(l, folder.Path) || Paths.IsSameOrUnder(folder.Path, l))) return true;
        string name = Normalize(folder.Name);
        // A 1- or 2-character name would match anything, so it counts as in use to avoid a false warning.
        if (name.Length < 3) return true;
        return names.Exists(n => n.Contains(name) || (n.Length >= 4 && name.Contains(n)));
    }

    static string Normalize(string text) => new([.. text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);

    // Reads the Uninstall keys (64-bit machine, 32-bit machine, current user), packages,
    // services and drivers, PATH, and MSI component folders.
    public static List<InstalledApp> ReadInstalled()
    {
        var apps = new List<InstalledApp>();
        foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32), (RegistryHive.CurrentUser, RegistryView.Default) })
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = Open(baseKey, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall is null) continue;
            foreach (var id in uninstall.GetSubKeyNames())
            {
                using var key = Open(uninstall, id);
                if (key is null) continue;
                // Entries without DisplayName (children of a suite) are still added: their folder is the reliable match.
                string name = key.GetValue("DisplayName") as string ?? "";
                string? location = key.GetValue("InstallLocation") as string is { Length: > 0 } dir && Path.IsPathFullyQualified(dir.Trim('"'))
                    ? dir.Trim('"')
                    : FolderOf(key.GetValue("DisplayIcon") as string) ?? FolderOf(key.GetValue("UninstallString") as string);
                if (!string.IsNullOrWhiteSpace(name) || location is not null)
                    apps.Add(new InstalledApp(name, key.GetValue("Publisher") as string, location));
            }
        }
        apps.AddRange(ReadPackages());
        var folders = ReadServiceFolders()
            .Concat(PathFolders(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)))
            .Concat(PathFolders(Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User)))
            .Concat(ReadMsiFolders(Parents(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), SystemRoots())));
        apps.AddRange(folders.Distinct(StringComparer.OrdinalIgnoreCase).Select(f => new InstalledApp("", null, f)));
        return apps;
    }

    // Some keys deny read access (antivirus services, other users' entries) and OpenSubKey throws
    // SecurityException. Only that key is skipped.
    static RegistryKey? Open(RegistryKey parent, string name)
    {
        try
        {
            return parent.OpenSubKey(name);
        }
        catch (Exception ex) when (ex is global::System.Security.SecurityException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Registered services and drivers. Their executable can live in a folder that Uninstall lists neither by path
    // nor by a similar name (C:\Program Files\WSL\wslservice.exe), and that is rarely written to.
    static List<string> ReadServiceFolders()
    {
        using var services = Open(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services");
        if (services is null) return [];
        var folders = new List<string>();
        foreach (var id in services.GetSubKeyNames())
        {
            using var key = Open(services, id);
            if (ServiceFolder(key?.GetValue("ImagePath") as string) is { } folder) folders.Add(folder);
        }
        return folders;
    }

    // Folder of a service's ImagePath. GetValue already expands %SystemRoot%; drivers have the "\??\" prefix.
    public static string? ServiceFolder(string? imagePath) =>
        FolderOf(imagePath?.Trim() is { } text && text.StartsWith(@"\??\", StringComparison.Ordinal) ? text[4..] : imagePath);

    // Absolute folders in a PATH variable: npm -g, pnpm or a portable CLI are not registered as programs.
    public static IEnumerable<string> PathFolders(string? pathVariable) =>
        (pathVariable ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => Environment.ExpandEnvironmentVariables(e.Trim('"')).TrimEnd('\\'))
            .Where(e => e.Length > 3 && Path.IsPathFullyQualified(e));

    // MSI installs without InstallLocation store each file's path in their components (about 120,000 values here).
    // Only the top-level folder under each checked folder is kept, which leaves a few hundred.
    // Reading them in parallel takes about 1 s (2 s serially); ReadInstalled runs alongside the scan.
    static ICollection<string> ReadMsiFolders(IReadOnlyList<string> roots)
    {
        var folders = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var userData = Open(baseKey, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData");
        if (userData is null) return [];
        // One subkey per account: S-1-5-18 (per-machine installs) and one per user.
        foreach (var sid in userData.GetSubKeyNames())
        {
            using var components = Open(userData, sid + @"\Components");
            if (components is null) continue;
            Parallel.ForEach(components.GetSubKeyNames(), id => AddComponentFolders(components, id, roots, folders));
        }
        return folders.Keys;
    }

    static void AddComponentFolders(RegistryKey components, string id, IReadOnlyList<string> roots, ConcurrentDictionary<string, byte> folders)
    {
        using var key = Open(components, id);
        if (key is null) return;
        foreach (var file in key.GetValueNames().Select(v => key.GetValue(v)).OfType<string>())
            foreach (var folder in TopFolders(file, roots)) folders.TryAdd(folder, 0);
    }

    // Top-level folder of a file under each root that contains it: "C:\Program Files\WSL\wsl.exe" gives "C:\Program Files\WSL".
    // Registry components ("02:\SOFTWARE\...") and uninstalled ones ("C?\...") are not paths and are ignored.
    public static IEnumerable<string> TopFolders(string file, IReadOnlyList<string> roots)
    {
        if (!Path.IsPathFullyQualified(file)) yield break;
        foreach (var root in roots)
        {
            if (!Paths.IsUnder(file, root)) continue;
            int start = root.TrimEnd('\\').Length + 1;
            // A file directly in the root has no folder of its own.
            if (file.IndexOf('\\', start) is > 0 and var end) yield return file[..end];
        }
    }

    // Store apps and other MSIX packages are not in Uninstall. Without them, such an app's data folder
    // (%APPDATA%\.minecraft for Minecraft Java, with the saved worlds) would be flagged as a leftover.
    // The normalized package name ("Microsoft.MinecraftJavaEdition") contains the folder name.
    static IEnumerable<InstalledApp> ReadPackages()
    {
        try
        {
            return [.. new Windows.Management.Deployment.PackageManager().FindPackagesForUser("")
                .Select(p => new InstalledApp(p.Id.Name, null, p.InstalledPath))];
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or global::System.Runtime.InteropServices.COMException)
        {
            // No access to the package catalog: continue with Uninstall only.
            return [];
        }
    }

    // Executable folder in values like "\"C:\Program Files\Foo\unins000.exe\" /S" or "C:\Foo\foo.exe,0".
    // MsiExec.exe /X{...} has no path and returns null.
    internal static string? FolderOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        string text = command.Trim();
        if (text.StartsWith('"')) text = text[1..(text.IndexOf('"', 1) is > 0 and var end ? end : text.Length)];
        else if (text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase) is >= 0 and var exe) text = text[..(exe + 4)];
        // Strips only an icon index (",0", ",-101"); a comma in a name ("Foo, Inc") is part of the path.
        if (text.LastIndexOf(',') is > 2 and var comma && int.TryParse(text[(comma + 1)..], out _)) text = text[..comma];
        // "\SystemRoot\..." is rooted but not fully qualified.
        return Path.IsPathFullyQualified(text) && text.Length > 3 ? Path.GetDirectoryName(text) : null;
    }
}
