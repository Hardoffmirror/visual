using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GGPKPatternEditor.Core.Data;
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

    [ObservableProperty]
    private DataView? _datTableView;

    [ObservableProperty]
    private bool _isDatFile;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editableContent = string.Empty;

    [ObservableProperty]
    private bool _isImageFile;

    [ObservableProperty]
    private System.Windows.Media.Imaging.BitmapImage? _imageSource;

    private DatFile? _currentDatFile;
    private byte[]? _currentFileContent;

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
        IsEditing = false;
        IsDatFile = false;
        IsImageFile = false;
        DatTableView = null;
        ImageSource = null;

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

            _currentFileContent = content;
            string ext = Path.GetExtension(SelectedFile.Name).ToLowerInvariant();

            // Detect actual file type by magic bytes first
            string detectedType = DetectFileType(content, ext);

            // Check if it's a DAT file - show as table
            if (detectedType == "dat" || ext == ".dat" || ext == ".dat64" || ext == ".datl" || ext == ".datl64")
            {
                var datFile = DatFile.Parse(content, SelectedFile.Name);
                _currentDatFile = datFile;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsDatFile = true;
                    if (datFile.DataTable != null)
                    {
                        DatTableView = datFile.DataTable.DefaultView;
                    }
                    string schemaStatus = datFile.HasSchema ? "with schema" : "no schema";
                    FileContent = $"DAT File: {datFile.RowCount} rows, {datFile.RowWidth} bytes per row ({schemaStatus})";
                    StatusMessage = $"Loaded {SelectedFile.Name} - {datFile.RowCount} rows ({schemaStatus})";
                });
                return;
            }

            // Check if it's an image file (PNG, JPG, BMP, GIF)
            if (detectedType == "png" || detectedType == "jpg" || detectedType == "bmp" || detectedType == "gif")
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        using (var ms = new MemoryStream(content))
                        {
                            bitmap.BeginInit();
                            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            bitmap.Freeze();
                        }
                        IsImageFile = true;
                        ImageSource = bitmap;
                        FileContent = $"Image: {bitmap.PixelWidth}x{bitmap.PixelHeight} pixels";
                        StatusMessage = $"Loaded {SelectedFile.Name} - {bitmap.PixelWidth}x{bitmap.PixelHeight}";
                    }
                    catch (Exception ex)
                    {
                        FileContent = $"Failed to load image: {ex.Message}";
                        StatusMessage = "Image load failed";
                    }
                });
                return;
            }

            // Check for DDS/TGA files - use Pfim to decode
            if (detectedType == "dds" || detectedType == "tga" || ext == ".dds" || ext == ".tga")
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(content);
                        using var image = Pfim.Pfimage.FromStream(ms);

                        // Convert to WPF BitmapSource
                        var format = GetPixelFormat(image.Format);
                        if (format == System.Windows.Media.PixelFormats.Default)
                        {
                            FileContent = $"Unsupported DDS format: {image.Format}";
                            StatusMessage = "DDS format not supported";
                            return;
                        }

                        var bitmap = System.Windows.Media.Imaging.BitmapSource.Create(
                            image.Width, image.Height,
                            96, 96,
                            format,
                            null,
                            image.Data,
                            image.Stride);

                        // Convert to BitmapImage for binding
                        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));

                        using var pngStream = new MemoryStream();
                        encoder.Save(pngStream);
                        pngStream.Position = 0;

                        var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                        bitmapImage.BeginInit();
                        bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmapImage.StreamSource = pngStream;
                        bitmapImage.EndInit();
                        bitmapImage.Freeze();

                        IsImageFile = true;
                        ImageSource = bitmapImage;
                        FileContent = $"DDS Texture: {image.Width}x{image.Height} ({image.Format})";
                        StatusMessage = $"Loaded {SelectedFile.Name} - {image.Width}x{image.Height}";
                    }
                    catch (Exception ex)
                    {
                        FileContent = $"Failed to load DDS: {ex.Message}\n\nUse Export to save and view with external tool.";
                        StatusMessage = "DDS load failed";
                    }
                });
                return;
            }

            string result;
            int maxSize = 100 * 1024; // 100KB limit for display

            // Only show as binary for known binary formats that we can't display specially
            // Everything else defaults to text view
            string[] binaryOnlyFormats = { "ogg", "wav", "mp3", "zip", "bank", "bk2", "fsb" };
            bool isBinary = binaryOnlyFormats.Contains(detectedType);

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
                EditableContent = result;
                StatusMessage = $"Loaded {SelectedFile.Name} ({content.Length / 1024}KB)";
            });
        });
    }

    [RelayCommand]
    private void StartEditing()
    {
        if (SelectedFile == null || IsDatFile) return;
        IsEditing = true;
        EditableContent = FileContent;
        StatusMessage = "Editing mode enabled. Make changes and click Save.";
    }

    [RelayCommand]
    private async Task SaveFileChanges()
    {
        if (SelectedFile == null || !IsEditing) return;

        try
        {
            byte[] newContent = Encoding.UTF8.GetBytes(EditableContent);

            if (newContent.Length != _currentFileContent?.Length)
            {
                MessageBox.Show(
                    $"File size changed from {_currentFileContent?.Length ?? 0} to {newContent.Length} bytes.\n" +
                    "GGPK files require same-size replacements. Changes not saved.",
                    "Size Mismatch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            _ggpkFile.WriteFileContent(SelectedFile, newContent);
            _currentFileContent = newContent;
            FileContent = EditableContent;
            IsEditing = false;
            StatusMessage = $"Saved changes to {SelectedFile.Name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void CancelEditing()
    {
        IsEditing = false;
        EditableContent = FileContent;
        StatusMessage = "Editing cancelled";
    }

    [RelayCommand]
    private async Task ExportToCsv()
    {
        if (_currentDatFile == null || !IsDatFile)
        {
            MessageBox.Show("Please select a DAT file first", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            FileName = Path.GetFileNameWithoutExtension(SelectedFile?.Name ?? "data") + ".csv",
            Title = "Export to CSV"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                string csv = _currentDatFile.ExportToCsv();
                await File.WriteAllTextAsync(dialog.FileName, csv, Encoding.UTF8);
                StatusMessage = $"Exported to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private static System.Windows.Media.PixelFormat GetPixelFormat(Pfim.ImageFormat format)
    {
        return format switch
        {
            Pfim.ImageFormat.Rgba32 => System.Windows.Media.PixelFormats.Bgra32,
            Pfim.ImageFormat.Rgb24 => System.Windows.Media.PixelFormats.Bgr24,
            Pfim.ImageFormat.Rgba16 => System.Windows.Media.PixelFormats.Bgr555,
            Pfim.ImageFormat.R5g5b5 => System.Windows.Media.PixelFormats.Bgr555,
            Pfim.ImageFormat.R5g6b5 => System.Windows.Media.PixelFormats.Bgr565,
            Pfim.ImageFormat.R5g5b5a1 => System.Windows.Media.PixelFormats.Bgr555,
            Pfim.ImageFormat.Rgb8 => System.Windows.Media.PixelFormats.Gray8,
            _ => System.Windows.Media.PixelFormats.Default
        };
    }

    private bool IsBinaryFile(byte[] content, string fileName)
    {
        // Check by extension first
        string ext = Path.GetExtension(fileName).ToLowerInvariant();

        // Known binary file extensions - check these FIRST
        string[] binaryExtensions = {
            ".dat", ".dat64", ".datl", ".datl64", // POE data
            ".dds", ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", // Images
            ".ogg", ".mp3", ".wav", ".bank", ".fsb", // Audio
            ".bin", ".bundle", ".bk2", ".usm", // Binary/video
            ".ttf", ".otf", ".woff", ".woff2", // Fonts
            ".zip", ".7z", ".rar", ".gz", // Archives
            ".dll", ".exe", ".so", // Executables
            ".psg", ".sm", ".ao", ".amd", ".epk", ".smd" // POE binary
        };

        if (binaryExtensions.Contains(ext))
            return true;

        // Known text file extensions (POE specific + common)
        string[] textExtensions = {
            // Shaders
            ".otc", ".hlsl", ".glsl", ".fx", ".shader", ".vert", ".frag", ".geom", ".comp",
            // Web/text
            ".txt", ".json", ".xml", ".html", ".htm", ".css", ".js", ".ts",
            // Code
            ".lua", ".py", ".cs", ".cpp", ".c", ".h", ".hpp", ".java", ".rb", ".php",
            // Config
            ".ini", ".cfg", ".config", ".yaml", ".yml", ".toml", ".properties",
            // Data text
            ".md", ".csv", ".tsv", ".log", ".sql",
            // POE specific text formats
            ".ot", ".filter", ".atlas", ".ais", ".aoc", ".arm", ".ast", ".at", ".bt",
            ".clt", ".dct", ".dgr", ".dlp", ".ecf", ".edp", ".env", ".et", ".ffx",
            ".fmt", ".gft", ".gt", ".idl", ".it", ".mat", ".mtd", ".mtp", ".mtx",
            ".pet", ".red", ".rs", ".rtx", ".tgr", ".tgt", ".tmd", ".trl", ".tsi",
            ".ui", ".act", ".ais", ".amd", ".ao", ".aoc", ".arm", ".ast", ".atlas",
            ".bank", ".bt", ".cht", ".clt", ".dct", ".dgr", ".dlp", ".ecf", ".edp",
            ".env", ".epk", ".et", ".ffx", ".fmt", ".gft", ".gt", ".idl", ".it",
            ".mat", ".mtp", ".mtx", ".ot", ".otc", ".pet", ".red", ".rs", ".rtx",
            ".sm", ".tgr", ".tgt", ".tmd", ".trl", ".tsi", ".ui"
        };

        if (textExtensions.Contains(ext))
            return false;

        // For unknown extensions, try to detect if it's valid text
        // Check first 4KB for content analysis
        int checkLength = Math.Min(content.Length, 4096);

        if (checkLength == 0)
            return false;

        // Count problematic bytes (null bytes and control characters except common ones)
        int problemBytes = 0;
        for (int i = 0; i < checkLength; i++)
        {
            byte b = content[i];
            // Null byte or non-printable control chars (except tab, newline, carriage return)
            if (b == 0 || (b < 32 && b != 9 && b != 10 && b != 13))
                problemBytes++;
        }

        // If more than 30% problematic bytes, it's likely binary
        // This is more lenient to handle various text encodings
        return problemBytes > checkLength * 0.3;
    }

    private string DetectFileType(byte[] content, string extension)
    {
        // Check magic bytes (file signatures) first for binary formats
        if (content.Length >= 8)
        {
            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47 &&
                content[4] == 0x0D && content[5] == 0x0A && content[6] == 0x1A && content[7] == 0x0A)
                return "png";

            // DDS: 44 44 53 20 (DDS )
            if (content[0] == 0x44 && content[1] == 0x44 && content[2] == 0x53 && content[3] == 0x20)
                return "dds";

            // OGG: 4F 67 67 53 (OggS)
            if (content[0] == 0x4F && content[1] == 0x67 && content[2] == 0x67 && content[3] == 0x53)
                return "ogg";
        }

        if (content.Length >= 4)
        {
            // RIFF (WAV, etc): 52 49 46 46
            if (content[0] == 0x52 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x46)
                return "wav";

            // GIF: 47 49 46 38 (GIF8)
            if (content[0] == 0x47 && content[1] == 0x49 && content[2] == 0x46 && content[3] == 0x38)
                return "gif";
        }

        if (content.Length >= 3)
        {
            // JPEG: FF D8 FF
            if (content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
                return "jpg";

            // ID3 (MP3): 49 44 33
            if (content[0] == 0x49 && content[1] == 0x44 && content[2] == 0x33)
                return "mp3";
        }

        if (content.Length >= 2)
        {
            // BMP: 42 4D (BM)
            if (content[0] == 0x42 && content[1] == 0x4D)
                return "bmp";

            // PK (ZIP): 50 4B
            if (content[0] == 0x50 && content[1] == 0x4B)
                return "zip";
        }

        // TGA doesn't have a reliable magic number, check extension
        if (extension == ".tga")
            return "tga";

        // DAT files
        if (extension == ".dat" || extension == ".dat64" || extension == ".datl" || extension == ".datl64")
            return "dat";

        // Known text file extensions (from VisualGGPK2) - trust the extension
        string[] unicodeTextExtensions = {
            ".act", ".ais", ".amd", ".ao", ".aoc", ".arm", ".ast", ".atlas", ".cht", ".clt",
            ".dct", ".dgr", ".dlp", ".ecf", ".edp", ".env", ".epk", ".et", ".ffx", ".gft",
            ".gt", ".idl", ".it", ".json", ".mat", ".mtd", ".mtp", ".ot", ".otc", ".pet",
            ".red", ".rs", ".sm", ".tgr", ".tgt", ".tmd", ".trl", ".tsi", ".txt", ".ui", ".xml"
        };

        string[] asciiTextExtensions = {
            ".csv", ".filter", ".fx", ".hlsl", ".mel", ".properties", ".slt"
        };

        // Additional common text extensions
        string[] commonTextExtensions = {
            ".html", ".htm", ".css", ".js", ".ts", ".lua", ".py", ".cs", ".cpp", ".c", ".h",
            ".hpp", ".java", ".rb", ".php", ".ini", ".cfg", ".config", ".yaml", ".yml",
            ".toml", ".md", ".log", ".sql", ".sh", ".bat", ".ps1", ".glsl", ".vert", ".frag"
        };

        if (unicodeTextExtensions.Contains(extension) ||
            asciiTextExtensions.Contains(extension) ||
            commonTextExtensions.Contains(extension))
            return "text";

        // For unknown extensions, check if content looks like text
        if (content.Length > 0 && !IsBinaryFile(content, "unknown" + extension))
            return "text";

        // Return extension-based type or unknown
        return extension.TrimStart('.').ToLowerInvariant();
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
