namespace FileScanner.Core;

// Invented tree for "FileScanner.exe --demo" (README screenshots): the home folder of a fictional user.
// Pure data, no disk access. Home must not exist on the PC: the app refuses demo mode otherwise.
public static class DemoData
{
    public const string Home = @"C:\Users\alex";
    // Fake drive totals for "Free after cleanup" and "Not scanned": a 1 TB SSD.
    public const long DriveSize = 1_000_204_886_016;
    public const long DriveFree = 241L << 30;

    public static DirNode Build(DateTime nowUtc)
    {
        // Folder written "days" ago, with its children.
        DirNode D(string name, int days, params DirNode[] children)
        {
            var node = new DirNode { Name = name, Path = "", LastWriteUtc = nowUtc.AddDays(-days) };
            node.Children.AddRange(children);
            return node;
        }
        // Loose files of a folder.
        DirNode F(double gb, long count, int days) => new()
        {
            Name = DirNode.FilesName, Path = "", IsFiles = true, Size = (long)(gb * (1L << 30)), FileCount = count,
            LastWriteUtc = nowUtc.AddDays(-days), NewestWriteUtc = nowUtc.AddDays(-days),
        };
        // Folder with only loose files.
        DirNode L(string name, double gb, long count, int days) => D(name, days, F(gb, count, days));

        var root = D("alex", 1,
            F(0.02, 21, 1),
            L("Desktop", 0.83, 64, 1),
            D("Documents", 3,
                F(0.61, 312, 3), L("Invoices", 0.42, 826, 41), L("Thesis", 2.13, 1418, 905), L("Scans", 1.24, 2317, 212),
                L("Zoom", 6.48, 42, 127)),
            D("Pictures", 10,
                L("2019", 13.7, 7206, 1810), L("2021", 16.2, 8932, 1104), L("2023", 18.9, 10544, 402), L("2025", 9.6, 5120, 10),
                L("Screenshots", 1.82, 3391, 1)),
            D("Videos", 60, L("Family", 27.4, 388, 731), L("Captures", 14.1, 163, 30), L("Edits", 7.3, 85, 214)),
            D("Music", 90, D("Library", 1500, L("Albums", 21.6, 6104, 1520), L("Singles", 2.4, 812, 1480)), L("Podcasts", 2.9, 213, 90)),
            D("Downloads", 2,
                F(8.7, 642, 2), L("installers", 16.4, 54, 702), L("old-isos", 11.2, 6, 1310), L("datasets", 4.9, 4210, 301)),
            D("source", 1, D("repos", 1,
                D("webshop", 5,
                    L(".git", 0.92, 6811, 5), L("src", 0.04, 1236, 5), L("dist", 0.11, 214, 5),
                    D("node_modules", 5, F(0.21, 4120, 5), L("@next", 0.62, 11402, 5), L("typescript", 0.09, 182, 40),
                        L("@img", 0.34, 1904, 5), L(".pnpm", 0.58, 61022, 5))),
                D("legacy-api", 412,
                    L(".git", 0.41, 4207, 412), L("src", 0.03, 911, 412), L("bin", 0.62, 318, 420),
                    D("obj", 420, L("Debug", 0.47, 2302, 420), L("Release", 0.38, 1874, 430)),
                    D("node_modules", 425, F(0.16, 3020, 425), L("aws-sdk", 0.71, 21330, 425), L("webpack", 0.22, 8401, 425),
                        L("@babel", 0.31, 17760, 425), L("rxjs", 0.12, 9880, 425), L("lodash", 0.03, 1054, 425))),
                D("mobile-app", 20,
                    L(".git", 0.66, 5102, 20), L("src", 0.05, 1544, 20),
                    D("android", 20, L("build", 2.21, 18432, 20), L("app", 0.08, 640, 20)),
                    D("node_modules", 20, F(0.11, 2210, 20), L("react-native", 0.58, 30128, 20), L("@expo", 0.43, 24310, 20),
                        L("hermes-engine", 0.29, 410, 20))),
                D("data-pipeline", 214,
                    L(".git", 0.27, 2905, 214), L(".venv", 2.84, 41976, 230), L("notebooks", 1.06, 318, 214),
                    D("target", 214, D("debug", 214, L("deps", 3.41, 5221, 214), L("incremental", 1.18, 9046, 216), L("build", 0.52, 3310, 216)))),
                D("game-prototype", 92,
                    L(".git", 3.52, 9103, 92), L("Assets", 6.04, 22106, 92), L("Library", 4.19, 61340, 92), L("Packages", 0.07, 96, 140)))),
            D("Games", 30, D("SteamLibrary", 30, D("steamapps", 30,
                D("common", 30, L("Starfall Odyssey", 44.6, 3907, 30), L("Deep Rock Tactics", 16.3, 2240, 61), L("Pixel Kart", 6.9, 1112, 410)),
                L("shadercache", 1.87, 4410, 30), L("workshop", 2.6, 1620, 120)))),
            D("OneDrive", 1, L("Documents", 3.18, 5023, 1), L("Pictures", 6.94, 4211, 4), L("Shared", 1.07, 318, 66)),
            D(".nuget", 15, D("packages", 15,
                L("microsoft.windowsappsdk", 2.06, 3811, 15), L("microsoft.aspnetcore.app.runtime.win-x64", 1.12, 4420, 80),
                L("microsoft.netcore.app.runtime.win-x64", 0.94, 3920, 80), L("skiasharp", 0.71, 2290, 200), L("other", 4.61, 148302, 15))),
            D(".gradle", 30, L("caches", 6.82, 88114, 30), D("wrapper", 300, L("dists", 1.31, 2108, 300))),
            D(".vscode", 7, L("extensions", 1.09, 46012, 7)),
            D("AppData", 0,
                D("Local", 0,
                    D("Temp", 0, F(5.9, 50211, 0), L("vs-setup", 3.24, 8120, 12), L("chrome_installer", 1.08, 12, 33),
                        L("msbuild", 2.31, 21004, 1), L("pip-build", 0.66, 9330, 74), L("WinGet", 1.37, 410, 20)),
                    D("npm-cache", 3, L("_cacache", 5.38, 210412, 3), L("_logs", 0.01, 104, 3)),
                    D("NuGet", 6, L("v3-cache", 2.27, 26031, 6), L("plugins-cache", 0.12, 412, 60)),
                    D("Google", 0, D("Chrome", 0, D("User Data", 0,
                        D("Default", 0, D("Cache", 0, L("Cache_Data", 3.08, 19204, 0)), L("Code Cache", 0.91, 9120, 0),
                            L("IndexedDB", 1.42, 2311, 0), L("GPUCache", 0.06, 21, 0), F(0.58, 340, 0)),
                        D("Profile 1", 9, D("Cache", 9, L("Cache_Data", 1.21, 7302, 9)), L("Code Cache", 0.33, 3104, 9), F(0.21, 290, 9))))),
                    D("Microsoft", 0,
                        D("Edge", 2, D("User Data", 2, D("Default", 2, L("Cache", 0.79, 6230, 2), L("Code Cache", 0.18, 2210, 2), F(0.31, 280, 2)))),
                        D("Windows", 0, L("Explorer", 0.71, 34, 0), L("INetCache", 0.24, 3301, 0)),
                        L("OneDrive", 0.32, 912, 1), L("Teams", 0.44, 1880, 5)),
                    D("Docker", 1, D("wsl", 1, D("disk", 1, F(31.7, 1, 1)), L("main", 0.12, 2, 1))),
                    D("Packages", 2, D("CanonicalGroupLimited.Ubuntu_79rhkp1fndgsc", 2, L("LocalState", 15.8, 2, 2)),
                        L("Microsoft.WindowsTerminal_8wekyb3d8bbwe", 0.02, 61, 1)),
                    D("JetBrains", 4, D("Rider2025.3", 4, L("caches", 2.38, 18320, 4), L("index", 1.12, 9044, 4), L("log", 0.09, 140, 4))),
                    L("CrashDumps", 1.62, 14, 25),
                    L("D3DSCache", 0.41, 230, 3),
                    D("Programs", 5, L("Microsoft VS Code", 0.52, 9213, 5), L("retro-editor", 0.94, 4106, 512)),
                    L("OldGameLauncher", 2.61, 1802, 803)),
                D("Roaming", 1,
                    D("Code", 1, L("Cache", 0.38, 2911, 1), L("CachedData", 0.61, 1340, 1), L("User", 0.29, 1802, 1),
                        L("CachedExtensionVSIXs", 0.22, 41, 7)),
                    D("discord", 2, L("Cache", 0.47, 3120, 2), L("Code Cache", 0.12, 980, 2), L("modules", 0.31, 640, 2)),
                    L("PhotoSuite 7", 1.33, 2204, 904),
                    L("Microsoft", 0.18, 1460, 1)),
                L("LocalLow", 0.21, 512, 6)));

        root.Path = Home;
        Finish(root, null);
        return root;
    }

    // Paths and parents top-down, then totals bottom-up with the scanner's own summing.
    static void Finish(DirNode node, DirNode? parent)
    {
        node.Parent = parent;
        if (parent is not null) node.Path = node.IsFiles ? parent.Path : Path.Join(parent.Path, node.Name);
        if (node.IsFiles) return;
        foreach (var child in node.ChildrenOrEmpty) Finish(child, node);
        node.HasRepo = node.ChildrenOrEmpty.Any(c => c.Name == ".git");
        DiskScanner.SetChildren(node, node.Children);
    }
}
