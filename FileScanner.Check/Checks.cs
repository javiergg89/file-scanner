using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using FileScanner.Core;

namespace FileScanner.Check;

// One check per function. A failed check throws with its label.
static class Checks
{
    // Fake tree paths: never touched on disk, used only as keys.
    static readonly string FakeRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    const string NodeModules = "node_modules";
    const string FileA = "a.bin";
    const string FileB = "b.bin";
    const string CacheTitle = "Caché";
    // A second drive, used only as a fake path key.
    static readonly string DriveD = "D:" + Path.DirectorySeparatorChar;
    static readonly DateTime Now = DateTime.UtcNow;

    static void Check(bool ok, string what)
    {
        if (!ok) throw new UnreachableException("Falla: " + what);
        Console.WriteLine("ok  " + what);
    }

    static DirNode Add(DirNode parent, string name, long size, DateTime lastWrite = default)
    {
        var node = new DirNode { Name = name, Path = Path.Combine(parent.Path, name), Parent = parent, Size = size, LastWriteUtc = lastWrite };
        parent.Children.Add(node);
        return node;
    }

    static DirNode Box()
    {
        var box = new DirNode { Name = "box", Path = Path.Combine(FakeRoot, "box"), Size = 24 };
        foreach (var size in new long[] { 6, 6, 4, 3, 2, 2, 1 }) Add(box, "n" + size, size);
        return box;
    }

    static bool Near(double a, double b, double tolerance = 1e-6) => Math.Abs(a - b) < tolerance;

    // Treemap: tile areas are proportional to size and fill the rectangle without overflowing it.
    public static void Treemap()
    {
        var tiles = FileScanner.Core.Treemap.Layout(Box().Children, 0, 0, 600, 400);
        Check(tiles.Count == 7, "treemap crea una celda por nodo");
        Check(Near(tiles.Sum(t => t.W * t.H), 240000), "treemap cubre todo el área");
        Check(tiles.All(t => Near(t.W * t.H, t.Node.Size * 10000.0)), "área proporcional al tamaño");
        Check(tiles.All(Inside), "celdas dentro del rectángulo");

        static bool Inside(Tile t) => t.X >= -1e-9 && t.Y >= -1e-9 && t.X + t.W <= 600 + 1e-6 && t.Y + t.H <= 400 + 1e-6;
    }

    // Rings: depth 1 covers 360 degrees and children fit inside the parent's sweep.
    public static void Rings()
    {
        var box = Box();
        Add(box.Children[0], "a", 4);
        Add(box.Children[0], "b", 2);
        var arcs = Sunburst.Layout(box, 3, 0);
        Check(Near(arcs.Where(a => a.Depth == 1).Sum(a => a.Sweep), 360), "anillo 1 suma 360°");
        Check(Near(arcs.Where(a => a.Depth == 2).Sum(a => a.Sweep), 90), "anillo 2 ocupa el ángulo del padre (6/24 de 360°)");
    }

