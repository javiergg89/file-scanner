using System.Diagnostics;
using FileScanner.Core;

namespace FileScanner.Check;

// Diagnostics on the real PC, kept out of the checks because results depend on what is on disk.
static class Tools
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // dotnet run -c Release --project FileScanner.Check -- bench
    public static void Bench()
    {
        for (int run = 1; run <= 2; run++)
        {
            var clock = Stopwatch.StartNew();
            var scanned = DiskScanner.Scan(Home);
            Console.WriteLine($"pasada {run}: {clock.Elapsed.TotalSeconds:0.00} s · {scanned.FileCount:N0} archivos · {DirNode.Format(scanned.Size)}");
            // The cache snapshot is taken on the UI thread, so its time matters.
            clock.Restart();
            var snapshot = ScanCache.Snapshot(scanned, DateTime.UtcNow);
            Console.WriteLine($"         foto de la copia: {clock.ElapsedMilliseconds} ms · {DirNode.Format(snapshot.Length)} sin comprimir");
        }
    }

    // dotnet run -c Release --project FileScanner.Check -- duplicates
    // Finds duplicates of 10 MB or more in the user folder and prints the groups with the most reclaimable space.
    public static async Task DuplicateReportAsync()
    {
        var clock = Stopwatch.StartNew();
        var result = await Duplicates.FindAsync([Home], 10L << 20, DuplicateExclusions.Load(DuplicateExclusions.DefaultFile), null, CancellationToken.None);
        Console.WriteLine($"{clock.Elapsed.TotalSeconds:0.0} s · {result.Groups.Count} grupos · {DirNode.Format(result.Groups.Sum(g => g.Wasted))} recuperables · {result.Skipped} saltados");
        foreach (var group in result.Groups.Take(8))
        {
            Console.WriteLine($"{DirNode.Format(group.Size)} × {group.Files.Count}");
            foreach (var file in group.Files) Console.WriteLine("    " + file.Path);
        }
    }

    // dotnet run -c Release --project FileScanner.Check -- leftovers
    // Lists possible program leftovers on this PC to review the heuristic.
    public static void LeftoverReport()
    {
        var extras = Leftovers.SystemRoots();
        var scanned = DiskScanner.ScanRoots([Home, .. extras]);
        var installed = Leftovers.ReadInstalled();
        Console.WriteLine($"{installed.Count} programas instalados");
        foreach (var folder in Leftovers.Find(scanned, Leftovers.Parents(Home, extras), installed, DateTime.UtcNow))
            Console.WriteLine($"{DirNode.Format(folder.Size),10}  {folder.NewestWriteUtc:yyyy-MM-dd}  {folder.Path}");
    }
}
