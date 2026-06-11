using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZipperApp.Services;

public static class AppLogger
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "zipperapp.log");
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        lock (Lock) { File.AppendAllText(LogPath, line + Environment.NewLine); }
    }

    public static void Error(string message, Exception? ex = null)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR: {message}";
        if (ex != null)
            line += $"\n  {ex.GetType().Name}: {ex.Message}\n  {ex.StackTrace}";
        lock (Lock) { File.AppendAllText(LogPath, line + Environment.NewLine); }
    }

    public static void Clear()
    {
        lock (Lock) { File.WriteAllText(LogPath, $"=== ZipperApp {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}"); }
    }
}

public enum OutputMode { Zip, DistributeToFolders }
public enum SplitMode { ByFileCount, BySizeMb }
public enum OversizedFileHandling { SeparateZip, SeparateFolder }

public record ArchiverOptions
{
    public required string InputPath { get; init; }
    public required string OutputDir { get; init; }
    public string FilePattern { get; init; } = "*";
    public OutputMode Mode { get; init; } = OutputMode.Zip;
    public SplitMode SplitBy { get; init; } = SplitMode.ByFileCount;
    public int FilesPerArchive { get; init; } = 100;
    public int MaxSizeMb { get; init; } = 100;
    public int FilesPerFolder { get; init; } = 0;       // 0 = no sub-folders
    public int FoldersPerArchive { get; init; } = 0;    // 0 = use FilesPerArchive directly
    public CompressionLevel CompressionLevel { get; init; } = CompressionLevel.Optimal;
    public string Prefix { get; init; } = "archive";
    public bool IncludeTimestamp { get; init; } = false;
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;
    public OversizedFileHandling OversizedHandling { get; init; } = OversizedFileHandling.SeparateZip;
}

public record ProgressInfo
{
    public int CompletedArchives { get; init; }
    public int TotalFiles { get; init; }
    public int FilesProcessed { get; init; }
    public long BytesProcessed { get; init; }
    public long TotalBytes { get; init; }
    public string CurrentOperation { get; init; } = "";
}

public class ArchiverService
{
    private record struct ChunkRange(int StartIndex, int Count);

    public async Task<List<string>> RunAsync(
        ArchiverOptions options, IProgress<ProgressInfo>? progress = null, CancellationToken ct = default)
    {
        AppLogger.Log($"RunAsync: Input={options.InputPath}, Output={options.OutputDir}, Mode={options.Mode}, Split={options.SplitBy}");

        var allFiles = GatherFiles(options.InputPath, options.FilePattern);
        if (allFiles.Count == 0)
            throw new InvalidOperationException("No files found matching the pattern.");

        AppLogger.Log($"Found {allFiles.Count} files, {allFiles.Sum(f => f.Length) / 1024 / 1024} MB total");
        Directory.CreateDirectory(options.OutputDir);

        // Separate oversized files
        List<FileInfo> files, oversizedFiles;
        if (options.SplitBy == SplitMode.BySizeMb)
        {
            long maxBytes = (long)options.MaxSizeMb * 1024 * 1024;
            oversizedFiles = allFiles.Where(f => f.Length > maxBytes).ToList();
            files = allFiles.Where(f => f.Length <= maxBytes).ToList();
            if (oversizedFiles.Count > 0)
                AppLogger.Log($"Separated {oversizedFiles.Count} oversized files");
        }
        else { oversizedFiles = []; files = allFiles; }

        var totalBytes = allFiles.Sum(f => f.Length);
        var results = new List<string>();

        // Handle oversized
        if (oversizedFiles.Count > 0)
        {
            results.AddRange(await HandleOversizedAsync(oversizedFiles, options, ct));
            progress?.Report(new ProgressInfo
            {
                CompletedArchives = results.Count, TotalFiles = allFiles.Count,
                FilesProcessed = oversizedFiles.Count, BytesProcessed = oversizedFiles.Sum(f => f.Length),
                TotalBytes = totalBytes, CurrentOperation = $"Handled {oversizedFiles.Count} oversized file(s)"
            });
        }

        if (files.Count == 0) return results;

        // Main processing
        if (options.Mode == OutputMode.Zip && options.SplitBy == SplitMode.BySizeMb)
        {
            results.AddRange(await ZipBySizeAsync(files, options, totalBytes, progress, ct));
        }
        else if (options.Mode == OutputMode.Zip && options.SplitBy == SplitMode.ByFileCount
                 && options.FilesPerFolder > 0 && options.FoldersPerArchive > 0)
        {
            // Grouped mode: files → folders → zips
            results.AddRange(await ZipGroupedAsync(files, options, totalBytes, progress, ct));
        }
        else
        {
            var chunks = options.SplitBy == SplitMode.ByFileCount
                ? SplitByCount(files, options.FilesPerArchive)
                : SplitBySizeRaw(files, options.MaxSizeMb);

            results.AddRange(await ProcessChunksAsync(chunks, options, totalBytes, progress, ct,
                isZip: options.Mode == OutputMode.Zip));
        }

        AppLogger.Log($"Done: {results.Count} outputs created");
        return results;
    }