    // Scanner on a real folder: totals, repos, paths and incremental rescan after disk changes.
    public static async Task ScannerAsync()
    {
        var dir = Directory.CreateTempSubdirectory("fscheck");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "repo", ".git"));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "repo", FileA), new byte[10]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, FileB), new byte[5]);
            var scanned = DiskScanner.Scan(dir.FullName);
            var repo = scanned.Find(Path.Combine(dir.FullName, "repo"))!;
            Check(scanned.Size == 15 && scanned.FileCount == 2, "escáner suma 15 bytes en 2 archivos");
            Check(scanned.ContainsRepo && repo.HasRepo, "escáner detecta el repo");
            Check(scanned.Find(dir.FullName + "x") is null, "Find no confunde prefijos de nombre");
            Check(DiskScanner.ListEntries(scanned).Select(e => e.Name).SequenceEqual(["repo", FileB]), "desglose lista carpetas y archivos por tamaño");

            var newDir = Path.Combine(dir.FullName, "nueva");
            Directory.CreateDirectory(newDir);
            await File.WriteAllBytesAsync(Path.Combine(newDir, "c.bin"), new byte[100]);
            DiskScanner.Apply(scanned, DiskScanner.Rescan(scanned, deep: false));
            Check(scanned.Size == 115 && scanned.Find(Path.Combine(dir.FullName, "repo")) == repo, "reescaneo añade lo nuevo y conserva los nodos que siguen");

            File.Delete(Path.Combine(dir.FullName, "repo", FileA));
            DiskScanner.Apply(repo, DiskScanner.Rescan(repo, deep: false));
            Check(repo.Size == 0 && scanned.Size == 105, "borrar un archivo descuenta su tamaño en los antecesores");

            Directory.Delete(newDir, recursive: true);
            var gone = scanned.Find(newDir)!;
            DiskScanner.Apply(scanned, DiskScanner.Rescan(scanned, deep: false));
            Check(scanned.Size == 5 && !gone.IsAttachedTo(scanned), "carpeta borrada sale del árbol");

            Directory.Delete(Path.Combine(dir.FullName, "repo", ".git"));
            DiskScanner.Apply(repo, DiskScanner.Rescan(repo, deep: false));
            Check(!repo.HasRepo && !scanned.ContainsRepo, "quitar el .git deja de marcar el repo en los antecesores");

            // Folder with an old date holding a freshly modified file, plus an empty file.
            var old = Path.Combine(dir.FullName, "vieja");
            Directory.CreateDirectory(old);
            var data = Path.Combine(old, "datos.db");
            await File.WriteAllBytesAsync(data, new byte[7]);
            await File.WriteAllBytesAsync(Path.Combine(old, "vacio.txt"), []);
            File.SetLastWriteTimeUtc(data, DateTime.UtcNow);
            Directory.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-400));
            scanned = DiskScanner.Scan(dir.FullName);
            var oldNode = scanned.Find(old)!;
            Check(oldNode.NewestWriteUtc > DateTime.UtcNow.AddDays(-1), "la fecha de un fichero modificado sube a la carpeta");
            Check(oldNode.FileCount == 2, "los ficheros vacíos cuentan");

            Directory.Move(old, old + "2");
            DiskScanner.Rename(oldNode, old + "2");
            DiskScanner.Apply(scanned, DiskScanner.Rescan(scanned, deep: false));
            Check(scanned.Find(old + "2") == oldNode, "renombrar conserva el nodo");

            Check(await Cancels(() => Task.Run(() => DiskScanner.Scan(dir.FullName, null, new CancellationToken(true)))), "cancelar el escaneo lanza OperationCanceledException");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    static async Task<bool> Cancels(Func<Task> run)
    {
        try
        {
            await run();
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    // mklink /J does not need admin rights; a symbolic link does.
    static void Junction(string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),["/c", "mklink", "/J", link, target]) { CreateNoWindow = true, RedirectStandardOutput = true })!;
        mklink.WaitForExit();
        if (mklink.ExitCode != 0) throw new IOException("no se pudo crear el junction " + link);
    }

    // Links: a junction is not followed, and pnpm hard links count once, also with long paths.
    public static async Task ScannerLinksAsync()
    {
        var dir = Directory.CreateTempSubdirectory("fslinks");
        try
        {
            var real = Directory.CreateDirectory(Path.Combine(dir.FullName, "real"));
            await File.WriteAllBytesAsync(Path.Combine(real.FullName, FileA), new byte[10]);
            string link = Path.Combine(dir.FullName, "enlace");
            Junction(link, real.FullName);
            var scanned = DiskScanner.Scan(dir.FullName);
            Check(scanned.Size == 10 && scanned.Find(link) is null, "el escáner no entra en un junction");
            // Delete(recursive) fails with access denied on the junction, so the link alone is removed first.
            Directory.Delete(link);

            var pnpm = Directory.CreateDirectory(Path.Combine(dir.FullName, ".pnpm"));
            // Over 260 characters: without the \\?\ prefix the file cannot be opened to read its links.
            string deep = pnpm.FullName;
            while (deep.Length < 300) deep = Path.Combine(deep, "carpeta-con-nombre-largo");
            foreach (var folder in new[] { pnpm.FullName, deep })
            {
                Directory.CreateDirectory(folder);
                string file = Path.Combine(folder, FileA);
                await File.WriteAllBytesAsync(file, new byte[64 * 1024]);
                if (!CreateHardLink(@"\\?\" + Path.Combine(folder, FileB), @"\\?\" + file, IntPtr.Zero)) throw new IOException("no se pudo crear el enlace duro");
            }
            scanned = DiskScanner.Scan(dir.FullName);
            Check(scanned.Find(pnpm.FullName)!.Size == 2 * 64 * 1024, "en pnpm un enlace duro de 64 KB cuenta una vez, también a más de 260 caracteres");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // Recommendations: path rules, and inactive node_modules outside AppData and dot folders.
    public static void Recommendations()
    {
        var home = new DirNode { Name = "home", Path = FakeRoot };
        Add(Add(home, ".gradle", 0), "caches", 100);
        Add(Add(home, "proj", 0), NodeModules, 50, Now.AddDays(-60));
        Add(Add(home, "fresh", 0), NodeModules, 30, Now);
        Add(Add(Add(home, ".vscode", 0), "ext", 0), NodeModules, 70, Now.AddDays(-60));
        var recs = Cleanup.Recommend(home, home.Path, Now, [], []);
        Check(recs.Single(r => r.Title == Strings.Rules.GradleCaches) is { Risk: Risk.Safe, Size: 100, IsChecked: false }, "regla de Gradle segura y sin marcar al empezar");
        Check(recs.Single(r => r.Title.StartsWith(NodeModules)) is { Risk: Risk.Review, Size: 50, IsChecked: false }, "solo node_modules de proyectos inactivos");
        // Project with an old node_modules but code edited yesterday is active.
        var edited = Add(home, "edited", 0);
        Add(edited, NodeModules, 40, Now.AddDays(-60));
        Add(edited, "src", 0).NewestWriteUtc = Now.AddDays(-1);
        var cargo = Add(home, "crate", 0);
        Add(Add(Add(cargo, "target", 80, Now.AddDays(-60)), "debug", 80), "deps", 80);
        var local = Add(Add(home, "AppData", 0), "Local", 0);
        Add(local, "npm-cache", 30L << 20);
        Add(Add(local, "pnpm", 0), "store", 40L << 20);
        Add(Add(Add(Add(Add(local, "Google", 0), "Chrome", 0), "User Data", 0), "Default", 0), "Cache", 7);
        Add(Add(Add(home, "Downloads", 9), "old", 9), NodeModules, 9, Now.AddDays(-60));
        Add(Add(Add(home, "java", 0), "target", 20, Now.AddDays(-60)), "classes", 20);
        Add(Add(Add(home, "app", 0), "obj", 10, Now.AddDays(-60)), "Debug", 10);
        Add(Add(Add(local, "JetBrains", 0), "Rider2026.2", 0), "caches", 11);
        recs = Cleanup.Recommend(home, home.Path, Now, [], [local.Path]);
        Check(recs.Single(r => r.Title.StartsWith(NodeModules)) is { Size: 50, DisplayPath: var idle } && idle == Strings.Rules.IdleFoldersOne,
            "node_modules con el proyecto editado o dentro de Descargas no se recomienda");
        Check(recs.Single(r => r.Title == Strings.Rules.IdleBuilds) is { Size: 80 + 20 + 10, Nodes.Count: 3 }, "target de Cargo y de Maven y obj de .NET inactivos");
        Check(recs.Single(r => r.Title == Strings.Rules.BrowserCaches).Size == 7, "caché de un perfil de Chrome por comodín");
        Check(recs.Single(r => r.Title == Strings.Rules.JetBrainsCaches).Size == 11, "caché de una versión de JetBrains por comodín");
        Check(recs.Single(r => r.Title == Strings.Rules.PnpmStore).Risk == Risk.Review, "el almacén de pnpm es para revisar");
        Check(!recs.Exists(r => r.Risk == Risk.Leftover), "npm-cache y pnpm no salen además como restos");

        // Windows.old is deleted with its folder.
        string systemDrive = Environment.ExpandEnvironmentVariables("%SystemDrive%") + Path.DirectorySeparatorChar;
        var drive = new DirNode { Name = systemDrive, Path = systemDrive };
        Add(drive, "Windows.old", 5);
        var windowsOld = Cleanup.Recommend(drive, FakeRoot, Now, [], []).Single();
        windowsOld.SelectAll();
        Check(!windowsOld.KeepFolders && !windowsOld.Targets.Single().ContentsOnly, "Windows.old se marca para borrarla entera");
    }

    // Test cache: a (10), b (b1 of 4 and b2 of 2) and the file f.txt (3).
    sealed record CacheTree(DirNode Root, DirNode A, DirNode B, DirNode B1, Dictionary<string, IReadOnlyList<Entry>> Listing)
    {
        public IReadOnlyList<Entry> List(string path) => Listing[path];
    }

    static CacheTree Cache()
    {
        var cache = new DirNode { Name = "cache", Path = Path.Combine(FakeRoot, "cache"), Size = 19 };
        var a = Add(cache, "a", 10);
        var b = Add(cache, "b", 6);
        var b1 = Add(b, "b1", 4);
        var b2 = Add(b, "b2", 2);
        return new CacheTree(cache, a, b, b1, new()
        {
            [cache.Path] = [new("a", a.Path, 10, a), new("b", b.Path, 6, b), new("f.txt", Path.Combine(cache.Path, "f.txt"), 3, null)],
            [b.Path] = [new("b1", b1.Path, 4, b1), new("b2", b2.Path, 2, b2)],
        });
    }

    static Entry EntryOf(DirNode node) => new(node.Name, node.Path, node.Size, node);

    // Partial selection: unchecking an item inside a fully checked folder moves the check to its siblings.
    public static void PartialSelection()
    {
        var t = Cache();
        var rec = new Recommendation(CacheTitle, Risk.Safe, "", [t.Root]);
        rec.SelectAll();
        Check(rec.CoverageOf(t.B1.Path) == Coverage.Full, "marcar la recomendación cubre todo su contenido");
        rec.Toggle(EntryOf(t.B1), false, t.List);
        Check(rec.CoverageOf(t.B1.Path) == Coverage.None, "desmarcar b1 la deja sin marcar");
        Check(rec.CoverageOf(t.B.Path) == Coverage.Partial && rec.CoverageOf(t.A.Path) == Coverage.Full, "b queda parcial y a entera");
        Check(rec.IsChecked is null && rec.SelectedSize == 10 + 3 + 2, "recomendación parcial con 15 bytes seleccionados");
        rec.Toggle(EntryOf(t.B), false, t.List);
        Check(rec.CoverageOf(t.B1.Path) == Coverage.Full && rec.SelectedSize == 19, "marcar la carpeta parcial la cubre entera");
        Check(rec.TargetCount == 1 && rec.IsChecked == true, "marcar lo último que faltaba junta la marca en la base");
        rec.Toggle(EntryOf(t.B1), false, t.List);
        rec.Toggle(EntryOf(t.B1), false, t.List);
        Check(rec.TargetCount == rec.Nodes.Count && rec.IsChecked == true, "desmarcar y volver a marcar deja la recomendación entera");
        var leftover = new Recommendation("Resto", Risk.Leftover, "", [t.Root]) { KeepFolders = false };
        leftover.SelectAllIn(t.Root);
        Check(leftover.IsChecked == true && !leftover.Targets.Single().ContentsOnly, "seleccionar todo en un resto lo borra entero");
        var restored = new Recommendation("m", Risk.Manual, "", []);
        restored.Restore([new Target(Path.Combine(FakeRoot, "no-existe.bin"), false, null, 5)], _ => null);
        Check(restored.TargetCount == 0, "al reescanear se descartan los ficheros que ya no existen");
    }

    // Prune and Restore: missing targets are dropped and the check state is recomputed on the new tree.
    public static void KeepSelection()
    {
        var t = Cache();
        var rec = new Recommendation(CacheTitle, Risk.Safe, "", [t.Root]);
        var changed = new List<string?>();
        rec.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        rec.SelectAll();
        changed.Clear();
        rec.Prune(_ => false);
        Check(changed.Order().SequenceEqual([nameof(Recommendation.Size), nameof(Recommendation.SizeText)]), "Prune sin nada que quitar solo avisa de los tamaños");
        changed.Clear();
        rec.Prune(target => target.Path == t.Root.Path);
        Check(rec is { TargetCount: 0, IsChecked: false } && changed.Contains(nameof(Recommendation.IsChecked)), "Prune quita lo borrado y avisa del cambio de marca");

        // After a deep rescan the nodes are new objects with the same paths.
        rec.SelectAll();
        var fresh = Cache();
        rec.Restore([.. rec.Targets], fresh.Root.Find);
        Check(rec.IsChecked == true && rec.Nodes.Single() == fresh.Root && rec.Targets.Single().Node == fresh.Root, "Restore vuelve a marcar la recomendación sobre los nodos nuevos");

        var dir = Directory.CreateTempSubdirectory("fskeep");
        try
        {
            string file = Path.Combine(dir.FullName, FileA);
            File.WriteAllBytes(file, new byte[3]);
            var manual = new Recommendation("m", Risk.Manual, "", []);
            manual.Restore([new Target(t.B.Path, false, t.B, 0), new Target(file, false, null, 3)], _ => null);
            Check(manual.Targets.Single().Path == file && manual.DisplayPath == Strings.Rules.ItemsOne, "Restore descarta la carpeta que ya no está y conserva el fichero que sigue en disco");

            // ExpandTargets: a folder to empty yields its entries; a missing folder yields nothing.
            File.WriteAllBytes(Path.Combine(dir.FullName, FileB), new byte[2]);
            var folder = new DirNode { Name = dir.Name, Path = dir.FullName, Size = 5 };
            var empty = new Recommendation(CacheTitle, Risk.Safe, "", [folder]);
            empty.SelectAll();
            Check(Cleanup.ExpandTargets([empty]).Order().SequenceEqual([file, Path.Combine(dir.FullName, FileB)]), "vaciar una carpeta elimina sus dos ficheros");
            // The breakdown showed only FileA (FileB stands for a cloud-only file), so checking it does not empty the whole folder.
            var seen = new Recommendation(CacheTitle, Risk.Safe, "", [folder]);
            seen.Toggle(new Entry(FileA, file, 3, null), false, _ => [new Entry(FileA, file, 3, null)]);
            Check(seen.TargetCount == 1 && seen.Targets.Single().Path == file, "marcar lo visible no junta la marca si en disco hay más");
            var missing = new Recommendation(CacheTitle, Risk.Safe, "", [new DirNode { Name = "x", Path = Path.Combine(dir.FullName, "no-existe") }]);
            missing.SelectAll();
            Check(Cleanup.ExpandTargets([missing]).Count == 0, "vaciar una carpeta que ya no existe no elimina nada");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // The script empties base folders, deletes the selection and escapes PowerShell single quotes.
    public static void Script()
    {
        var t = Cache();
        var rec = new Recommendation(CacheTitle, Risk.Safe, "", [t.Root]);
        rec.SelectAll();
        rec.Toggle(EntryOf(t.A), false, t.List);
        var script = Cleanup.BuildScript([rec]);
        Check(script.Contains($"Remove-Tree '{t.B.Path}'\n"), "script borra b");
        Check(!script.Contains($"'{t.A.Path}'"), "script respeta a");
        var whole = new Recommendation(CacheTitle, Risk.Safe, "", [t.Root]);
        whole.SelectAll();
        Check(Cleanup.BuildScript([whole]).Contains($"Remove-Tree '{t.Root.Path}' -ContentsOnly\n"), "script vacía la caché y conserva la carpeta");
        var leftover = new Recommendation("Resto", Risk.Leftover, "", [t.Root]) { KeepFolders = false };
        leftover.SelectAll();
        Check(Cleanup.BuildScript([leftover]).Contains($"Remove-Tree '{t.Root.Path}'\n"), "script borra un resto entero");
        var manual = new Recommendation("m", Risk.Manual, "", []);
        manual.Add(new DirNode { Name = "O'Brien", Path = Path.Combine(FakeRoot, "O'Brien"), Size = 1 });
        Check(Cleanup.BuildScript([manual]).Contains("O''Brien'"), "script escapa comillas");
        var typographic = new Recommendation("t", Risk.Manual, "", []);
        typographic.AddFile(Path.Combine(FakeRoot, "x’; Remove-Item x; ’.txt"), 1);
        Check(Cleanup.BuildScript([typographic]).Contains("x’’; Remove-Item"), "script escapa las comillas tipográficas");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var vital = new Recommendation("v", Risk.Manual, "", []);
        vital.Add(new DirNode { Name = "home", Path = home, Size = 1 });
        Check(!Cleanup.BuildScript([vital]).Contains("Remove-Item -LiteralPath"), "script no borra la carpeta personal entera");
        var emptied = new Recommendation("v", Risk.Safe, "", [new DirNode { Name = "home", Path = home, Size = 1 }]);
        emptied.SelectAll();
        Check(!Cleanup.BuildScript([emptied]).Contains("-ContentsOnly"), "script no vacía la carpeta personal (se llevaría AppData)");
        Check(!Paths.HoldsProtected(Path.Combine(home, "Downloads")) && Paths.HoldsProtected(home), "Descargas se puede vaciar; la carpeta personal, no");
        Check(Paths.IsProtected(home) && Paths.IsProtected(Path.GetPathRoot(home)!) && Paths.IsProtected(Path.GetDirectoryName(home)!)
            && Paths.IsProtected(Path.Combine(home, "AppData", "Local")) && !Paths.IsProtected(Path.Combine(home, "AppData", "Local", "Temp")),
            "carpetas protegidas: personal, la que la contiene, unidad y AppData, pero no Temp");
    }

    // Categories: the nearest ancestor with a known name or a repo root wins.
    public static void Categories()
    {
        var home = new DirNode { Name = "home", Path = Path.Combine(FakeRoot, "cat") };
        var myGames = Add(Add(home, "Documents", 0), "My Games", 0);
        var minecraft = Add(Add(Add(home, "AppData", 0), "Roaming", 0), ".minecraft", 0);
        var project = Add(Add(home, "code", 0), "web", 0);
        project.HasRepo = true;
        var models = Add(Add(project, "src", 0), "models", 0);
        var deps = Add(project, NodeModules, 0);
        Check(FileScanner.Core.Treemap.Classify(myGames) == Category.Games, "My Games es juegos");
        Check(FileScanner.Core.Treemap.Classify(minecraft) == Category.Games, ".minecraft es juegos");
        Check(FileScanner.Core.Treemap.Classify(models) == Category.Code, "dentro de un repo: código");
        Check(FileScanner.Core.Treemap.Classify(deps) == Category.PackageCaches, "node_modules de un repo: caché de paquetes");
        Check(FileScanner.Core.Treemap.Classify(Add(home, "OneDrive - Empresa", 0)) == Category.Cloud, "OneDrive de empresa es nube");
        var packages = Add(home, "Packages", 0);
        Check(FileScanner.Core.Treemap.Classify(Add(packages, "CanonicalGroupLimited.Ubuntu_79rhkp1fndgsc", 0)) == Category.Virtualization, "distro de WSL es virtualización");
        Check(FileScanner.Core.Treemap.Classify(Add(packages, "MSTeams_8wekyb3d8bbwe", 0)) == Category.Communication, "el Teams nuevo es comunicación");
    }

    // Checking or unchecking a breakdown folder leaves its siblings alone.
    public static void FolderSelection()
    {
        var t = Cache();
        var rec = new Recommendation(CacheTitle, Risk.Safe, "", [t.Root]);
        rec.SelectAllIn(t.B);
        Check(rec.CoverageOf(t.B1.Path) == Coverage.Full && rec.CoverageOf(t.A.Path) == Coverage.None, "seleccionar todo en b no marca a");
        rec.SelectAll();
        rec.Remove(t.B.Path, t.List);
        Check(rec.CoverageOf(t.B1.Path) == Coverage.None && rec.CoverageOf(t.A.Path) == Coverage.Full, "deseleccionar b deja a marcada");
        Check(rec.IsChecked is null, "y la recomendación queda parcial");
    }

    // Multiple folders: the root is the drive, intermediate folders are partial and totals add up.
    public static async Task MultipleRootsAsync()
    {
        var multi = Directory.CreateTempSubdirectory("fsmulti");
        try
        {
            var homeDir = Directory.CreateDirectory(Path.Combine(multi.FullName, "home"));
            var programs = Directory.CreateDirectory(Path.Combine(multi.FullName, "programs"));
            await File.WriteAllBytesAsync(Path.Combine(homeDir.FullName, FileA), new byte[7]);
            await File.WriteAllBytesAsync(Path.Combine(programs.FullName, FileB), new byte[3]);
            var drive = DiskScanner.ScanRoots([homeDir.FullName, programs.FullName]);
            var parent = drive.Find(multi.FullName)!;
            Check(drive.IsPartial && parent.IsPartial && !drive.Find(homeDir.FullName)!.IsPartial, "unidad e intermedias parciales, escaneadas no");
            Check(parent.Size == 10 && drive.Size == 10 && parent.Children.Count == 2, "la intermedia solo suma lo escaneado");
        }
        finally
        {
            multi.Delete(recursive: true);
        }
    }

    // Duplicates: identical copies match; same size with different content does not; a hard link is not a copy.
    public static async Task DuplicatesAsync()
    {
        var dir = Directory.CreateTempSubdirectory("fsdup");
        try
        {
            var content = new byte[300_000];
            Random.Shared.NextBytes(content);
            var other = (byte[])content.Clone();
            other[150_000] ^= 0xFF; // same size and edges, different in the middle
            Directory.CreateDirectory(Path.Combine(dir.FullName, "x"));
            string original = Path.Combine(dir.FullName, "original.bin");
            await File.WriteAllBytesAsync(original, content);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "x", "copia.bin"), content);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "distinto.bin"), other);
            if (!CreateHardLink(Path.Combine(dir.FullName, "x", "enlace.bin"), original, IntPtr.Zero)) throw new IOException("no se pudo crear el enlace duro");
            // Copies in node_modules and .git must not show up; the junction to x is not followed.
            Directory.CreateDirectory(Path.Combine(dir.FullName, NodeModules, "p"));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, NodeModules, "p", "c.bin"), content);
            Directory.CreateDirectory(Path.Combine(dir.FullName, ".git"));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, ".git", "c.bin"), content);
            string link = Path.Combine(dir.FullName, "enlace-x");
            Junction(link, Path.Combine(dir.FullName, "x"));

            var progress = new Recorder();
            var result = await Duplicates.FindAsync([dir.FullName], 1, new DuplicateExclusions(), progress, CancellationToken.None);
            Check(result.Groups.Count == 1, "un solo grupo de duplicados");
            var names = result.Groups[0].Files.Select(f => Path.GetFileName(f.Path)).Order().ToList();
            Check(names.Count == 2 && names.Contains("copia.bin") && !names.Contains("distinto.bin"), "la copia sí, el distinto no");
            Check(!(names.Contains("original.bin") && names.Contains("enlace.bin")), "el enlace duro y su original cuentan una vez");
            Check(!result.Groups[0].Files.Any(f => f.Path.Contains(NodeModules) || f.Path.Contains(".git")), "las copias en node_modules y .git no salen");
            Check(result.Groups[0].Wasted == 300_000, "se recuperan 300.000 bytes");
            var edges = progress.Reports.Where(p => p.Stage == Strings.Core.DuplicatesComparingEdges).ToList();
            Check(edges.Count > 0 && edges.TrueForAll(p => !p.InBytes) && edges[^1].Done == edges[^1].Total && progress.Reports[^1] is { InBytes: true } last && last.Done == last.Total,
                "la fase de bordes cuenta archivos y cada fase termina con su aviso completo");

            // Exclusions: folder x (holds the copy), extension .bin or the whole group each leave no results.
            async Task<int> GroupsWith(DuplicateExclusions ex) => (await Duplicates.FindAsync([dir.FullName], 1, ex, null, CancellationToken.None)).Groups.Count;
            var byFolder = new DuplicateExclusions();
            byFolder.AddFolder(DuplicateExclusions.NormalizeFolder(Path.Combine(dir.FullName, "x") + "\\")!);
            Check(await GroupsWith(byFolder) == 0, "excluir la carpeta de la copia quita el grupo y el junction a ella no la trae de vuelta");
            Directory.Delete(link);
            var byExtension = new DuplicateExclusions();
            byExtension.AddExtension(DuplicateExclusions.NormalizeExtension("*.BIN")!);
            Check(await GroupsWith(byExtension) == 0 && byExtension.Extensions[0] == ".bin", "excluir *.BIN quita los .bin");
            var byGroup = new DuplicateExclusions();
            byGroup.Ignore(new IgnoredGroup(result.Groups[0].Hash, "original.bin", 300_000));
            Check(await GroupsWith(byGroup) == 0, "un grupo ignorado no vuelve a salir");

            string file = Path.Combine(dir.FullName, "exclusiones.json");
            byGroup.AddExtension(".mp4");
            byGroup.Save(file);
            var loaded = DuplicateExclusions.Load(file);
            Check(loaded.Ignores(result.Groups[0].Hash) && loaded.Extensions.SequenceEqual([".mp4"]), "las exclusiones se guardan y se cargan");
            Check(DuplicateExclusions.NormalizeFolder("carpeta\\relativa") is null && DuplicateExclusions.NormalizeExtension("a b") is null, "rutas relativas y extensiones raras se rechazan");
            await File.WriteAllTextAsync(file, """{ "Folders": null, "Extensions": null, "Groups": null }""");
            Check(!DuplicateExclusions.Load(file).Copy().ExcludesFolder(dir.FullName), "listas null en el JSON cuentan como vacías");
            Check(DuplicateExclusions.NormalizeFolder(@"C:\") == @"C:\" && DuplicateExclusions.NormalizeFolder("\"C:\\a\\b\\\"") == @"C:\a\b"
                && DuplicateExclusions.NormalizeFolder("C:/a/../b") == @"C:\b" && DuplicateExclusions.NormalizeFolder("C:") is null,
                "carpetas excluidas: raíz de unidad, comillas, .., barras normales y C: sin barra");
            var nested = new DuplicateExclusions();
            nested.AddFolder(@"C:\x");
            var whole = new DuplicateExclusions();
            whole.AddFolder(@"C:\");
            Check(!nested.ExcludesFolder(@"C:\xy") && nested.ExcludesFolder(@"C:\x\y") && whole.ExcludesFolder(@"C:\Users"), "excluir C:\\x no excluye C:\\xy y excluir C:\\ lo excluye todo");

            // Cancel mid-folder: the progress report after the first 500 files cancels the search.
            var many = Directory.CreateDirectory(Path.Combine(dir.FullName, "muchos"));
            for (int i = 0; i < 600; i++) await File.WriteAllBytesAsync(Path.Combine(many.FullName, i + ".txt"), [1]);
            using var cancel = new CancellationTokenSource();
            var cancelling = new Recorder(cancel.Cancel);
            Check(await Cancels(() => Duplicates.FindAsync([many.FullName], 1, new DuplicateExclusions(), cancelling, cancel.Token)), "cancelar durante la búsqueda de duplicados lanza OperationCanceledException");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // Synchronous IProgress: Progress<T> delivers reports on another thread in any order.
    sealed class Recorder(Action? onReport = null) : IProgress<DuplicateProgress>
    {
        readonly Lock _gate = new();
        public List<DuplicateProgress> Reports { get; } = [];

        public void Report(DuplicateProgress value)
        {
            lock (_gate) Reports.Add(value);
            onReport?.Invoke();
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern bool CreateHardLink(string newFile, string existingFile, IntPtr security);

    // Deleting from the app removes the folder from the tree and subtracts its size up to the root without reading the disk.
    public static void RemoveFolder()
    {
        var t = Cache();
        t.Root.Size = 19;
        t.Root.FileCount = 4;
        t.B.FileCount = 2;
        DiskScanner.Remove(t.B);
        Check(!t.B.IsAttachedTo(t.Root) && t.Root.Size == 13 && t.Root.FileCount == 2, "quitar b resta 6 bytes y 2 archivos a la raíz");
    }

    // "Este equipo" looks up each path in its drive, and a folder inside another requested folder is scanned once.
    public static void Computer()
    {
        var computer = new DirNode { Name = DiskScanner.ComputerName, Path = "", IsPartial = true };
        var c = Add(computer, "C:", 0);
        var d = new DirNode { Name = "D:", Path = DriveD, Parent = computer };
        computer.Children.Add(d);
        var games = Add(d, "Games", 0);
        Check(computer.Find(games.Path) == games && computer.FindNearest(Path.Join(DriveD, "Games", "nada")) == games, "Este equipo encuentra rutas de D:");
        Check(computer.FindNearest(Path.Join("Z:" + Path.DirectorySeparatorChar, "x")) is null && c.Parent == computer, "una unidad no escaneada no se encuentra");

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string drive = Path.GetPathRoot(home)!;
        Check(DiskScanner.Targets([home, drive]).SequenceEqual([drive]), "con la unidad completa sobra la carpeta personal");
    }

    // Folder passed at launch (Explorer context menu) and the scope button label.
    public static void Scope()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string drive = Path.GetPathRoot(home)!;
        Check(ScanScope.LaunchFolder([]) is null && ScanScope.LaunchFolder(["--x", Path.Join(home, "no-existe-fs")]) is null, "sin carpeta válida no se elige ninguna");
        Check(ScanScope.LaunchFolder(["--x", $"\"{home}{Path.DirectorySeparatorChar}\""]) == home, "la primera carpeta existente, sin comillas ni barra final");
        Check(ScanScope.LaunchFolder([drive]) == drive && ScanScope.LaunchFolder([drive[..2] + "\""]) == drive, "una unidad conserva su barra, también si llega como C:\"");
        Check(ScanScope.Label([home], home) == Strings.Core.ScopeHome
              && ScanScope.Label([home.ToUpperInvariant(), drive], home) == string.Format(CultureInfo.CurrentCulture, Strings.Core.ScopeHomePlus, 1), "con la carpeta personal");
        Check(ScanScope.Label([Path.Join(drive, "Juegos")], home) == "Juegos" && ScanScope.Label([drive], home) == drive.TrimEnd('\\'), "una carpeta o unidad elegida muestra su nombre");
        Check(ScanScope.Label([drive, DriveD], home) == string.Format(CultureInfo.CurrentCulture, Strings.Core.ScopeFolders, 2), "varias sin la personal");
    }

    // Saved cache: what loads equals what was scanned (paths, sizes, dates, repos, intermediate folders).
    public static async Task CacheAsync()
    {
        var dir = Directory.CreateTempSubdirectory("fscache");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "repo", ".git"));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "repo", FileA), new byte[10]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, FileB), new byte[5]);
            var scanned = DiskScanner.Scan(dir.FullName);
            string file = Path.Combine(dir.FullName, "copia.bin");
            var saved = DateTime.UtcNow;
            ScanCache.Save(file, scanned, saved);
            var (loaded, when) = ScanCache.Load(file)!.Value;
            Check(when == saved && Same(scanned, loaded), "la copia guardada se carga igual que se escaneó");
            await File.WriteAllBytesAsync(file, [1, 2, 3]);
            Check(ScanCache.Load(file) is null, "una copia dañada se ignora");
            // Valid Brotli wrapping random bytes: it decompresses and then fails when reading fields.
            var garbage = new byte[4096];
            System.Security.Cryptography.RandomNumberGenerator.Fill(garbage);
            BitConverter.TryWriteBytes(garbage, 1);
            await using (var output = new System.IO.Compression.BrotliStream(File.Create(file), System.IO.Compression.CompressionLevel.Fastest))
                await output.WriteAsync(garbage);
            Check(ScanCache.Load(file) is null && !File.Exists(file), "una copia con basura bien comprimida se ignora y se borra");

            // "Este equipo": drives are saved by name ("D:") and loaded with the path "D:\".
            var computer = new DirNode { Name = DiskScanner.ComputerName, Path = "", IsPartial = true };
            foreach (var letter in "CD") computer.Children.Add(new DirNode { Name = letter + ":", Path = letter + ":" + Path.DirectorySeparatorChar, Parent = computer, IsPartial = true });
            var games = Add(computer.Children[1], "Games", 3);
            ScanCache.Save(file, computer, saved);
            var (drives, _) = ScanCache.Load(file)!.Value;
            Check(Same(computer, drives) && drives.Find(games.Path) is not null, "la copia de Este equipo se carga con las rutas de cada unidad");
            Check(ScanCache.FileFor([@"C:\A", DriveD]) == ScanCache.FileFor(["d:" + Path.DirectorySeparatorChar, @"c:\a"]), "la copia no depende del orden ni de las mayúsculas");

            // Saving deletes caches older than 30 days and temp files older than 1 day; recent caches stay.
            string Aged(string name, int days)
            {
                string path = Path.Combine(dir.FullName, name);
                File.WriteAllBytes(path, [1]);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-days));
                return path;
            }
            string stale = Aged("scan-vieja.bin", 40), recent = Aged("scan-reciente.bin", 5), temp = Aged("scan-x.bin.1.tmp", 2);
            ScanCache.Save(Path.Combine(dir.FullName, "scan-actual.bin"), scanned, saved);
            Check(!File.Exists(stale) && !File.Exists(temp) && File.Exists(recent), "guardar borra las copias y temporales viejos");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    static bool Same(DirNode a, DirNode b) =>
        a.Name == b.Name && a.Path == b.Path && a.Size == b.Size && a.FileCount == b.FileCount && a.IsFiles == b.IsFiles
        && a.HasRepo == b.HasRepo && a.ContainsRepo == b.ContainsRepo && a.IsPartial == b.IsPartial
        && a.LastWriteUtc == b.LastWriteUtc && a.NewestWriteUtc == b.NewestWriteUtc
        && a.ChildrenOrEmpty.Count == b.ChildrenOrEmpty.Count && a.ChildrenOrEmpty.Zip(b.ChildrenOrEmpty).All(p => Same(p.First, p.Second) && p.Second.Parent == b);

    // Leftovers: no installed program uses them, unchanged for 60 days, at least 20 MB.
    public static void LeftoverFolders()
    {
        var pc = new DirNode { Name = "C:", Path = Path.Combine(FakeRoot, "pc") };
        var pf = Add(pc, "Program Files", 0);
        DirNode Folder(string name, long mb, int daysAgo)
        {
            var folder = Add(pf, name, mb << 20);
            folder.NewestWriteUtc = Now.AddDays(-daysAgo);
            return folder;
        }
        var orphan = Folder("OldApp", 30, 200);
        Folder("Adobe", 500, 200);
        var tool = Folder("Tool", 40, 200);
        Folder("Fresh", 40, 5);
        Folder("Small", 1, 200);
        Folder("Microsoft Office", 900, 200);
        var wsl = Folder("WSL", 800, 200);
        var npm = Folder("npm", 588, 200);
        Folder("7-Zip", 30, 200);
        List<InstalledApp> apps =
        [
            new("Photoshop 2026", "Adobe Inc.", null),
            new("Some Tool", "Nobody", Path.Combine(tool.Path, "bin")),
            new("7-Zip 23.01 (x64)", "Igor Pavlov", null),
            // Unnamed locations (service, PATH, the reviewed folder itself and the drive) must not protect OldApp.
            new("", null, wsl.Path),
            new("", null, npm.Path),
            new("", null, pf.Path),
            new("Roto", null, Path.GetPathRoot(pf.Path)),
        ];
        var leftovers = Leftovers.Find(pc, [pf.Path], apps, Now);
        Check(leftovers.Count == 1 && leftovers[0] == orphan, "solo OldApp es un posible resto");
        Check(Leftovers.ServiceFolder(@"\??\C:\Program Files\Foo\drv.sys") == @"C:\Program Files\Foo"
            && Leftovers.ServiceFolder("\"C:\\Program Files\\WSL\\wslservice.exe\"") == @"C:\Program Files\WSL"
            && Leftovers.ServiceFolder(@"\SystemRoot\System32\drivers\x.sys") is null, "carpeta del ImagePath de un servicio");
        Check(Leftovers.PathFolders("C:\\A\\bin;;\"C:\\B\\\";relative;C:\\").SequenceEqual([@"C:\A\bin", @"C:\B"]), "carpetas absolutas del PATH");
        Check(Leftovers.TopFolders(@"C:\Program Files\WSL\x\wsl.exe", [@"C:\Program Files"]).SequenceEqual([@"C:\Program Files\WSL"])
            && !Leftovers.TopFolders(@"C:\Program Files\a.exe", [@"C:\Program Files"]).Any()
            && !Leftovers.TopFolders(@"02:\SOFTWARE\x", [@"C:\Program Files"]).Any(), "carpeta de primer nivel de un componente MSI");
        Check(Leftovers.FolderOf("\"C:\\P\\Foo\\u.exe\" /S") == @"C:\P\Foo" && Leftovers.FolderOf(@"C:\Foo\foo.ico,0") == @"C:\Foo"
            && Leftovers.FolderOf("MsiExec.exe /X{90160000-0011-0000-0000-0000000FF1CE}") is null
            && Leftovers.FolderOf("\"C:\\P\\Foo, Inc\\u.exe\"") == @"C:\P\Foo, Inc", "carpeta del desinstalador: índice de icono sí, coma del nombre no");
    }

    // Types: totals per extension, case-insensitive, subfolders included, largest first.
    public static async Task FileTypesAsync()
    {
        var dir = Directory.CreateTempSubdirectory("fstypes");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "sub"));
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "a.txt"), new byte[10]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "b.TXT"), new byte[5]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "sin"), new byte[3]);
            await File.WriteAllBytesAsync(Path.Combine(dir.FullName, "sub", "v.mp4"), new byte[100]);
            var scanned = DiskScanner.Scan(dir.FullName);
            var types = DiskScanner.ByExtension(DiskScanner.FileFolders(scanned));
            Check(types.SequenceEqual([new TypeStat(".mp4", 100, 1), new TypeStat(".txt", 15, 2), new TypeStat("", 3, 1)]),
                "tipos: .mp4, .txt sin distinguir mayúsculas y sin extensión, de mayor a menor");
            Check(types.Sum(t => t.Bytes) == scanned.Size, "los tipos suman lo mismo que el escaneo");
            Check(DiskScanner.ByExtension([]).Count == 0, "sin carpetas no hay tipos");

            // Largest: the n biggest files in the subtree, subfolders included, largest first, with full path.
            var folders = DiskScanner.FileFolders(scanned);
            var top = DiskScanner.Largest(folders, 2);
            Check(top.Select(f => (Path.GetFileName(f.Path), f.Size)).SequenceEqual([("v.mp4", 100L), ("a.txt", 10L)])
                && top[0].Path == Path.Combine(dir.FullName, "sub", "v.mp4"), "más grandes: los dos mayores con su ruta");
            Check(DiskScanner.Largest(folders, 10).Count == 4 && DiskScanner.Largest([], 10).Count == 0, "más grandes: todos si hay menos que el límite");
        }
        finally
        {
            dir.Delete(true);
        }
    }

    // List: sort by column, ties by name, and the direction each column takes when clicked.
    public static void FolderListOrder()
    {
        var box = new DirNode { Name = "box", Path = Path.Combine(FakeRoot, "box") };
        var b = Add(box, "b", 5);
        var a = Add(box, "a", 5);
        var c = Add(box, "C", 9);
        (a.FileCount, b.FileCount, c.FileCount) = (3, 1, 2);
        (a.NewestWriteUtc, b.NewestWriteUtc, c.NewestWriteUtc) = (Now, Now.AddDays(-1), Now.AddDays(-2));
        static string Names(List<DirNode> list) => string.Concat(list.Select(n => n.Name));
        Check(Names(FolderList.Sorted(box.Children, FolderColumn.Size, true)) == "Cab", "lista: tamaño de mayor a menor, empates por nombre");
        Check(Names(FolderList.Sorted(box.Children, FolderColumn.Name, false)) == "abC", "lista: nombre sin distinguir mayúsculas");
        Check(Names(FolderList.Sorted(box.Children, FolderColumn.Files, false)) == "bCa", "lista: archivos de menor a mayor");
        Check(Names(FolderList.Sorted(box.Children, FolderColumn.Modified, true)) == "abC", "lista: modificado, lo más reciente primero");
        Check(FolderList.Toggle(FolderColumn.Size, true, FolderColumn.Size) == (FolderColumn.Size, false)
            && FolderList.Toggle(FolderColumn.Size, true, FolderColumn.Name) == (FolderColumn.Name, false)
            && FolderList.Toggle(FolderColumn.Name, false, FolderColumn.Modified) == (FolderColumn.Modified, true),
            "lista: la misma columna invierte; otra empieza en su orden natural");
    }

    // Search: substring or wildcard pattern, skips the files node, largest first, at most count results.
    public static void FolderSearchTop()
    {
        var home = new DirNode { Name = "home", Path = FakeRoot };
        var web = Add(home, "web", 50);
        var outer = Add(web, NodeModules, 40);
        Add(outer, NodeModules, 10);
        Add(Add(home, "api", 30), NodeModules, 25);
        home.Children.Add(new DirNode { Name = DirNode.FilesName, Path = home.Path, Parent = home, IsFiles = true, Size = 99 });
        var folders = FolderSearch.Folders(home);
        Check(folders.Count == 6, "búsqueda: recoge las carpetas y salta los nodos de archivos");
        var found = FolderSearch.Find(folders, " NODE_mod ", 2);
        Check(found.Count == 2 && found[0] == outer && found[1].Size == 25, "búsqueda: subcadena sin mayúsculas, las más grandes primero");
        Check(FolderSearch.Find(folders, "node_*", 10).Count == 3 && FolderSearch.Find(folders, "*eb", 10).Single() == web,
            "búsqueda: comodines sobre el nombre entero");
        Check(FolderSearch.Find(folders, "archivos", 10).Count == 0 && FolderSearch.Find(folders, "  ", 10).Count == 0,
            "búsqueda: ni archivos sueltos ni texto vacío");
    }

    public static void GrowthSinceCache()
    {
        var before = new DirNode { Name = "home", Path = FakeRoot, Size = 100 };
        var oldA = Add(before, "a", 60);
        Add(oldA, "a1", 50);
        Add(oldA, "a2", 10);
        Add(before, "b", 40);
        var after = new DirNode { Name = "home", Path = FakeRoot, Size = 230 };
        var a = Add(after, "A", 150);
        var a1 = Add(a, "a1", 140);
        Add(a, "a2", 10);
        Add(after, "b", 45);
        var deep = Add(Add(after, "nueva", 35), "dentro", 30);
        var grown = Growth.Compare(before, after, 20);
        Check(grown.Count == 5 && grown[a1.Path] == 50 && grown[deep.Path] == 0 && !grown.ContainsKey(Path.Combine(FakeRoot, "b")),
            "crecimiento: tamaño anterior de lo que creció, sin distinguir mayúsculas y con 0 si es nueva");
        var top = Growth.Top(after, grown, 10, 20);
        Check(top.Count == 2 && top[0] == (a1, 90) && top[1] == (deep, 30),
            "crecimiento: la lista salta las carpetas que crecen solo por una subcarpeta ya listada");
        // Deleting 80 bytes from a1 also subtracts them from its ancestors.
        (a1.Size, a.Size, after.Size) = (60, 70, 150);
        Check(Growth.Top(after, grown, 10, 20).Single().Node == deep, "crecimiento: usa los tamaños actuales tras un borrado");
    }

    public static void CsvExportRows()
    {
        var home = new DirNode { Name = "home", Path = FakeRoot, Size = 100, FileCount = 4 };
        var small = Add(home, "a;\"b", 10);
        var big = Add(home, "big", 80);
        Add(Add(Add(big, "l2", 70), "l3", 60), "l4", 50);
        home.Children.Add(new DirNode { Name = DirNode.FilesName, Path = home.Path, Parent = home, IsFiles = true, Size = 10 });
        var rows = CsvExport.Rows(home);
        Check(rows.Select(r => r.Size).SequenceEqual([100L, 80, 70, 60, 10]), "CSV: tres niveles bajo la carpeta, de mayor a menor, sin archivos sueltos");
        var text = new StringWriter();
        CsvExport.Write(text, rows);
        var lines = text.ToString().Split(Environment.NewLine);
        string sep = CsvExport.Separator;
        Check(lines[0] == string.Join(sep, Strings.Core.CsvPath, Strings.Core.CsvSize, Strings.Core.CsvFiles, Strings.Core.CsvModified)
              && lines[1] == $"\"{FakeRoot}\"{sep}100{sep}4{sep}"
              && lines[5] == $"\"{small.Path.Replace("\"", "\"\"")}\"{sep}10{sep}0{sep}",
            "CSV: cabecera, comillas duplicadas en la ruta y sin fecha si no se conoce");
    }

    public static void AgeBandsOf()
    {
        var now = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        int[] bands = [.. new[] { now.AddDays(-3), now.AddMonths(-2), now.AddMonths(-8), now.AddYears(-2), now.AddYears(-5), default }
            .Select(d => AgeBands.Of(d, now))];
        Check(bands.SequenceEqual([0, 1, 2, 3, 4, 4]), "antigüedad: un tramo por fecha y sin fecha cuenta como lo más antiguo");
        Check(AgeBands.Of(now.AddMonths(-1), now) == 1 && AgeBands.Of(now.AddMonths(-36).AddDays(1), now) == 3,
            "antigüedad: justo un mes cae en el segundo tramo y casi tres años en el cuarto");
    }

    public static void SettingsFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FileScanner.Check-" + Environment.ProcessId);
        string file = Path.Combine(dir, "settings.json");
        try
        {
            var missing = Settings.Load(file);
            Check(missing.Theme == AppTheme.System && missing.Language == "en", "ajustes: sin archivo, tema de Windows e inglés");
            new Settings { Theme = AppTheme.Dark, Language = "es" }.Save(file);
            var saved = Settings.Load(file);
            Check(saved.Theme == AppTheme.Dark && saved.Language == "es" && File.ReadAllText(file).Contains("\"Dark\"")
                  && Directory.GetFiles(dir).Length == 1, "ajustes: se guardan con el nombre del tema y sin archivo temporal");
            File.WriteAllText(file, "{ \"Theme\": ");
            var broken = Settings.Load(file);
            Check(broken.Theme == AppTheme.System && broken.Language == "en", "ajustes: un archivo roto da los valores por defecto");
            File.WriteAllText(file, "{ \"Theme\": 7, \"Language\": \"fr\" }");
            var unknown = Settings.Load(file);
            Check(unknown.Theme == AppTheme.System && unknown.Language == "en", "ajustes: valores desconocidos dan los valores por defecto");
            Check(350.2.ToString("0.0", new Settings().Culture) == "350.2" && 350.2.ToString("0.0", saved.Culture) == "350,2",
                "ajustes: separador decimal según el idioma");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Each generated FileScanner.Strings class: every English key has a Spanish text with the same {n} placeholders.
    public static void StringsComplete()
    {
        // "es", the culture of the .es.resx files: with tryParents false, "es-ES" finds nothing.
        var spanish = CultureInfo.GetCultureInfo("es");
        var areas = typeof(Checks).Assembly.GetTypes().Where(t => t.Namespace == "FileScanner.Strings").ToList();
        Check(areas.Count > 0, "textos: hay clases generadas desde los .resx");
        foreach (var area in areas)
        {
            var manager = (ResourceManager)area.GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
            var english = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false)!.Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!);
            var es = manager.GetResourceSet(spanish, true, false)?.Cast<System.Collections.DictionaryEntry>()
                .ToDictionary(e => (string)e.Key, e => (string)e.Value!) ?? [];
            var missing = english.Keys.Where(k => !es.ContainsKey(k)).Concat(es.Keys.Where(k => !english.ContainsKey(k))).ToList();
            var placeholders = english.Keys.Where(k => es.TryGetValue(k, out var text) && Placeholders(text) != Placeholders(english[k])).ToList();
            Check(missing.Count == 0 && placeholders.Count == 0,
                $"textos {area.Name}: inglés y español con las mismas claves y marcadores" + string.Concat(missing.Concat(placeholders).Select(k => " · " + k)));
        }
    }

    static string Placeholders(string text) =>
        string.Join(",", Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().Order());
}
