using System.Text.Json;
using System.Text.Json.Serialization;
using GGPKPatternEditor.Core.Pattern;

namespace GGPKPatternEditor.Core.Settings;

/// <summary>
/// Manages loading and saving of pattern settings/configurations
/// </summary>
public class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Loads pattern configuration from a JSON file
    /// </summary>
    public async Task<PatternConfiguration> LoadConfigurationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Configuration file not found: {filePath}");
        }

        string json = await File.ReadAllTextAsync(filePath);
        var config = JsonSerializer.Deserialize<PatternConfiguration>(json, JsonOptions);

        if (config == null)
        {
            throw new InvalidDataException("Failed to parse configuration file");
        }

        return config;
    }

    /// <summary>
    /// Saves pattern configuration to a JSON file
    /// </summary>
    public async Task SaveConfigurationAsync(PatternConfiguration config, string filePath)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    /// <summary>
    /// Creates a default/example configuration
    /// </summary>
    public PatternConfiguration CreateDefaultConfiguration()
    {
        return new PatternConfiguration
        {
            Name = "My Pattern Configuration",
            Description = "Custom patterns for GGPK editing",
            Version = "1.0",
            Rules = new List<PatternRuleConfig>
            {
                new PatternRuleConfig
                {
                    Name = "Example Text Replace",
                    Description = "Example of text search and replace",
                    Search = new SearchPatternConfig
                    {
                        SearchText = "OldValue",
                        IsRegex = false,
                        CaseSensitive = true
                    },
                    Replace = "NewValue",
                    FilePatterns = new List<string> { "*.txt", "*.json" },
                    Enabled = true
                },
                new PatternRuleConfig
                {
                    Name = "Example Hex Pattern",
                    Description = "Example of binary pattern search",
                    Search = new SearchPatternConfig
                    {
                        HexPattern = "48 65 6C 6C 6F", // "Hello" in hex
                        CaseSensitive = true
                    },
                    Replace = "57 6F 72 6C 64", // "World" in hex
                    FilePatterns = new List<string> { "*.dat" },
                    Enabled = false
                },
                new PatternRuleConfig
                {
                    Name = "Example Regex",
                    Description = "Example of regex pattern",
                    Search = new SearchPatternConfig
                    {
                        SearchText = @"value\s*=\s*\d+",
                        IsRegex = true,
                        CaseSensitive = false
                    },
                    Replace = "value = 100",
                    FilePatterns = new List<string> { "*.cfg", "*.ini" },
                    Enabled = false
                }
            }
        };
    }

    /// <summary>
    /// Validates a configuration
    /// </summary>
    public List<string> ValidateConfiguration(PatternConfiguration config)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(config.Name))
        {
            errors.Add("Configuration name is required");
        }

        for (int i = 0; i < config.Rules.Count; i++)
        {
            var rule = config.Rules[i];

            if (string.IsNullOrWhiteSpace(rule.Name))
            {
                errors.Add($"Rule #{i + 1}: Name is required");
            }

            if (string.IsNullOrWhiteSpace(rule.Search.SearchText) &&
                string.IsNullOrWhiteSpace(rule.Search.HexPattern))
            {
                errors.Add($"Rule '{rule.Name}': Search pattern is required");
            }

            if (rule.Search.IsRegex && !string.IsNullOrEmpty(rule.Search.SearchText))
            {
                try
                {
                    _ = new System.Text.RegularExpressions.Regex(rule.Search.SearchText);
                }
                catch (Exception ex)
                {
                    errors.Add($"Rule '{rule.Name}': Invalid regex - {ex.Message}");
                }
            }

            if (!string.IsNullOrEmpty(rule.Search.HexPattern))
            {
                try
                {
                    PatternMatcher.ParseHexPattern(rule.Search.HexPattern);
                }
                catch (Exception ex)
                {
                    errors.Add($"Rule '{rule.Name}': Invalid hex pattern - {ex.Message}");
                }
            }
        }

        return errors;
    }
}

/// <summary>
/// Configuration file structure
/// </summary>
public class PatternConfiguration
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public List<PatternRuleConfig> Rules { get; set; } = new();
}

/// <summary>
/// Pattern rule as stored in configuration
/// </summary>
public class PatternRuleConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SearchPatternConfig Search { get; set; } = new();
    public string Replace { get; set; } = string.Empty;
    public List<string> FilePatterns { get; set; } = new();
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Converts to a PatternRule for the matcher
    /// </summary>
    public PatternRule ToPatternRule()
    {
        var searchPattern = new SearchPattern
        {
            SearchText = Search.SearchText,
            IsRegex = Search.IsRegex,
            CaseSensitive = Search.CaseSensitive
        };

        if (!string.IsNullOrEmpty(Search.HexPattern))
        {
            searchPattern.BinaryPattern = PatternMatcher.ParseHexPattern(Search.HexPattern);
        }

        return new PatternRule
        {
            Name = Name,
            Description = Description,
            Search = searchPattern,
            Replace = Replace,
            FilePatterns = FilePatterns,
            Enabled = Enabled
        };
    }
}

/// <summary>
/// Search pattern configuration
/// </summary>
public class SearchPatternConfig
{
    public string SearchText { get; set; } = string.Empty;
    public string HexPattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; } = true;
}
