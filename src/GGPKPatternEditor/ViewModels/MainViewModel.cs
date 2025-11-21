using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGPKPatternEditor.Core.GGPK;
using GGPKPatternEditor.Core.Pattern;
using GGPKPatternEditor.Core.Settings;
using GGPKPatternEditor.Models;
using Microsoft.Win32;

namespace GGPKPatternEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GGPKFile _ggpkFile = new();
    private readonly SettingsManager _settingsManager = new();
    private readonly PatternMatcher _patternMatcher = new();

    [ObservableProperty]
    private string _statusMessage = "Ready. Open a GGPK file to begin.";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private string _ggpkFilePath = string.Empty;

    [ObservableProperty]
    private string _configFilePath = string.Empty;

    [ObservableProperty]
    private PatternConfiguration? _currentConfiguration;

    [ObservableProperty]
    private PatternRuleConfig? _selectedRule;

    [ObservableProperty]
    private GGPKRecord? _selectedFile;

    [ObservableProperty]
    private string _fileContent = string.Empty;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    public ObservableCollection<GGPKRecord> FilteredFiles { get; } = new();
    public ObservableCollection<TreeNode> FileTree { get; } = new();
    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    public bool IsFileLoaded => _ggpkFile.IsLoaded;
    public bool HasConfiguration => CurrentConfiguration != null;

    [RelayCommand]
    private async Task OpenGGPKFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "GGPK Files (*.ggpk)|*.ggpk|All Files (*.*)|*.*",
            Title = "Open GGPK File"
        };

        if (dialog.ShowDialog() == true)
        {
            IsLoading = true;
            StatusMessage = "Loading GGPK file...";

            var progress = new Progress<string>(msg => StatusMessage = msg);

            bool success = await _ggpkFile.LoadAsync(dialog.FileName, progress);

            if (success)
            {
                GgpkFilePath = dialog.FileName;
                StatusMessage = $"Building file tree...";

                // Build tree structure in background
                await Task.Run(() =>
                {
                    var tree = TreeNode.BuildTree(_ggpkFile.GetAllFiles());
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        FileTree.Clear();
                        foreach (var node in tree)
                        {
                            FileTree.Add(node);
                        }
                    });
                });

                StatusMessage = $"Loaded {_ggpkFile.AllRecords.Count} records from {Path.GetFileName(dialog.FileName)}";
                RefreshFileList();
                OnPropertyChanged(nameof(IsFileLoaded));
            }
            else
            {
                MessageBox.Show("Failed to load GGPK file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task LoadConfiguration()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Load Pattern Configuration"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                CurrentConfiguration = await _settingsManager.LoadConfigurationAsync(dialog.FileName);
                ConfigFilePath = dialog.FileName;

                var errors = _settingsManager.ValidateConfiguration(CurrentConfiguration);
                if (errors.Count > 0)
                {
                    MessageBox.Show(
                        $"Configuration has warnings:\n\n{string.Join("\n", errors)}",
                        "Validation Warnings",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                StatusMessage = $"Loaded configuration: {CurrentConfiguration.Name} ({CurrentConfiguration.Rules.Count} rules)";
                OnPropertyChanged(nameof(HasConfiguration));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load configuration: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task SaveConfiguration()
    {
        if (CurrentConfiguration == null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            Title = "Save Pattern Configuration",
            FileName = string.IsNullOrEmpty(ConfigFilePath)
                ? "patterns.json"
                : Path.GetFileName(ConfigFilePath)
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                await _settingsManager.SaveConfigurationAsync(CurrentConfiguration, dialog.FileName);
                ConfigFilePath = dialog.FileName;
                StatusMessage = $"Configuration saved to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private async Task CreateDefaultConfiguration()
    {
        CurrentConfiguration = _settingsManager.CreateDefaultConfiguration();
        ConfigFilePath = string.Empty;
        StatusMessage = "Created new default configuration";
        OnPropertyChanged(nameof(HasConfiguration));
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task SearchPatterns()
    {
        if (!_ggpkFile.IsLoaded || CurrentConfiguration == null)
        {
            MessageBox.Show("Please load a GGPK file and configuration first", "Warning",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        SearchResults.Clear();
        StatusMessage = "Searching for patterns...";

        await Task.Run(() =>
        {
            var files = _ggpkFile.GetAllFiles().ToList();
            int processed = 0;

            foreach (var file in files)
            {
                foreach (var ruleConfig in CurrentConfiguration.Rules.Where(r => r.Enabled))
                {
                    // Check if file matches any pattern
                    if (!FileMatchesPatterns(file.FullPath, ruleConfig.FilePatterns))
                        continue;

                    var rule = ruleConfig.ToPatternRule();
                    byte[]? content = _ggpkFile.ReadFileContent(file);
                    if (content == null) continue;

                    var matches = _patternMatcher.FindPattern(content, rule.Search);

                    foreach (var match in matches)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            SearchResults.Add(new SearchResult
                            {
                                FilePath = file.FullPath,
                                RuleName = rule.Name,
                                Offset = match.Offset,
                                MatchedText = match.MatchedText ?? BitConverter.ToString(match.MatchedBytes ?? Array.Empty<byte>()),
                                Record = file,
                                Rule = ruleConfig
                            });
                        });
                    }
                }

                processed++;
                if (processed % 100 == 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ProgressValue = (int)((double)processed / files.Count * 100);
                        StatusMessage = $"Searching... {processed}/{files.Count} files";
                    });
                }
            }
        });

        StatusMessage = $"Search complete. Found {SearchResults.Count} matches";
        ProgressValue = 0;
        IsLoading = false;
    }

    [RelayCommand]
    private async Task ApplyAllReplacements()
    {
        if (SearchResults.Count == 0)
        {
            MessageBox.Show("No search results to apply", "Warning",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Apply {SearchResults.Count} replacements?\n\nThis will modify the GGPK file.",
            "Confirm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        StatusMessage = "Applying replacements...";

        int applied = 0;
        int failed = 0;

        // Group by file
        var groupedResults = SearchResults.GroupBy(r => r.FilePath).ToList();

        await Task.Run(() =>
        {
            foreach (var group in groupedResults)
            {
                var file = group.First().Record;
                if (file == null) continue;

                byte[]? content = _ggpkFile.ReadFileContent(file);
                if (content == null)
                {
                    failed += group.Count();
                    continue;
                }

                try
                {
                    // Apply all rules for this file
                    byte[] modifiedContent = content;
                    foreach (var searchResult in group)
                    {
                        if (searchResult.Rule == null) continue;
                        var rule = searchResult.Rule.ToPatternRule();
                        modifiedContent = _patternMatcher.ApplyAllReplacements(modifiedContent, rule);
                    }

                    // Only write if size matches (limitation of current implementation)
                    if (modifiedContent.Length == content.Length)
                    {
                        _ggpkFile.WriteFileContent(file, modifiedContent);
                        applied += group.Count();
                    }
                    else
                    {
                        // Size changed - would need more complex handling
                        failed += group.Count();
                    }
                }
                catch
                {
                    failed += group.Count();
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Applying... {applied + failed}/{SearchResults.Count}";
                });
            }
        });

        StatusMessage = $"Applied {applied} replacements, {failed} failed";
        IsLoading = false;

        if (failed > 0)
        {
            MessageBox.Show(
                $"Some replacements failed ({failed}). This usually happens when the replacement changes the file size.",
                "Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task ViewFileContent()
    {
        if (SelectedFile == null || !_ggpkFile.IsLoaded) return;

        FileContent = "Loading...";
        StatusMessage = $"Loading {SelectedFile.Name}...";

        await Task.Run(() =>
        {
            byte[]? content = _ggpkFile.ReadFileContent(SelectedFile);
            if (content == null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    FileContent = "Unable to read file content";
                    StatusMessage = "Failed to read file";
                });
                return;
            }

            string result;
            int maxSize = 100 * 1024; // 100KB limit for display

            // Check if file is binary
            bool isBinary = IsBinaryFile(content, SelectedFile.Name);

            if (isBinary)
            {
                // Show hex dump for binary files (limited)
                int displaySize = Math.Min(content.Length, maxSize);
                result = FormatHexDump(content.Take(displaySize).ToArray());
                if (content.Length > maxSize)
                {
                    result += $"\n\n... Showing first {maxSize / 1024}KB of {content.Length / 1024}KB. Use Export to save full file.";
                }
            }
            else
            {
                // Text file
                if (content.Length > maxSize)
                {
                    result = Encoding.UTF8.GetString(content, 0, maxSize);
                    result += $"\n\n... Truncated. Showing first {maxSize / 1024}KB of {content.Length / 1024}KB.";
                }
                else
                {
                    result = Encoding.UTF8.GetString(content);
                }
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                FileContent = result;
                StatusMessage = $"Loaded {SelectedFile.Name} ({content.Length / 1024}KB)";
            });
        });
    }

    private bool IsBinaryFile(byte[] content, string fileName)
    {
        // Check by extension first
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        // Known text file extensions (POE specific + common)
        string[] textExtensions = {
            ".otc", ".hlsl", ".glsl", ".fx", ".shader", // Shaders
            ".txt", ".json", ".xml", ".html", ".htm", ".css", ".js", // Web/text
            ".lua", ".py", ".cs", ".cpp", ".c", ".h", ".hpp", // Code
            ".ini", ".cfg", ".config", ".yaml", ".yml", ".toml", // Config
            ".md", ".csv", ".tsv", ".log", // Data text
            ".ot", ".otx", ".oc", ".occ", ".oct", ".filter", // POE specific
            ".atlas", ".ais", ".aoc", ".arm", ".ast", ".at", ".bt", ".clt",
            ".dct", ".dgr", ".dlp", ".ecf", ".edp", ".env", ".epk", ".et",
            ".ffx", ".fmt", ".frag", ".gft", ".gt", ".idl", ".it", ".mat",
            ".mtp", ".mtx", ".ot", ".otc", ".pet", ".psg", ".red", ".rs",
            ".rtx", ".sm", ".tgr", ".tgt", ".tmd", ".trl", ".tsi", ".ttf",
            ".ui", ".vert"
        };

        if (textExtensions.Contains(ext))
            return false;

        // Known binary file extensions
        string[] binaryExtensions = {
            ".dat", ".dat64", ".datl", ".datl64", // POE data
            ".dds", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", // Images
            ".ogg", ".mp3", ".wav", ".bank", ".fsb", // Audio
            ".bin", ".bundle", ".bk2", ".usm", // Binary/video
            ".ttf", ".otf", ".woff", // Fonts
            ".zip", ".7z", ".rar" // Archives
        };

        if (binaryExtensions.Contains(ext))
            return true;

        // Check content for null bytes (common in binary)
        int checkLength = Math.Min(content.Length, 8192);
        int nullCount = 0;
        for (int i = 0; i < checkLength; i++)
        {
            if (content[i] == 0)
                nullCount++;
        }

        // If more than 10% null bytes, likely binary
        return nullCount > checkLength * 0.1;
    }

    [RelayCommand]
    private async Task ExportFile()
    {
        if (SelectedFile == null || !_ggpkFile.IsLoaded) return;

        var dialog = new SaveFileDialog
        {
            FileName = SelectedFile.Name,
            Title = "Export File"
        };

        if (dialog.ShowDialog() == true)
        {
            byte[]? content = _ggpkFile.ReadFileContent(SelectedFile);
            if (content != null)
            {
                await File.WriteAllBytesAsync(dialog.FileName, content);
                StatusMessage = $"Exported {SelectedFile.Name}";
            }
        }
    }

    partial void OnSearchFilterChanged(string value)
    {
        _ = RefreshFileListAsync();
    }

    private async Task RefreshFileListAsync()
    {
        if (!_ggpkFile.IsLoaded) return;

        var filesList = await Task.Run(() =>
        {
            var files = _ggpkFile.GetAllFiles();

            if (!string.IsNullOrWhiteSpace(SearchFilter))
            {
                string filter = SearchFilter.ToLowerInvariant();
                files = files.Where(f => f.FullPath.ToLowerInvariant().Contains(filter));
            }

            return files.Take(500).ToList(); // Reduced limit for better performance
        });

        FilteredFiles.Clear();
        foreach (var file in filesList)
        {
            FilteredFiles.Add(file);
        }

        int totalCount = _ggpkFile.GetAllFiles().Count();
        if (totalCount > 500)
        {
            StatusMessage = $"Showing first 500 of {totalCount} files. Use filter to narrow down.";
        }
    }

    private void RefreshFileList()
    {
        _ = RefreshFileListAsync();
    }

    private bool FileMatchesPatterns(string filePath, List<string> patterns)
    {
        if (patterns.Count == 0) return true;

        foreach (var pattern in patterns)
        {
            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*\\*", ".*")
                .Replace("\\*", "[^/]*")
                .Replace("\\?", ".") + "$";

            if (Regex.IsMatch(filePath, regexPattern, RegexOptions.IgnoreCase))
                return true;
        }

        return false;
    }

    private string FormatHexDump(byte[] data, int bytesPerLine = 16)
    {
        var sb = new StringBuilder();
        int maxLines = Math.Min(data.Length / bytesPerLine + 1, 100);

        for (int i = 0; i < maxLines * bytesPerLine && i < data.Length; i += bytesPerLine)
        {
            sb.Append($"{i:X8}  ");

            int lineLength = Math.Min(bytesPerLine, data.Length - i);
            for (int j = 0; j < lineLength; j++)
            {
                sb.Append($"{data[i + j]:X2} ");
            }

            sb.Append(new string(' ', (bytesPerLine - lineLength) * 3 + 2));

            for (int j = 0; j < lineLength; j++)
            {
                char c = (char)data[i + j];
                sb.Append(char.IsControl(c) ? '.' : c);
            }

            sb.AppendLine();
        }

        if (data.Length > maxLines * bytesPerLine)
        {
            sb.AppendLine($"... ({data.Length - maxLines * bytesPerLine} more bytes)");
        }

        return sb.ToString();
    }
}

public class SearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public int Offset { get; set; }
    public string MatchedText { get; set; } = string.Empty;
    public GGPKRecord? Record { get; set; }
    public PatternRuleConfig? Rule { get; set; }
}
