using LibGGPK3;
using LibGGPK3.Records;
using LibBundledGGPK3;

namespace GGPKPatternEditor.Core.GGPK;

/// <summary>
/// Represents a GGPK file container using LibGGPK3
/// </summary>
public class GGPKFile : IDisposable
{
    private GGPK? _ggpk;
    private BundledGGPK? _bundledGgpk;
    private bool _isBundled;

    public string FilePath { get; private set; } = string.Empty;
    public List<GGPKRecord> AllRecords { get; } = new();
    public Dictionary<string, GGPKRecord> FileIndex { get; } = new();
    public bool IsLoaded => _ggpk != null || _bundledGgpk != null;

    /// <summary>
    /// Opens and parses a GGPK file
    /// </summary>
    public async Task<bool> LoadAsync(string filePath, IProgress<string>? progress = null)
    {
        try
        {
            FilePath = filePath;
            progress?.Report($"Opening file: {filePath}");

            await Task.Run(() =>
            {
                // Try to open as bundled GGPK first (modern POE)
                try
                {
                    progress?.Report("Trying to load as bundled GGPK...");
                    _bundledGgpk = new BundledGGPK(filePath, true);
                    _isBundled = true;

                    progress?.Report("Parsing file paths...");
                    _bundledGgpk.Index.ParsePaths();

                    progress?.Report("Building file tree...");
                    var root = _bundledGgpk.Index.BuildTree(true);

                    progress?.Report("Indexing files...");
                    IndexBundledFiles(root, "", progress);
                }
                catch
                {
                    // Fall back to legacy GGPK
                    _bundledGgpk?.Dispose();
                    _bundledGgpk = null;

                    progress?.Report("Loading as legacy GGPK...");
                    _ggpk = new GGPK(filePath);
                    _isBundled = false;

                    progress?.Report("Indexing files...");
                    IndexLegacyFiles(_ggpk.Root, "", progress);
                }
            });

            progress?.Report($"Loaded {AllRecords.Count} files");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    private int _recordCount;
    private DateTime _lastProgressReport = DateTime.MinValue;

    private void IndexBundledFiles(LibBundle3.Index.TreeNode? node, string parentPath, IProgress<string>? progress)
    {
        if (node == null) return;

        string currentPath = string.IsNullOrEmpty(parentPath) && string.IsNullOrEmpty(node.Name)
            ? ""
            : string.IsNullOrEmpty(parentPath)
                ? node.Name
                : $"{parentPath}/{node.Name}";

        if (node.Children != null)
        {
            // Directory
            foreach (var child in node.Children)
            {
                IndexBundledFiles(child, currentPath, progress);
            }
        }
        else
        {
            // File
            var record = new GGPKRecord
            {
                Name = node.Name,
                FullPath = currentPath,
                Tag = "FILE",
                BundledNode = node
            };
            AllRecords.Add(record);
            FileIndex[currentPath.ToLowerInvariant()] = record;

            _recordCount++;
            if ((DateTime.Now - _lastProgressReport).TotalMilliseconds > 100)
            {
                progress?.Report($"Indexing... {_recordCount} files");
                _lastProgressReport = DateTime.Now;
            }
        }
    }

    private void IndexLegacyFiles(DirectoryRecord? dir, string parentPath, IProgress<string>? progress)
    {
        if (dir == null) return;

        string currentPath = string.IsNullOrEmpty(parentPath) && string.IsNullOrEmpty(dir.Name)
            ? ""
            : string.IsNullOrEmpty(parentPath)
                ? dir.Name
                : $"{parentPath}/{dir.Name}";

        foreach (var child in dir)
        {
            if (child is DirectoryRecord subDir)
            {
                IndexLegacyFiles(subDir, currentPath, progress);
            }
            else if (child is FileRecord file)
            {
                string filePath = string.IsNullOrEmpty(currentPath)
                    ? file.Name
                    : $"{currentPath}/{file.Name}";

                var record = new GGPKRecord
                {
                    Name = file.Name,
                    FullPath = filePath,
                    Tag = "FILE",
                    LegacyRecord = file,
                    DataLength = file.DataLength
                };
                AllRecords.Add(record);
                FileIndex[filePath.ToLowerInvariant()] = record;

                _recordCount++;
                if ((DateTime.Now - _lastProgressReport).TotalMilliseconds > 100)
                {
                    progress?.Report($"Indexing... {_recordCount} files");
                    _lastProgressReport = DateTime.Now;
                }
            }
        }
    }

    /// <summary>
    /// Reads the content of a file record
    /// </summary>
    public byte[]? ReadFileContent(GGPKRecord record)
    {
        if (record.Tag != "FILE") return null;

        try
        {
            if (_isBundled && record.BundledNode != null && _bundledGgpk != null)
            {
                return _bundledGgpk.Index.GetFileContent(record.BundledNode);
            }
            else if (record.LegacyRecord != null)
            {
                return record.LegacyRecord.Read();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Writes content to a file record
    /// </summary>
    public bool WriteFileContent(GGPKRecord record, byte[] content)
    {
        if (record.Tag != "FILE") return false;

        try
        {
            if (_isBundled && record.BundledNode != null && _bundledGgpk != null)
            {
                // For bundled files, we need to replace through the index
                _bundledGgpk.Index.Replace(record.BundledNode, content);
                return true;
            }
            else if (record.LegacyRecord != null)
            {
                record.LegacyRecord.Write(content);
                return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Gets all FILE records
    /// </summary>
    public IEnumerable<GGPKRecord> GetAllFiles()
    {
        return AllRecords.Where(r => r.Tag == "FILE");
    }

    /// <summary>
    /// Finds a file by path
    /// </summary>
    public GGPKRecord? FindFile(string path)
    {
        return FileIndex.TryGetValue(path.ToLowerInvariant().Replace('\\', '/'), out var record)
            ? record
            : null;
    }

    public void Dispose()
    {
        _ggpk?.Dispose();
        _bundledGgpk?.Dispose();
        _ggpk = null;
        _bundledGgpk = null;
    }
}

/// <summary>
/// Represents a single record in a GGPK file
/// </summary>
public class GGPKRecord
{
    public long Offset { get; set; }
    public int Length { get; set; }
    public string Tag { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public byte[]? Hash { get; set; }
    public long[]? ChildOffsets { get; set; }
    public long DataOffset { get; set; }
    public int DataLength { get; set; }

    // LibGGPK3 references
    public FileRecord? LegacyRecord { get; set; }
    public LibBundle3.Index.TreeNode? BundledNode { get; set; }

    public override string ToString() => $"[{Tag}] {Name}";
}
