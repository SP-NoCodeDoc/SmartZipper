# SmartZipper — Roadmap

## v1.1.2 (Current Release)
- [x] Zip by file count (flat)
- [x] Zip by size (MB) with fast estimation
- [x] Grouped mode (files → sub-folders → zips)
- [x] Custom folder name prefix (or auto from source folder)
- [x] Distribute to folders (no compression)
- [x] Parallel compression (all CPU cores)
- [x] Oversized file handling (separate zip or folder)
- [x] Light theme UI (SmartCopy-inspired)
- [x] Standalone executables (Windows + macOS)
- [x] Cancel support
- [x] Progress reporting (files, archives, percentage)
- [x] Duplicate filename handling
- [x] Log file for troubleshooting
- [x] Commercial license link in app
- [x] GitHub Actions automated builds
- [x] Tooltips for key settings

## v1.2.0 — Network Share Optimizations
- [ ] Auto-detect UNC/network paths
- [ ] Adaptive parallelism (reduce concurrent reads for network, keep full parallelism for compression)
- [ ] Larger I/O buffers for SMB (256KB–1MB)
- [ ] Retry logic with exponential backoff for transient network failures
- [ ] "Copy locally first" option for maximum speed on slow networks
- [ ] Network throughput display (MB/s read vs write)

## v1.3.0 — Accuracy & Resilience
- [ ] Post-compression size verification (warn if any archive exceeds limit)
- [ ] Resume capability (skip already-created archives on re-run)
- [ ] Checksum verification (optional SHA-256 per archive)
- [ ] Error report file (list of files that failed to compress)

## v1.4.0 — UX Polish
- [ ] Drag-and-drop input folder
- [ ] Recent paths history
- [ ] Estimated time remaining
- [ ] Dark/light theme toggle
- [ ] Archive preview (list contents without extracting)
- [ ] Sound/notification on completion

## Future Ideas
- [ ] Password-protected zip archives
- [ ] Exclude patterns (e.g., skip *.tmp, Thumbs.db)
- [ ] Date-based or alphabetical folder naming options
- [ ] CLI mode (run from command line without GUI)
- [ ] Multi-format support (7z, tar.gz)
- [ ] Windows code signing
- [ ] macOS notarization
