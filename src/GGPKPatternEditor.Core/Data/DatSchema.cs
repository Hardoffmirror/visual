using System.Text.Json;

namespace GGPKPatternEditor.Core.Data;

/// <summary>
/// Loads and manages DAT file schema definitions
/// </summary>
public class DatSchema
{
    private static Dictionary<string, Dictionary<string, string>>? _definitions;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets column definitions for a specific DAT file
    /// </summary>
    public static Dictionary<string, string>? GetTableDefinition(string tableName)
    {
        EnsureLoaded();

        // Try exact match first
        if (_definitions!.TryGetValue(tableName, out var def))
            return def;

        // Try without extension
        string nameWithoutExt = Path.GetFileNameWithoutExtension(tableName);
        if (_definitions.TryGetValue(nameWithoutExt, out def))
            return def;

        return null;
    }

    /// <summary>
    /// Gets all available table names
    /// </summary>
    public static IEnumerable<string> GetTableNames()
    {
        EnsureLoaded();
        return _definitions!.Keys;
    }

    private static void EnsureLoaded()
    {
        if (_definitions != null) return;

        lock (_lock)
        {
            if (_definitions != null) return;

            _definitions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            // Try to load from embedded or external file
            string? schemaPath = FindSchemaFile();
            if (schemaPath != null && File.Exists(schemaPath))
            {
                try
                {
                    string json = File.ReadAllText(schemaPath);
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                    if (parsed != null)
                    {
                        _definitions = new Dictionary<string, Dictionary<string, string>>(
                            parsed, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // Failed to load, use empty definitions
                }
            }
        }
    }

    private static string? FindSchemaFile()
    {
        // Look for DatDefinitions.json in various locations
        string[] searchPaths = {
            "DatDefinitions.json",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DatDefinitions.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "DatDefinitions.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "GGPKPatternEditor", "DatDefinitions.json")
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Loads schema from a specific file
    /// </summary>
    public static void LoadFromFile(string filePath)
    {
        lock (_lock)
        {
            string json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
            if (parsed != null)
            {
                _definitions = new Dictionary<string, Dictionary<string, string>>(
                    parsed, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Clears loaded definitions
    /// </summary>
    public static void Clear()
    {
        lock (_lock)
        {
            _definitions = null;
        }
    }
}
