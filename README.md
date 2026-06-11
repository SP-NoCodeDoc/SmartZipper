# Advanced 7zip Archiver

A comprehensive file archiving solution using `py7zr` with advanced features including compression level selection, multiple compression types, file-based splitting, custom naming with sequences, and parallel processing.

## 🚀 Features

- ✅ **Multiple Compression Types**: LZMA2, LZMA, Deflate, BZip2, Copy (no compression)
- ✅ **Compression Level Control**: 0-9 levels for fine-tuning
- ✅ **File-based Splitting**: Split by file count or archive size
- ✅ **Custom Naming**: Flexible naming with sequence numbers and timestamps
- ✅ **Parallel Processing**: Multi-core support for faster archiving
- ✅ **Progress Tracking**: Real-time progress and performance metrics
- ✅ **Recursive Search**: Option to search subdirectories
- ✅ **File Pattern Matching**: Support for glob patterns
- ✅ **Comprehensive Logging**: Detailed logs for troubleshooting

## 📦 Installation

```bash
# Install dependencies
pip install -r requirements.txt

# Or install directly
pip install py7zr
```

## 🎯 Quick Start

### Basic Usage
```bash
# Archive all files in a directory
python advanced_7zip_archiver.py /path/to/files

# Archive specific file types
python advanced_7zip_archiver.py /path/to/files --file-pattern "*.pdf"

# Archive in chunks of 1000 files
python advanced_7zip_archiver.py /path/to/files --files-per-archive 1000
```

### Advanced Usage
```bash
# Archive by size (100MB per archive) with fast compression
python advanced_7zip_archiver.py /path/to/files --max-size-mb 100 --compression-type deflate

# Custom naming with alphabetic sequences and timestamp
python advanced_7zip_archiver.py /path/to/files --prefix my_archive --sequence-format alpha --include-timestamp

# Parallel processing for maximum speed
python advanced_7zip_archiver.py /path/to/files --parallel --max-workers 8
```

## 📋 Command Line Options

### Input Options
- `input_path`: Path to files or directory to archive
- `--file-pattern, -p`: File pattern to match (default: `*`)
- `--recursive, -r`: Search recursively in subdirectories (default: True)
- `--no-recursive`: Disable recursive search

### Splitting Options (choose one)
- `--files-per-archive, -f`: Number of files per archive
- `--max-size-mb, -s`: Maximum size of each archive in MB

### Naming Options
- `--prefix`: Prefix for archive file names (default: `archive`)
- `--sequence-format`: Format for sequence numbers:
  - `numeric`: 001, 002, 003... (default)
  - `alpha`: A, B, C, AA, AB...
  - `hex`: 01, 02, 03, 0a, 0b...
  - `binary`: 00000001, 00000010...
- `--include-timestamp, -t`: Include timestamp in archive names

### Compression Options
- `--compression-type, -c`: Type of compression:
  - `lzma2`: Best compression, slower (default)
  - `lzma`: Good compression, balanced
  - `deflate`: Fast, moderate compression
  - `bzip2`: Good compression, slower
  - `copy`: No compression, fastest
- `--compression-level, -l`: Compression level 0-9 (default: 7)

### Performance Options
- `--parallel`: Enable parallel processing
- `--max-workers`: Number of worker processes

### Output Options
- `--output-dir, -o`: Output directory for archives (default: `archives`)
- `--verbose, -v`: Enable verbose logging
- `--list-compression-types`: List available compression types and exit

## 🔧 Compression Types

| Type | Description | Speed | Compression | Best For |
|------|-------------|-------|-------------|----------|
| **lzma2** | LZMA2 - Best compression, slower | Slow | Excellent | Long-term storage |
| **lzma** | LZMA - Good compression, balanced | Medium | Very Good | General use |
| **deflate** | Deflate - Fast, moderate compression | Fast | Good | Quick archiving |
| **bzip2** | BZip2 - Good compression, slower | Slow | Very Good | Text files |
| **copy** | No compression - Fastest | Very Fast | None | Already compressed files |

## 📝 Examples

### Example 1: Archive PDFs in 10K Lots
```bash
python advanced_7zip_archiver.py /path/to/pdfs \
    --files-per-archive 10000 \
    --prefix pdf_batch \
    --compression-type lzma2 \
    --compression-level 7 \
    --parallel
```
**Result**: `pdf_batch_part_001_of_080.7z`, `pdf_batch_part_002_of_080.7z`, etc.

