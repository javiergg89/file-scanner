namespace FileScanner.Core;

public readonly record struct Tile(DirNode Node, double X, double Y, double W, double H);

public readonly record struct Arc(DirNode Node, int Depth, double Start, double Sweep);

public enum Category { Media, Other, Code, Sdk, AiModels, PackageCaches, Games, Browsers, Communication, Editors, Virtualization, Cloud, Temp, Programs }

public static class Sunburst
{
    // Concentric rings (sunburst): each child gets an angle proportional to its size within its parent's angle.
    public static List<Arc> Layout(DirNode root, int maxDepth, double minSweep)
    {
        var arcs = new List<Arc>();
        Add(root, 1, 0, 360);
        return arcs;

        void Add(DirNode node, int depth, double start, double sweep)
        {
            if (depth > maxDepth || node.Size <= 0) return;
            foreach (var child in node.ChildrenOrEmpty.OrderByDescending(c => c.Size))
            {
                double part = sweep * child.Size / node.Size;
                if (part < minSweep) break;
                arcs.Add(new Arc(child, depth, start, part));
                Add(child, depth + 1, start, part);
                start += part;
            }
        }
    }
}

public static class Treemap
{
    // Squarified treemap (Bruls et al.): fills rows along the short side while the worst aspect ratio improves.
    public static List<Tile> Layout(IEnumerable<DirNode> items, double x, double y, double w, double h)
    {
        var tiles = new List<Tile>();
        var nodes = items.Where(n => n.Size > 0).OrderByDescending(n => n.Size).ToList();
        double total = nodes.Sum(n => (double)n.Size);
        if (total <= 0 || w <= 0 || h <= 0) return tiles;
        double scale = w * h / total;

        int i = 0;
        while (i < nodes.Count)
        {
            double side = Math.Min(w, h);
            double rowArea = 0, worst = double.MaxValue;
            int j = i;
            while (j < nodes.Count)
            {
                double area = rowArea + nodes[j].Size * scale;
                double ratio = Worst(nodes[i].Size * scale, nodes[j].Size * scale, area, side);
                if (ratio > worst) break;
                worst = ratio;
                rowArea = area;
                j++;
            }

            double thick = rowArea / side, offset = 0;
            for (int k = i; k < j; k++)
            {
                double len = nodes[k].Size * scale / thick;
                tiles.Add(w >= h
                    ? new Tile(nodes[k], x, y + offset, thick, len)
                    : new Tile(nodes[k], x + offset, y, len, thick));
                offset += len;
            }
            if (w >= h) { x += thick; w -= thick; }
            else { y += thick; h -= thick; }
            i = j;
        }
        return tiles;
    }

    // Worst aspect ratio of a row; with the list sorted, only the largest and smallest items matter.
    static double Worst(double max, double min, double rowArea, double side)
    {
        double s2 = side * side, r2 = rowArea * rowArea;
        return Math.Max(s2 * max / r2, r2 / (s2 * min));
    }

    // Known folder names by category (case-insensitive). The nearest ancestor wins:
    // Documents\My Games is Games even though Documents is Media.
    static readonly (Category Category, string[] Names)[] Known =
    [
        (Category.Games, ["games", "my games", "saved games", "steam", "steamapps", "epic games", "epicgameslauncher",
            "fortnitegame", "riot games", "riot client", "valorant", "league of legends", "battle.net", "blizzard entertainment",
            "gog galaxy", "gog.com", "ea games", "ea desktop", "electronic arts", "ubisoft", "ubisoft game launcher",
            "rockstar games", "xboxgames", "minecraft", ".minecraft", "curseforge", "modrinthapp", "prismlauncher",
            "wemod", "playnite", "nintendo 3ds", "yuzu", "ryujinx", "dolphin emulator", "retroarch", "pcsx2", "rpcs3"]),
        (Category.Browsers, ["chrome", "chromium", "bravesoftware", "brave-browser", "mozilla", "firefox", "edge",
            "opera software", "opera", "vivaldi", "thebrowsercompany"]),
        (Category.Communication, ["discord", "slack", "teams", "microsoft teams", "whatsapp", "telegram desktop",
            "signal", "zoom", "skype", "thunderbird", "outlook"]),
        (Category.Editors, [".vscode", ".cursor", "cursor", "jetbrains", ".idea", "visual studio",
            "visualstudio", "sublime text", "notepad++", "android studio", ".windsurf", "windsurf", "zed"]),
        (Category.Virtualization, ["docker", ".docker", "docker desktop", "virtualbox vms", ".virtualbox", ".vagrant.d",
            "vmware", "virtual machines", "hyper-v", "wsl", "podman", ".minikube", ".kube"]),
        (Category.Cloud, ["dropbox", "google drive", "drivefs", "icloud drive", "iclouddrive", "mega", "megasync",
            "nextcloud", "pcloud drive"]),
        (Category.Temp, ["temp", "tmp", "crashdumps", "crashpad", "inetcache"]),
        (Category.AiModels, [".ollama", ".lmstudio", "lm studio", "huggingface", "comfyui",
            "stable-diffusion-webui"]),
        (Category.PackageCaches, ["npm-cache", ".npm", "npm", ".gradle", ".nuget", "nuget", ".cargo", ".rustup", ".m2",
            "pip", "uv", "yarn", "pnpm", "pnpm-cache", ".pnpm-store", "go", ".bun", ".deno", "composer", ".cache",
            "node_modules", ".conda", "anaconda3", "miniconda3"]),
        (Category.Sdk, ["android", ".android", "sdk", "flutter", ".dotnet", ".jdks", "java", "windows kits", ".platformio"]),
        (Category.Media, ["documents", "pictures", "music", "videos", "downloads", "spotify", "obs-studio",
            "adobe", "screenshots", "camera roll"]),
        (Category.Programs, ["program files", "program files (x86)", "programdata"]),
        (Category.Other, ["appdata", "local", "roaming", "locallow", "programs", "packages"]),
    ];

    static readonly Dictionary<string, Category> ByName = Known
        .SelectMany(k => k.Names.Select(name => (name, k.Category)))
        .ToDictionary(p => p.name, p => p.Category, StringComparer.OrdinalIgnoreCase);

    // Names with a variable part: work OneDrive ("OneDrive - Company") and Store packages, which have the
    // publisher in front and a suffix after. Without this, WSL distros and the new Teams would fall under "packages" (Other).
    static readonly (string Prefix, Category Category)[] Prefixes =
    [
        ("onedrive", Category.Cloud),
        ("canonicalgrouplimited.", Category.Virtualization),
        ("microsoftcorporationii.windowssubsystemforlinux", Category.Virtualization),
        ("msteams_", Category.Communication),
    ];

    static Category? Match(string name)
    {
        if (ByName.TryGetValue(name, out var category)) return category;
        foreach (var (prefix, prefixed) in Prefixes)
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return prefixed;
        return null;
    }

    public static Category Classify(DirNode node)
    {
        // Walking up the path, the first match wins: a known name or a repo root.
        for (var n = node; n is not null; n = n.Parent)
        {
            if (Match(n.Name) is { } category) return category;
            if (n.HasRepo) return Category.Code;
        }
        return node.ContainsRepo ? Category.Code : Category.Other;
    }
}
