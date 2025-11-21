using System.Text;

namespace GGPKPatternEditor.Core.GGPK;

/// <summary>
/// Represents a GGPK file container used by Path of Exile
/// </summary>
public class GGPKFile : IDisposable
{
    private FileStream? _stream;
    private BinaryReader? _reader;

    public string FilePath { get; private set; } = string.Empty;
    public GGPKRecord? Root { get; private set; }
    public List<GGPKRecord> AllRecords { get; } = new();
    public Dictionary<string, GGPKRecord> FileIndex { get; } = new();
    public bool IsLoaded => _stream != null;

    /// <summary>
    /// Opens and parses a GGPK file
    /// </summary>
    public async Task<bool> LoadAsync(string filePath, IProgress<string>? progress = null)
    {
        try
        {
            FilePath = filePath;
            progress?.Report($"Opening file: {filePath}");

            _stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            _reader = new BinaryReader(_stream);

            // Read GGPK header
            progress?.Report("Reading GGPK header...");
            await Task.Run(() => ParseRecords(progress));

            progress?.Report($"Loaded {AllRecords.Count} records");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"Error: {ex.Message}");
            return false;
        }
    }

    private void ParseRecords(IProgress<string>? progress)
    {
        if (_reader == null || _stream == null) return;

        _stream.Position = 0;
        long fileLength = _stream.Length;

        // First record should be GGPK
        var firstRecord = ReadRecord(0);
        if (firstRecord?.Tag != "GGPK")
        {
            throw new InvalidDataException("Invalid GGPK file - missing GGPK header");
        }

        AllRecords.Add(firstRecord);

        // Parse all records referenced by GGPK header
        if (firstRecord.ChildOffsets != null)
        {
            foreach (var offset in firstRecord.ChildOffsets)
            {
                ParseRecordTree(offset, "", progress);
            }
        }

        // Build file index
        BuildFileIndex(Root, "");
    }

    private void ParseRecordTree(long offset, string parentPath, IProgress<string>? progress)
    {
        if (_reader == null || _stream == null) return;

        var record = ReadRecord(offset);
        if (record == null) return;

        AllRecords.Add(record);

        string currentPath = string.IsNullOrEmpty(parentPath)
            ? record.Name
            : $"{parentPath}/{record.Name}";
        record.FullPath = currentPath;

        if (record.Tag == "PDIR")
        {
            if (Root == null && string.IsNullOrEmpty(record.Name))
            {
                Root = record;
            }

            // Parse children
            if (record.ChildOffsets != null)
            {
                foreach (var childOffset in record.ChildOffsets)
                {
                    ParseRecordTree(childOffset, currentPath, progress);
                }
            }
        }
    }

    private GGPKRecord? ReadRecord(long offset)
    {
        if (_reader == null || _stream == null) return null;

        _stream.Position = offset;

        var record = new GGPKRecord
        {
            Offset = offset,
            Length = _reader.ReadInt32()
        };

        byte[] tagBytes = _reader.ReadBytes(4);
        record.Tag = Encoding.ASCII.GetString(tagBytes);

        switch (record.Tag)
        {
            case "GGPK":
                int version = _reader.ReadInt32();
                int numOffsets = (record.Length - 12) / 8;
                record.ChildOffsets = new long[numOffsets];
                for (int i = 0; i < numOffsets; i++)
                {
                    record.ChildOffsets[i] = _reader.ReadInt64();
                }
                break;

            case "PDIR":
                int nameLength = _reader.ReadInt32();
                int childCount = _reader.ReadInt32();
                record.Hash = _reader.ReadBytes(32);

                if (nameLength > 0)
                {
                    byte[] nameBytes = _reader.ReadBytes((nameLength - 1) * 2);
                    record.Name = Encoding.Unicode.GetString(nameBytes);
                    _reader.ReadBytes(2); // null terminator
                }

                record.ChildOffsets = new long[childCount];
                for (int i = 0; i < childCount; i++)
                {
                    _reader.ReadInt32(); // name hash
                    record.ChildOffsets[i] = _reader.ReadInt64();
                }
                break;

            case "FILE":
                int fileNameLength = _reader.ReadInt32();
                record.Hash = _reader.ReadBytes(32);

                if (fileNameLength > 0)
                {
                    byte[] nameBytes = _reader.ReadBytes((fileNameLength - 1) * 2);
                    record.Name = Encoding.Unicode.GetString(nameBytes);
                    _reader.ReadBytes(2); // null terminator
                }

                record.DataOffset = _stream.Position;
                record.DataLength = record.Length - (int)(_stream.Position - offset);
                break;

            case "FREE":
                // Free space record
                break;
        }

        return record;
    }

    private void BuildFileIndex(GGPKRecord? record, string path)
    {
        if (record == null) return;

        string currentPath = string.IsNullOrEmpty(path)
            ? record.Name
            : $"{path}/{record.Name}";

        if (record.Tag == "FILE")
        {
            FileIndex[currentPath.ToLowerInvariant()] = record;
        }
    }

    /// <summary>
    /// Reads the content of a file record
    /// </summary>
    public byte[]? ReadFileContent(GGPKRecord record)
    {
        if (_stream == null || record.Tag != "FILE") return null;

        _stream.Position = record.DataOffset;
        return new BinaryReader(_stream).ReadBytes(record.DataLength);
    }

    /// <summary>
    /// Writes content to a file record (same size only)
    /// </summary>
    public bool WriteFileContent(GGPKRecord record, byte[] content)
    {
        if (_stream == null || record.Tag != "FILE") return false;
        if (content.Length != record.DataLength)
        {
            throw new ArgumentException($"Content size mismatch. Expected {record.DataLength}, got {content.Length}");
        }

        _stream.Position = record.DataOffset;
        _stream.Write(content, 0, content.Length);
        _stream.Flush();
        return true;
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
        _reader?.Dispose();
        _stream?.Dispose();
        _reader = null;
        _stream = null;
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

    public override string ToString() => $"[{Tag}] {Name} @ {Offset}";
}
