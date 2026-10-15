using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace FileScanner.Core;

// On-disk copy of the last scan, shown at startup while the disk is rescanned in the background.
// One file per combination of scanned folders, in %LOCALAPPDATA%\FileScanner (outside the repository).
public static class ScanCache
{
    // Bump when the format changes. A copy with another version is ignored and a normal scan runs.
    const int FormatVersion = 1;

    [Flags]
    enum NodeTraits : byte { None = 0, Files = 1, Repo = 2, ContainsRepo = 4, Partial = 8 }

    public static string FileFor(IEnumerable<string> targets)
    {
        string key = string.Join('|', targets.Select(t => t.ToLowerInvariant()).Order(StringComparer.Ordinal));
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FileScanner", $"scan-{hash}.bin");
    }

    // One save at a time: the save on close waits for a running background save, then writes the latest state.
    static readonly Lock Gate = new();

    public static bool Save(string file, DirNode root, DateTime savedUtc) => WriteSnapshot(file, Snapshot(root, savedUtc));

    // Uncompressed in-memory copy of the tree. Taken on the UI thread, the only thread that modifies the tree,
    // so nothing changes during the walk; WriteSnapshot compresses and writes it later in the background.
    // Returns the internal buffer without a copy: for large trees, ToArray copies tens of MB on the UI thread.
    public static ReadOnlyMemory<byte> Snapshot(DirNode root, DateTime savedUtc)
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(FormatVersion);
            writer.Write(savedUtc.Ticks);
            writer.Write(root.Path);
            Write(writer, root);
        }
        return memory.GetBuffer().AsMemory(0, (int)memory.Length);
    }

    // Writes to a temp file and renames it, so if the app exits midway the previous copy is still whole.
    // The copy is optional: if it cannot be written (disk full, antivirus, another instance) this returns false
    // and the app goes on; only the fast load on the next startup is lost.
    public static bool WriteSnapshot(string file, ReadOnlyMemory<byte> snapshot)
    {
        // Per-process temp file, so two instances scanning the same folders do not write to the same file.
        string temp = $"{file}.{Environment.ProcessId}.tmp";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.WriteThrough))
                using (var stream = new BrotliStream(output, CompressionLevel.Fastest))
                    stream.Write(snapshot.Span);
                File.Move(temp, file, overwrite: true);
                PruneStale(file);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(temp);
                return false;
            }
        }
    }

    // Each folder combination leaves its own copy and nothing else deletes them: this removes copies unused for 30 days
    // and temp files from processes that died midway. A temp file under one day old may belong to another instance still writing.
    static void PruneStale(string keep)
    {
        var now = DateTime.UtcNow;
        try
        {
            foreach (var other in Directory.EnumerateFiles(Path.GetDirectoryName(keep)!, "scan-*"))
            {
                var age = now - File.GetLastWriteTimeUtc(other);
                bool stale = other.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ? age > TimeSpan.FromDays(1)
                    : other.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) && age > TimeSpan.FromDays(30);
                if (stale && !string.Equals(other, keep, StringComparison.OrdinalIgnoreCase)) TryDelete(other);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is optional: the new copy is already saved, and the next save tries again.
        }
    }

    static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If it cannot be deleted either, this process overwrites it on its next save.
        }
    }

    static void Write(BinaryWriter writer, DirNode node)
    {
        var flags = NodeTraits.None;
        if (node.IsFiles) flags |= NodeTraits.Files;
        if (node.HasRepo) flags |= NodeTraits.Repo;
        if (node.ContainsRepo) flags |= NodeTraits.ContainsRepo;
        if (node.IsPartial) flags |= NodeTraits.Partial;
        writer.Write(node.Name);
        writer.Write((byte)flags);
        writer.Write(node.LastWriteUtc.Ticks);
        writer.Write(node.NewestWriteUtc.Ticks);
        writer.Write(node.Size);
        writer.Write(node.FileCount);
        writer.Write(node.ChildrenOrEmpty.Count);
        foreach (var child in node.ChildrenOrEmpty) Write(writer, child);
    }

    // null if there is no copy or it has another format or is corrupt; in each case the caller scans from scratch.
    public static (DirNode Root, DateTime SavedUtc)? Load(string file)
    {
        if (!File.Exists(file)) return null;
        try
        {
            // Buffered: BinaryReader reads 1 to 8 bytes at a time, and each unbuffered read would call into the native decompressor.
            using var reader = new BinaryReader(new BufferedStream(new BrotliStream(File.OpenRead(file), CompressionMode.Decompress), 1 << 16), Encoding.UTF8);
            if (reader.ReadInt32() != FormatVersion) return null;
            var saved = new DateTime(reader.ReadInt64(), DateTimeKind.Utc);
            string rootPath = reader.ReadString();
            return (Read(reader, null, rootPath), saved);
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException or ArgumentException or EndOfStreamException)
        {
            // Corrupt copy. Brotli has no checksum, so a few damaged bytes can decompress into garbage
            // and fail when a field is read. It is deleted so every later startup does not hit it again.
            TryDelete(file);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked copy: scan from scratch; the next save replaces it.
            return null;
        }
    }

    static DirNode Read(BinaryReader reader, DirNode? parent, string? rootPath)
    {
        string name = reader.ReadString();
        var flags = (NodeTraits)reader.ReadByte();
        bool isFiles = flags.HasFlag(NodeTraits.Files);
        string path = parent switch
        {
            null => rootPath!,
            _ when isFiles => parent.Path,
            // Children of "Este equipo" are drives, whose path is "C:\".
            { Path.Length: 0 } => name + Path.DirectorySeparatorChar,
            _ => Path.Join(parent.Path, name),
        };
        var node = new DirNode
        {
            // Both names are UI text, saved in the language of the scan that wrote the copy.
            Name = isFiles ? DirNode.FilesName : parent is null && path.Length == 0 ? DiskScanner.ComputerName : name,
            Path = path,
            Parent = parent,
            IsFiles = isFiles,
            HasRepo = flags.HasFlag(NodeTraits.Repo),
            ContainsRepo = flags.HasFlag(NodeTraits.ContainsRepo),
            IsPartial = flags.HasFlag(NodeTraits.Partial),
            LastWriteUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc),
            NewestWriteUtc = new DateTime(reader.ReadInt64(), DateTimeKind.Utc),
            Size = reader.ReadInt64(),
            FileCount = reader.ReadInt64(),
        };
        int count = reader.ReadInt32();
        if (count < 0) throw new InvalidDataException("Negative child count.");
        for (int i = 0; i < count; i++) node.Children.Add(Read(reader, node, null));
        return node;
    }
}
