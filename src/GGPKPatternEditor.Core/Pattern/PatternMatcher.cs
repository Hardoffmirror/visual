using System.Text;
using System.Text.RegularExpressions;

namespace GGPKPatternEditor.Core.Pattern;

/// <summary>
/// Pattern matching engine for searching and replacing content in files
/// </summary>
public class PatternMatcher
{
    /// <summary>
    /// Searches for a pattern in binary data
    /// </summary>
    public List<PatternMatch> FindPattern(byte[] data, SearchPattern pattern)
    {
        var matches = new List<PatternMatch>();

        if (pattern.IsBinaryPattern)
        {
            matches.AddRange(FindBinaryPattern(data, pattern.BinaryPattern!));
        }
        else if (pattern.IsRegex)
        {
            string text = Encoding.UTF8.GetString(data);
            var regex = new Regex(pattern.SearchText,
                pattern.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);

            foreach (Match match in regex.Matches(text))
            {
                matches.Add(new PatternMatch
                {
                    Offset = match.Index,
                    Length = match.Length,
                    MatchedText = match.Value,
                    Pattern = pattern
                });
            }
        }
        else
        {
            // Simple text search
            string text = Encoding.UTF8.GetString(data);
            string searchFor = pattern.CaseSensitive
                ? pattern.SearchText
                : pattern.SearchText.ToLowerInvariant();
            string searchIn = pattern.CaseSensitive
                ? text
                : text.ToLowerInvariant();

            int index = 0;
            while ((index = searchIn.IndexOf(searchFor, index)) != -1)
            {
                matches.Add(new PatternMatch
                {
                    Offset = index,
                    Length = pattern.SearchText.Length,
                    MatchedText = text.Substring(index, pattern.SearchText.Length),
                    Pattern = pattern
                });
                index++;
            }
        }

        return matches;
    }

    /// <summary>
    /// Searches for a binary pattern (hex bytes with wildcards)
    /// </summary>
    private List<PatternMatch> FindBinaryPattern(byte[] data, byte?[] pattern)
    {
        var matches = new List<PatternMatch>();

        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (pattern[j].HasValue && data[i + j] != pattern[j].Value)
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                byte[] matchedBytes = new byte[pattern.Length];
                Array.Copy(data, i, matchedBytes, 0, pattern.Length);

                matches.Add(new PatternMatch
                {
                    Offset = i,
                    Length = pattern.Length,
                    MatchedBytes = matchedBytes,
                    Pattern = new SearchPattern { BinaryPattern = pattern }
                });
            }
        }

        return matches;
    }

    /// <summary>
    /// Applies a replacement to data at the specified match
    /// </summary>
    public byte[] ApplyReplacement(byte[] data, PatternMatch match, string replacement)
    {
        if (match.Pattern?.IsBinaryPattern == true)
        {
            // Binary replacement
            byte[] replaceBytes = ParseHexString(replacement);
            return ApplyBinaryReplacement(data, match.Offset, match.Length, replaceBytes);
        }
        else
        {
            // Text replacement
            string text = Encoding.UTF8.GetString(data);
            string result = text.Substring(0, match.Offset) + replacement +
                           text.Substring(match.Offset + match.Length);
            return Encoding.UTF8.GetBytes(result);
        }
    }

    /// <summary>
    /// Applies all replacements from a pattern rule
    /// </summary>
    public byte[] ApplyAllReplacements(byte[] data, PatternRule rule)
    {
        var matches = FindPattern(data, rule.Search);

        if (matches.Count == 0) return data;

        // Apply replacements in reverse order to maintain offsets
        byte[] result = data;
        foreach (var match in matches.OrderByDescending(m => m.Offset))
        {
            result = ApplyReplacement(result, match, rule.Replace);
        }

        return result;
    }

    private byte[] ApplyBinaryReplacement(byte[] data, int offset, int length, byte[] replacement)
    {
        byte[] result = new byte[data.Length - length + replacement.Length];
        Array.Copy(data, 0, result, 0, offset);
        Array.Copy(replacement, 0, result, offset, replacement.Length);
        Array.Copy(data, offset + length, result, offset + replacement.Length,
                   data.Length - offset - length);
        return result;
    }

    /// <summary>
    /// Parses a hex string like "48 65 6C 6C 6F" or "48656C6C6F" into bytes
    /// Supports ?? for wildcards (returns null in array)
    /// </summary>
    public static byte?[] ParseHexPattern(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        var result = new byte?[hex.Length / 2];

        for (int i = 0; i < hex.Length; i += 2)
        {
            string byteStr = hex.Substring(i, 2);
            if (byteStr == "??" || byteStr == "**")
            {
                result[i / 2] = null; // wildcard
            }
            else
            {
                result[i / 2] = Convert.ToByte(byteStr, 16);
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a hex string into bytes (no wildcards)
    /// </summary>
    public static byte[] ParseHexString(string hex)
    {
        hex = hex.Replace(" ", "").Replace("-", "");
        byte[] result = new byte[hex.Length / 2];

        for (int i = 0; i < hex.Length; i += 2)
        {
            result[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        }

        return result;
    }
}

/// <summary>
/// Represents a search pattern
/// </summary>
public class SearchPattern
{
    public string SearchText { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public bool CaseSensitive { get; set; } = true;
    public byte?[]? BinaryPattern { get; set; }

    public bool IsBinaryPattern => BinaryPattern != null && BinaryPattern.Length > 0;
}

/// <summary>
/// Represents a match found by the pattern matcher
/// </summary>
public class PatternMatch
{
    public int Offset { get; set; }
    public int Length { get; set; }
    public string? MatchedText { get; set; }
    public byte[]? MatchedBytes { get; set; }
    public SearchPattern? Pattern { get; set; }
}

/// <summary>
/// Represents a search and replace rule
/// </summary>
public class PatternRule
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SearchPattern Search { get; set; } = new();
    public string Replace { get; set; } = string.Empty;
    public List<string> FilePatterns { get; set; } = new(); // e.g., "*.txt", "Data/*.dat"
    public bool Enabled { get; set; } = true;
}
