using System.IO.Enumeration;

namespace FileScanner.Core;

// Folder name search in the scanned tree. The tree stores no files, so only folders are searched.
public static class FolderSearch
{
    // All folders in the tree. Captured on the UI thread (the only one that changes the tree); the search
    // walks this list on a background thread.
    public static List<DirNode> Folders(DirNode root)
    {
        var all = new List<DirNode>();
        var stack = new Stack<DirNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.IsFiles) continue;
            all.Add(node);
            foreach (var child in node.ChildrenOrEmpty) stack.Push(child);
        }
        return all;
    }

    // The count largest folders whose name contains the text, case-insensitive.
    // With * or ? the text is a pattern over the whole name, as in Explorer (node_*, *.git).
    public static List<DirNode> Find(IReadOnlyList<DirNode> folders, string text, int count, CancellationToken token = default)
    {
        text = text.Trim();
        if (text.Length == 0 || count <= 0) return [];
        bool pattern = text.AsSpan().IndexOfAny('*', '?') >= 0;
        var top = new PriorityQueue<DirNode, long>(count + 1);
        for (int i = 0; i < folders.Count; i++)
        {
            if ((i & 0xFFFF) == 0) token.ThrowIfCancellationRequested();
            var node = folders[i];
            bool match = pattern
                ? FileSystemName.MatchesSimpleExpression(text, node.Name)
                : node.Name.Contains(text, StringComparison.OrdinalIgnoreCase);
            if (!match) continue;
            top.Enqueue(node, node.Size);
            if (top.Count > count) top.Dequeue();
        }
        return [.. top.UnorderedItems.Select(i => i.Element).OrderByDescending(n => n.Size).ThenBy(n => n.Path, StringComparer.OrdinalIgnoreCase)];
    }
}