    // ─── Zip Grouped (Files → Folders → Zips) ─────────────────────────────

    private async Task<List<string>> ZipGroupedAsync(
        List<FileInfo> files, ArchiverOptions options, long totalBytes,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        int filesPerFolder = options.FilesPerFolder;
        int foldersPerArchive = options.FoldersPerArchive;
        int filesPerArchive = filesPerFolder * foldersPerArchive;

        AppLogger.Log($"Grouped mode: {filesPerFolder} files/folder, {foldersPerArchive} folders/zip = {filesPerArchive} files/zip");

        // Split files into archive-sized chunks
        var archiveChunks = SplitByCount(files, filesPerArchive);
        int totalArchives = archiveChunks.Count;
        int completedArchives = 0, filesProcessed = 0;
        long bytesProcessed = 0;
        var results = new string?[totalArchives];

        AppLogger.Log($"Will create {totalArchives} archives");

        progress?.Report(new ProgressInfo
        {
            CompletedArchives = 0, TotalFiles = files.Count, FilesProcessed = 0,
            BytesProcessed = 0, TotalBytes = totalBytes,
            CurrentOperation = $"Planned {totalArchives} archives ({foldersPerArchive} folders each)"
        });

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalArchives),
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = ct },
            (index, token) =>
            {
                token.ThrowIfCancellationRequested();
                var chunk = archiveChunks[index];
                var name = MakeName(options.Prefix, index + 1, totalArchives, options.IncludeTimestamp, ".zip");
                var outPath = Path.Combine(options.OutputDir, name);

                WriteZipGrouped(outPath, chunk, filesPerFolder, options.CompressionLevel, token);
                results[index] = outPath;

                var size = new FileInfo(outPath).Length;
                int done = Interlocked.Increment(ref completedArchives);
                Interlocked.Add(ref filesProcessed, chunk.Count);
                Interlocked.Add(ref bytesProcessed, chunk.Sum(f => f.Length));

                AppLogger.Log($"Archive {done}/{totalArchives}: {name} ({chunk.Count} files, {size / 1024 / 1024} MB)");
                progress?.Report(new ProgressInfo
                {
                    CompletedArchives = done, TotalFiles = files.Count,
                    FilesProcessed = Volatile.Read(ref filesProcessed),
                    BytesProcessed = Interlocked.Read(ref bytesProcessed),
                    TotalBytes = totalBytes,
                    CurrentOperation = $"Created {name} ({chunk.Count} files, {size / 1024 / 1024} MB)"
                });
                return ValueTask.CompletedTask;
            });

        return results.Where(r => r != null).Cast<string>().ToList();
    }

    /// <summary>
    /// Writes a zip with files organized into sub-folders.
    /// E.g., Folder_01/file1.pdf, Folder_01/file2.pdf, ..., Folder_02/file1001.pdf, ...
    /// </summary>
    private static void WriteZipGrouped(string path, List<FileInfo> files,
                                         int filesPerFolder, CompressionLevel level, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        int folderIndex = 1;
        int fileInFolder = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            if (fileInFolder >= filesPerFolder)
            {
                folderIndex++;
                fileInFolder = 0;
                usedNames.Clear();
            }

            var folderName = $"Folder_{folderIndex:D3}";
            var fileName = UniqueName(file.Name, usedNames);
            usedNames.Add(fileName);
            var entryPath = $"{folderName}/{fileName}";

            var entry = archive.CreateEntry(entryPath, level);
            using var es = entry.Open();
            using var fs = file.OpenRead();
            fs.CopyTo(es);

            fileInFolder++;
        }
    }

    // ─── Zip By Size (Fast Estimate + Parallel Write) ───────────────────────

    private async Task<List<string>> ZipBySizeAsync(
        List<FileInfo> files, ArchiverOptions options, long totalBytes,
        IProgress<ProgressInfo>? progress, CancellationToken ct)
    {
        long maxBytes = (long)options.MaxSizeMb * 1024 * 1024;

        // Instant chunk planning — no I/O, just arithmetic on file sizes
        var chunks = EstimateChunks(files, maxBytes);
        AppLogger.Log($"Planned {chunks.Count} chunks (instant, no compression)");

        progress?.Report(new ProgressInfo
        {
            CompletedArchives = 0, TotalFiles = files.Count, FilesProcessed = 0,
            BytesProcessed = 0, TotalBytes = totalBytes,
            CurrentOperation = $"Planned {chunks.Count} archives — compressing..."
        });

        // Write all archives in parallel
        int totalChunks = chunks.Count;
        int completedArchives = 0, filesProcessed = 0;
        long bytesProcessed = 0;
        var results = new string[totalChunks];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalChunks),
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = ct },
            (index, token) =>
            {
                token.ThrowIfCancellationRequested();
                var chunk = chunks[index];
                var name = MakeName(options.Prefix, index + 1, totalChunks, options.IncludeTimestamp, ".zip");
                var outPath = Path.Combine(options.OutputDir, name);

                WriteZip(outPath, files, chunk.StartIndex, chunk.Count, options.CompressionLevel, token);
                results[index] = outPath;

                var size = new FileInfo(outPath).Length;
                int done = Interlocked.Increment(ref completedArchives);
                Interlocked.Add(ref filesProcessed, chunk.Count);
                Interlocked.Add(ref bytesProcessed, SumBytes(files, chunk.StartIndex, chunk.Count));

                AppLogger.Log($"Archive {done}/{totalChunks}: {name} ({chunk.Count} files, {size / 1024 / 1024} MB)");
                progress?.Report(new ProgressInfo
                {
                    CompletedArchives = done, TotalFiles = files.Count,
                    FilesProcessed = Volatile.Read(ref filesProcessed),
                    BytesProcessed = Interlocked.Read(ref bytesProcessed),
                    TotalBytes = totalBytes,
                    CurrentOperation = $"Created {name} ({size / 1024 / 1024} MB)"
                });
                return ValueTask.CompletedTask;
            });

        return results.ToList();
    }

    private static List<ChunkRange> EstimateChunks(List<FileInfo> files, long maxBytes)
    {
        var chunks = new List<ChunkRange>();
        int start = 0;
        while (start < files.Count)
        {
            long acc = 0;
            int count = 0;
            for (int i = start; i < files.Count; i++)
            {
                int nameLen = System.Text.Encoding.UTF8.GetByteCount(files[i].Name);
                long entry = files[i].Length + 76 + nameLen * 2; // local + central + name×2
                if (count > 0 && acc + entry > maxBytes) break;
                acc += entry;
                count++;
            }
            chunks.Add(new ChunkRange(start, Math.Max(1, count)));
            start += Math.Max(1, count);
        }
        return chunks;
    }

    // ─── Process Pre-Split Chunks (Count or Distribute) ─────────────────────

    private async Task<List<string>> ProcessChunksAsync(
        List<List<FileInfo>> chunks, ArchiverOptions options, long totalBytes,
        IProgress<ProgressInfo>? progress, CancellationToken ct, bool isZip)
    {
        int totalFiles = chunks.Sum(c => c.Count);
        var results = new string?[chunks.Count];
        int completedArchives = 0, filesProcessed = 0;
        long bytesProcessed = 0;

        await Parallel.ForEachAsync(
            Enumerable.Range(0, chunks.Count),
            new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = ct },
            async (index, token) =>
            {
                var chunk = chunks[index];
                var ext = isZip ? ".zip" : "";
                var name = MakeName(options.Prefix, index + 1, chunks.Count,
                    options.IncludeTimestamp, ext);
                var outPath = Path.Combine(options.OutputDir, name);

                if (isZip)
                    await WriteZipAsync(chunk, outPath, options.CompressionLevel, token);
                else
                    await CopyToFolderAsync(chunk, outPath, token);

                results[index] = outPath;
                int done = Interlocked.Increment(ref completedArchives);
                Interlocked.Add(ref filesProcessed, chunk.Count);
                Interlocked.Add(ref bytesProcessed, chunk.Sum(f => f.Length));

                progress?.Report(new ProgressInfo
                {
                    CompletedArchives = done, TotalFiles = totalFiles,
                    FilesProcessed = Volatile.Read(ref filesProcessed),
                    BytesProcessed = Interlocked.Read(ref bytesProcessed),
                    TotalBytes = totalBytes,
                    CurrentOperation = $"Completed {name} ({chunk.Count} files)"
                });
            });

        return results.Where(r => r != null).Cast<string>().ToList();
    }

    // ─── Core I/O ───────────────────────────────────────────────────────────

    private static void WriteZip(string path, List<FileInfo> files, int start, int count,
                                  CompressionLevel level, CancellationToken ct)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = start; i < start + count && i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = files[i];
            var entryName = UniqueName(file.Name, names);
            names.Add(entryName);
            var entry = archive.CreateEntry(entryName, level);
            using var es = entry.Open();
            using var fs = file.OpenRead();
            fs.CopyTo(es);
        }
    }

    private static async Task WriteZipAsync(List<FileInfo> files, string path,
                                             CompressionLevel level, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var entryName = UniqueName(file.Name, names);
            names.Add(entryName);
            var entry = archive.CreateEntry(entryName, level);
            await using var es = entry.Open();
            await using var fs = file.OpenRead();
            await fs.CopyToAsync(es, ct);
        }
    }

    private static async Task CopyToFolderAsync(List<FileInfo> files, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(folder, file.Name);
            if (File.Exists(dest))
            {
                var n = Path.GetFileNameWithoutExtension(file.Name);
                var e = Path.GetExtension(file.Name);
                int c = 1;
                do { dest = Path.Combine(folder, $"{n}_{c++}{e}"); } while (File.Exists(dest));
            }
            await using var src = file.OpenRead();
            await using var dst = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await src.CopyToAsync(dst, ct);
        }
    }

    private static async Task<List<string>> HandleOversizedAsync(
        List<FileInfo> files, ArchiverOptions options, CancellationToken ct)
    {
        var results = new List<string>();
        if (options.OversizedHandling == OversizedFileHandling.SeparateFolder)
        {
            var folder = Path.Combine(options.OutputDir, $"{options.Prefix}_oversized");
            await CopyToFolderAsync(files, folder, ct);
            results.Add(folder);
        }
        else
        {
            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var zipPath = Path.Combine(options.OutputDir,
                    $"{options.Prefix}_oversized_{Path.GetFileNameWithoutExtension(file.Name)}.zip");
                await using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
                var entry = archive.CreateEntry(file.Name, options.CompressionLevel);
                await using var es = entry.Open();
                await using var fs = file.OpenRead();
                await fs.CopyToAsync(es, ct);
                results.Add(zipPath);
            }
        }
        return results;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static List<FileInfo> GatherFiles(string inputPath, string pattern)
    {
        AppLogger.Log($"GatherFiles: {inputPath}, pattern={pattern}");
        if (File.Exists(inputPath)) return [new FileInfo(inputPath)];
        if (!Directory.Exists(inputPath))
            throw new DirectoryNotFoundException($"Not found: {inputPath}");
        return Directory.EnumerateFiles(inputPath, pattern, SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).OrderBy(f => f.FullName).ToList();
    }

    private static List<List<FileInfo>> SplitByCount(List<FileInfo> files, int n)
    {
        var chunks = new List<List<FileInfo>>();
        for (int i = 0; i < files.Count; i += n)
            chunks.Add(files.GetRange(i, Math.Min(n, files.Count - i)));
        return chunks;
    }

    private static List<List<FileInfo>> SplitBySizeRaw(List<FileInfo> files, int maxMb)
    {
        long max = (long)maxMb * 1024 * 1024;
        var chunks = new List<List<FileInfo>>();
        var cur = new List<FileInfo>();
        long size = 0;
        foreach (var f in files)
        {
            if (cur.Count > 0 && size + f.Length > max)
            { chunks.Add(cur); cur = [f]; size = f.Length; }
            else { cur.Add(f); size += f.Length; }
        }
        if (cur.Count > 0) chunks.Add(cur);
        return chunks;
    }

    private static long SumBytes(List<FileInfo> files, int start, int count)
    {
        long s = 0;
        for (int i = start; i < start + count && i < files.Count; i++) s += files[i].Length;
        return s;
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        if (!used.Contains(name)) return name;
        var n = Path.GetFileNameWithoutExtension(name);
        var e = Path.GetExtension(name);
        int c = 1;
        string candidate;
        do { candidate = $"{n}_{c++}{e}"; } while (used.Contains(candidate));
        return candidate;
    }

    private static string MakeName(string prefix, int index, int total, bool timestamp, string ext)
    {
        var parts = new List<string> { prefix };
        if (total > 1) parts.Add($"part_{index:D4}_of_{total:D4}");
        if (timestamp) parts.Add(DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        return string.Join("_", parts) + ext;
    }
}
