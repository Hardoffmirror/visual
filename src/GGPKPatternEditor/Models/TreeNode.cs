using System.Collections.ObjectModel;
using GGPKPatternEditor.Core.GGPK;

namespace GGPKPatternEditor.Models;

/// <summary>
/// Represents a node in the file tree (folder or file)
/// </summary>
public class TreeNode
{
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public GGPKRecord? Record { get; set; }
    public ObservableCollection<TreeNode> Children { get; } = new();

    public TreeNode() { }

    public TreeNode(string name, bool isDirectory)
    {
        Name = name;
        IsDirectory = isDirectory;
    }

    /// <summary>
    /// Builds a tree structure from a flat list of records
    /// </summary>
    public static ObservableCollection<TreeNode> BuildTree(IEnumerable<GGPKRecord> files)
    {
        var root = new TreeNode("Root", true);
        var nodeCache = new Dictionary<string, TreeNode>();

        foreach (var file in files)
        {
            string[] parts = file.FullPath.Split('/');
            TreeNode current = root;
            string currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (string.IsNullOrEmpty(part)) continue;

                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                bool isFile = (i == parts.Length - 1);

                if (!nodeCache.TryGetValue(currentPath, out TreeNode? node))
                {
                    node = new TreeNode
                    {
                        Name = part,
                        FullPath = currentPath,
                        IsDirectory = !isFile,
                        Record = isFile ? file : null
                    };
                    nodeCache[currentPath] = node;
                    current.Children.Add(node);
                }

                current = node;
            }
        }

        // Sort children: directories first, then by name
        SortChildren(root);

        return root.Children;
    }

    private static void SortChildren(TreeNode node)
    {
        if (node.Children.Count == 0) return;

        var sorted = node.Children
            .OrderByDescending(n => n.IsDirectory)
            .ThenBy(n => n.Name)
            .ToList();

        node.Children.Clear();
        foreach (var child in sorted)
        {
            node.Children.Add(child);
            if (child.IsDirectory)
            {
                SortChildren(child);
            }
        }
    }
}
