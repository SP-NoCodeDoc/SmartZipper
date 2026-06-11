using System;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ZipperApp.Services;

namespace ZipperApp.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ArchiverService _archiver = new();
    private CancellationTokenSource? _cts;

    // --- Input ---
    [ObservableProperty] private string _inputPath = "";
    [ObservableProperty] private string _filePattern = "*.pdf";
    [ObservableProperty] private string _outputDir = "";

    // --- Mode: 0 = Zip, 1 = Distribute ---
    [ObservableProperty] private int _outputModeIndex = 0;

    public bool IsZipMode => OutputModeIndex == 0;

    public bool ShowGroupedFields => UseGroupedMode && IsZipMode && IsSplitByCount;

    partial void OnOutputModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsZipMode));
        OnPropertyChanged(nameof(ShowGroupedFields));
    }

    // --- Splitting: 0 = ByCount, 1 = BySize ---
    [ObservableProperty] private int _splitModeIndex = 0;

    public bool IsSplitByCount => SplitModeIndex == 0;
    public bool IsSplitBySize => SplitModeIndex == 1;

    partial void OnSplitModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsSplitByCount));
        OnPropertyChanged(nameof(IsSplitBySize));
        OnPropertyChanged(nameof(ShowGroupedFields));
    }

    [ObservableProperty] private string _filesPerArchive = "1000";
    [ObservableProperty] private string _maxSizeMb = "100";
    [ObservableProperty] private string _filesPerFolder = "10000";
    [ObservableProperty] private string _foldersPerArchive = "10";
    [ObservableProperty] private string _folderNamePrefix = "";
    [ObservableProperty] private bool _useGroupedMode = false;

    partial void OnUseGroupedModeChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowGroupedFields));
    }

    // --- Options ---
    [ObservableProperty] private int _compressionLevelIndex = 1; // 0=Fastest, 1=Optimal, 2=SmallestSize
    [ObservableProperty] private string _prefix = "archive";
    [ObservableProperty] private bool _includeTimestamp = false;
    [ObservableProperty] private string _parallelism = Environment.ProcessorCount.ToString();
    [ObservableProperty] private int _oversizedHandlingIndex = 0; // 0=SeparateZip, 1=SeparateFolder

    // --- Progress ---
    [ObservableProperty] private double _progressPercent = 0;
    [ObservableProperty] private string _statusText = "Configure settings and click Start.";
    [ObservableProperty] private bool _isRunning = false;
    [ObservableProperty] private string _progressPercentText = "0.0%";
    [ObservableProperty] private string _filesProgressText = "0 / 0";
    [ObservableProperty] private string _archivesText = "0";

    // --- Log ---
    public ObservableCollection<string> LogMessages { get; } = new();

    // Browse commands — initialized as no-ops, replaced by the View once loaded
    [ObservableProperty] private IAsyncRelayCommand _browseInputCommand = new AsyncRelayCommand(() => Task.CompletedTask);
    [ObservableProperty] private IAsyncRelayCommand _browseOutputCommand = new AsyncRelayCommand(() => Task.CompletedTask);

    [RelayCommand]
    private async Task StartAsync()
    {
        // Validate
        if (string.IsNullOrWhiteSpace(InputPath))
        {
            StatusText = "Error: Please select an input folder.";
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputDir))
        {
            StatusText = "Error: Please select an output directory.";
            return;
        }

        if (!int.TryParse(FilesPerArchive, out var filesPerArch) || filesPerArch <= 0)
            filesPerArch = 1000;
        if (!int.TryParse(MaxSizeMb, out var maxSize) || maxSize <= 0)
            maxSize = 100;
        if (!int.TryParse(Parallelism, out var parallel) || parallel <= 0)
            parallel = Environment.ProcessorCount;
        if (!int.TryParse(FilesPerFolder, out var filesPerFolder) || filesPerFolder <= 0)
            filesPerFolder = 10000;
        if (!int.TryParse(FoldersPerArchive, out var foldersPerArch) || foldersPerArch <= 0)
            foldersPerArch = 10;

        var level = CompressionLevelIndex switch
        {
            0 => CompressionLevel.Fastest,
            2 => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };

        var options = new ArchiverOptions
        {
            InputPath = InputPath,
            OutputDir = OutputDir,
            FilePattern = string.IsNullOrWhiteSpace(FilePattern) ? "*" : FilePattern,
            Mode = OutputModeIndex == 0 ? OutputMode.Zip : OutputMode.DistributeToFolders,
            SplitBy = SplitModeIndex == 0 ? SplitMode.ByFileCount : SplitMode.BySizeMb,
            FilesPerArchive = filesPerArch,
            MaxSizeMb = maxSize,
            FilesPerFolder = UseGroupedMode ? filesPerFolder : 0,
            FoldersPerArchive = UseGroupedMode ? foldersPerArch : 0,
            FolderNamePrefix = FolderNamePrefix?.Trim() ?? "",
            CompressionLevel = level,
            Prefix = string.IsNullOrWhiteSpace(Prefix) ? "archive" : Prefix,
            IncludeTimestamp = IncludeTimestamp,
            MaxParallelism = parallel,
            OversizedHandling = OversizedHandlingIndex == 0
                ? OversizedFileHandling.SeparateZip
                : OversizedFileHandling.SeparateFolder
        };

        IsRunning = true;
        ProgressPercent = 0;
        LogMessages.Clear();
        StatusText = "Scanning files...";
        _cts = new CancellationTokenSource();

        ZipperApp.Services.AppLogger.Clear();
        ZipperApp.Services.AppLogger.Log($"Start clicked: Input={InputPath}, Output={OutputDir}");
        ZipperApp.Services.AppLogger.Log($"Mode={options.Mode}, SplitBy={options.SplitBy}, MaxSizeMb={options.MaxSizeMb}, FilesPerArchive={options.FilesPerArchive}");

        var progress = new Progress<ProgressInfo>(p =>
        {
            ZipperApp.Services.AppLogger.Log($"Progress: {p.FilesProcessed}/{p.TotalFiles} files, {p.CompletedArchives} archives — {p.CurrentOperation}");

            ProgressPercent = p.TotalFiles > 0
                ? (double)p.FilesProcessed / p.TotalFiles * 100
                : 0;
            ProgressPercentText = $"{ProgressPercent:F1}%";
            FilesProgressText = $"{p.FilesProcessed:N0} / {p.TotalFiles:N0}";
            ArchivesText = $"{p.CompletedArchives}";
            StatusText = p.CurrentOperation;

            if (LogMessages.Count == 0 || LogMessages[^1] != p.CurrentOperation)
                LogMessages.Add(p.CurrentOperation);
        });

        try
        {
            ZipperApp.Services.AppLogger.Log("Calling RunAsync...");
            var results = await Task.Run(() => _archiver.RunAsync(options, progress, _cts.Token));
            ZipperApp.Services.AppLogger.Log($"RunAsync completed: {results.Count} results");
            var outputType = OutputModeIndex == 0 ? "archive(s)" : "folder(s)";
            StatusText = $"Done! Created {results.Count} {outputType}.";
            ProgressPercent = 100;
            LogMessages.Add($"✓ Completed: {results.Count} outputs in {OutputDir}");
        }
        catch (OperationCanceledException)
        {
            ZipperApp.Services.AppLogger.Log("Operation cancelled");
            StatusText = "Cancelled by user.";
            LogMessages.Add("Operation cancelled.");
        }
        catch (Exception ex)
        {
            ZipperApp.Services.AppLogger.Error("RunAsync failed", ex);
            StatusText = $"Error: {ex.Message}";
            LogMessages.Add($"✗ {ex.GetType().Name}: {ex.Message}");
            if (ex.InnerException != null)
                LogMessages.Add($"  Inner: {ex.InnerException.Message}");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "Cancelling...";
    }
}