### Example 2: Archive by Size with Custom Naming
```bash
python advanced_7zip_archiver.py /path/to/files \
    --max-size-mb 500 \
    --prefix my_files \
    --sequence-format alpha \
    --include-timestamp \
    --compression-type deflate \
    --compression-level 5
```
**Result**: `my_files_part_A_of_005_20241215_143022.7z`

### Example 3: Fast Archiving for Already Compressed Files
```bash
python advanced_7zip_archiver.py /path/to/images \
    --file-pattern "*.jpg" \
    --files-per-archive 5000 \
    --compression-type copy \
    --prefix images \
    --parallel
```
**Result**: Fast archiving with no compression overhead

### Example 4: Maximum Compression for Text Files
```bash
python advanced_7zip_archiver.py /path/to/documents \
    --file-pattern "*.txt" \
    --max-size-mb 100 \
    --compression-type bzip2 \
    --compression-level 9 \
    --prefix docs
```
**Result**: Maximum compression for text files

## 🎛️ Sequence Formats

### Numeric (default)
```
archive_part_001_of_010.7z
archive_part_002_of_010.7z
...
archive_part_010_of_010.7z
```

### Alphabetic
```
archive_part_A_of_J.7z
archive_part_B_of_J.7z
...
archive_part_J_of_J.7z
```

### Hexadecimal
```
archive_part_01_of_0a.7z
archive_part_02_of_0a.7z
...
archive_part_0a_of_0a.7z
```

### Binary
```
archive_part_00000001_of_00001010.7z
archive_part_00000010_of_00001010.7z
...
archive_part_00001010_of_00001010.7z
```

### With Timestamp
```
archive_part_001_of_010_20241215_143022.7z
archive_part_002_of_010_20241215_143022.7z
```

## 📊 Performance

### Expected Performance for Different File Types

| File Type | Files/Second | MB/Second | Recommended Settings |
|-----------|-------------|-----------|---------------------|
| Small files (< 1KB) | 1,000-5,000 | 1-5 MB/s | `--compression-type deflate` |
| Medium files (1KB-1MB) | 100-500 | 10-50 MB/s | `--compression-type lzma` |
| Large files (> 1MB) | 10-50 | 20-100 MB/s | `--compression-type lzma2` |
| Already compressed | 50-200 | 50-200 MB/s | `--compression-type copy` |

### Performance Tips

1. **For maximum speed**: Use `--compression-type copy` or `deflate`
2. **For maximum compression**: Use `--compression-type lzma2` with level 9
3. **For large collections**: Use `--parallel` with multiple workers
4. **For already compressed files**: Use `--compression-type copy`

## 🔍 Troubleshooting

### Common Issues

1. **"py7zr not installed"**
   ```bash
   pip install py7zr
   ```

2. **"No files found to archive"**
   - Check the input path exists
   - Verify file pattern matches your files
   - Use `--verbose` for debugging

3. **Slow performance**
   - Use `--parallel` for multi-core processing
   - Try `--compression-type copy` for speed
   - Increase `--files-per-archive` for fewer archives

4. **Memory issues**
   - Decrease `--files-per-archive`
   - Use `--max-size-mb` instead
   - Reduce `--max-workers`

### Logging

The script creates detailed logs in `archiver.log`:
```
2024-12-15 14:30:22 - INFO - Found 800000 files (140.2 MB total)
2024-12-15 14:30:22 - INFO - Will create 80 archive(s)
2024-12-15 14:30:22 - INFO - Compression: lzma2 (level 7) - LZMA2 - Best compression, slower
2024-12-15 14:30:45 - INFO - Created archive_part_001_of_080.7z (1.75 MB) in 23.45s
```

## 📈 Use Cases

### Large File Collections (800K+ files)
```bash
python advanced_7zip_archiver.py /path/to/large_collection \
    --files-per-archive 10000 \
    --parallel \
    --max-workers 8 \
    --compression-type lzma2
```

### Backup Systems
```bash
python advanced_7zip_archiver.py /path/to/backup \
    --max-size-mb 1000 \
    --include-timestamp \
    --compression-type lzma2 \
    --compression-level 9
```

### Quick Archiving
```bash
python advanced_7zip_archiver.py /path/to/files \
    --compression-type copy \
    --parallel \
    --prefix quick_archive
```

## 🤝 Contributing

Improvements and bug reports are welcome! Consider:
- Performance optimizations
- Additional compression algorithms
- Better error handling
- More file format support
- GUI interface

## 📄 License

This script is provided as-is for educational and practical use. Feel free to modify and distribute. 