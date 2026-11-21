# Pomegranate — Spec

Status: agreed in the design interview on 2026-11-18. v2 details agreed on 2026-11-19 (§7.2), v3 on 2026-11-20 (§7.3).

## 1. What it is

Pomegranate is an offline file converter for Windows 11. You start dragging a file, hold a modifier key, and a wheel of options opens at the cursor. Drop the file on a slice and Pomegranate converts it, runs a tool on it, or performs an action. The result is saved next to the original. Nothing ever leaves the PC.

It borrows its interaction model from Tangerine for Mac (Leap Studio). It is a from-scratch Windows app. It does **not** use Tangerine's name, icon, artwork, wheel design, marketing text or code, and the Store listing never mentions Tangerine.

## 2. Platform and distribution

| | |
|---|---|
| OS | Windows 11 only |
| CPU | x64 and ARM64 (separate builds) |
| Language / UI | C# on .NET 10, WPF with its Windows 11 (Fluent) theme; Win32 interop through hand-written `LibraryImport` declarations |
| Package | MSIX, self-contained, built by `tools/package-msix.ps1` (dotnet publish + makepri + makeappx from the Windows SDK) |
| Channels | Microsoft Store (free; the Store signs the package), GitHub Releases, winget |
| License | MIT for Pomegranate's code; bundled engines keep their own licenses (see §9) |
| Price | Free. No trial, no paywall, no conversion limit |
| Network | None. No telemetry, crash uploads or update checks; the Store and winget handle updates |
| Install size | Measured on x64 with v3: **225 MB download, 686 MB installed**: engines 426 MB (FFmpeg 182, Pandoc 230, dnglab 13) and the app with its own .NET runtime ~260 MB. v2 was 222.5 / 681 MB (Store MSIX packages can't use a shared .NET install). v1 alone was 175 / 442 MB. Every bundled engine ships in the package; nothing is downloaded at runtime. Possible later cut: replace the Windows Forms tray icon with direct Win32 calls (~20 MB) |
| Language | English in v1. All strings live in `.resx` files so the app can be translated later |

## 3. Phases

| Phase | Scope |
|---|---|
| **v1** | App shell (tray, settings, first run), drag detection and the wheel, right-click menu, hotkey, keyboard control, conversions for **images, audio, video, archives, subtitles**, all **6 actions** |
| **v2** | Conversions for **PDF, documents, slides, spreadsheets, EPUB** (§7.2). Built on branch `v2/documents` |
| **v3** | The **file tools** (§7.3): 20 tools, 6 of them with an editor window. Built on branch `v3/tools` |
| Later | Touch and pen activation, translations, remove background (Windows only has an offline engine for it on Copilot+ PCs), PDF passwords, watermarks |

## 4. How you use it

### 4.1 Drag and hold

| Hold while dragging | Wheel |
|---|---|
| **Shift** | Convert |
| **Ctrl** | Tools (§7.3) |
| **Alt** | Actions |

- The wheel opens only after the key has been held for **250 ms** during a drag. A quick Shift or Ctrl press still does Explorer's normal move or copy.
- Once open, the wheel stays open while you keep dragging, even if you let go of the key. Pressing a different modifier switches wheels.
- Drop on a slice to run it. Drop in the center or press **Esc** to cancel.
- Keys and hold delay can be changed in Settings.

### 4.2 Without dragging

- **Right-click menu**: "Open in Pomegranate" in the Windows 11 menu opens the Convert wheel at the cursor for the selected files.
- **Global hotkey** (default **Win+Alt+C**): opens the Convert wheel at the cursor for the files selected in the active Explorer window or on the desktop.
- **Keyboard in the wheel**: arrow keys move, Enter runs the slice, Esc cancels, Tab / Shift+Tab switch wheels.

### 4.3 What the wheels offer

- **Several files**: Convert shows only formats that work for every file. If there are none, the wheel says "No shared formats" and Actions still work.
- **Folders, or a mix of files and folders**: Actions only.
- A format the file is already in is never offered.

## 5. The wheel

- An original Windows 11 design: a ring centered on the cursor that follows light/dark mode and the system accent color.
- At most **8 slices**. Extra options sit in an outer ring that opens when you hover the "More" slice.
- Each slice shows a small format icon and a label (e.g. "PNG").
- A title in the center names the open wheel (Convert, Tools, Actions).
- A short open animation, skipped when Windows' "Animation effects" setting is off.
- Works in high-contrast mode. Narrator reads out each slice through UI Automation.

## 6. Actions (v1, all 6)

| Action | On Windows |
|---|---|
| **Share** | Opens the Windows share panel (Nearby Share, Phone Link, Mail…) for the file(s) |
| **Float** | Puts the file(s) in a small always-on-top tray. You can drag them out of it into any app later. The tray holds references, not copies |
| **Copy to…** | Shows a list of pinned and recent folders plus "Browse…", then copies the file(s) there |
| **Move to…** | Same list, but moves the file(s) |
| **Copy Path** | Puts the full path(s) on the clipboard, one per line |
| **Terminal** | Opens the default terminal (Windows Terminal if installed) in the file's folder |

## 7. Conversions

### 7.1 v1: images, audio, video, archives, subtitles

182 input → output pairs. The format lists match Tangerine's published ones for these types, except that HEIC is input only (see below).

| Type | Inputs | Outputs | Engine |
|---|---|---|---|
| Images | JPG, PNG, WebP, HEIC/HEIF, TIFF, AVIF, BMP, SVG, camera RAW (CR2, CR3, NEF, ARW, RAF, ORF, RW2, DNG…) | JPG, PNG, WebP, TIFF, AVIF, BMP, PDF; RAW also → DNG | Magick.NET (in-process); dnglab for RAW → DNG |
| Audio | MP3, M4A, WAV, FLAC, OGG, Opus, AIFF, WMA | all of the inputs | FFmpeg |
| Video | MP4, MOV, MKV, WebM, AVI, WMV, GIF | all of the inputs, plus MP3 (except from GIF, which has no sound) | FFmpeg |
| Archives | ZIP, TAR, TAR.GZ, RAR | ZIP, TAR, TAR.GZ (RAR is input only) | SharpCompress (in-process) |
| Subtitles | SRT, VTT | SRT ↔ VTT; both → TXT | Pomegranate's own code |

Notes:
- **Video → video** copies the streams without re-encoding when the target container supports the existing codecs, which makes the change near-instant. Otherwise it re-encodes with the defaults in §11.
- **SVG** is input only. PDF input arrived in v2 (§7.2).
- **HEIC is input only.** The bundled ImageMagick build can read HEIC but has no HEIC encoder, and FFmpeg has no HEIF writer either (engine check, §13).
- **Correction from the interview:** Magick.NET can read camera RAW but cannot write DNG. RAW → DNG uses the separate `dnglab` command-line tool.
- **Several files at once:** a target is offered when every file either converts to it or is already in it. Files already in the target format are skipped.
- **Missing engines:** if an engine isn't installed (e.g. dnglab on ARM64), its conversions are removed from the wheel instead of failing.

### 7.2 v2: documents, slides and spreadsheets

Agreed in the v2 interview (2026-11-19). 97 new pairs, 279 in total.

| Type | Inputs | Outputs |
|---|---|---|
| Documents | DOCX, ODT, RTF, Markdown, HTML, EPUB, TXT; DOC (input only) | Each other, plus PDF. DOC → DOCX, PDF, ODT, RTF, HTML, TXT |
| PDF | PDF | DOCX, JPG, PNG, TXT, Markdown, EPUB, PPTX, XLSX |
| Slides | PPTX, ODP; PPT (input only) | PDF, JPG, PNG; PPTX ↔ ODP |
| Spreadsheets | XLSX, ODS, CSV, TSV; XLS (input only) | Each other, plus PDF |

Apple-only formats from Tangerine's list (Pages output, RTFD, text clippings) are left out.

**Engines.** Where two are listed, the first is used when installed and the second is the fallback.

| Conversion | Engine |
|---|---|
| DOCX, ODT, RTF between each other | Office (Word or LibreOffice Writer), else Pandoc |
| DOCX, ODT, RTF → PDF; DOC → anything | Office only |
| Anything to or from Markdown, HTML, EPUB, TXT | Pandoc |
| Markdown, HTML, EPUB, TXT → PDF | Pandoc makes one HTML page; the PC's Microsoft Edge prints it, headless and offline |
| PDF → JPG, PNG | Windows' built-in PDF renderer, one image per page |
| PDF → TXT, Markdown, XLSX | PdfPig: the text, with a simplified layout. XLSX: one sheet per page, text placed in cells by position |
| PDF → DOCX, EPUB | PdfPig (text and pictures), then Pandoc |
| PDF → PPTX | Each page becomes a picture slide (looks exact, not editable) |
| Slides → PDF, JPG, PNG; PPTX ↔ ODP | Office (PowerPoint or LibreOffice Impress) |
| CSV, TSV, XLSX, XLS between each other | Built in (ExcelDataReader + MiniExcel), values only. XLS → XLSX prefers Excel, which keeps formatting |
| Anything with ODS; spreadsheets → PDF | Office (Excel or LibreOffice Calc) |

**Which office app.** Microsoft Office handles its own formats and RTF; LibreOffice handles OpenDocument (ODT, ODS, ODP). Each falls back to the other when only one is installed. Word, Excel and PowerPoint are detected separately, so a PC with Word but no PowerPoint is offered document conversions but no slide conversions. With no office suite at all, those slices are hidden.

**Behaviour.**
- **Several pages** (PDF → images, slides → images) go into a new folder next to the original, `Report (pages)`, holding `Report - 01.png`, `Report - 02.png`, … A single page is just `Report.png`.
- **A workbook with several sheets** → CSV/TSV gives one file per sheet next to the original: `Budget - Sheet1.csv`, `Budget - Sheet2.csv`.
- **Password-protected** PDF, Word, Excel or PowerPoint files fail with "This file is password-protected." There are no password prompts.
- **PDF → DOCX never uses Word.** Word's PDF conversion shows a prompt that hidden automation can't answer (§13.1).
- **Office runs hidden** with macros forced off, one conversion per app at a time, and a 3-minute watchdog. Pomegranate only ever closes an Office process it started itself. An already open PowerPoint (single-instance) is used but never closed.
- **Pandoc runs sandboxed**: it reads only the input file and never goes online. Pictures that a Markdown or HTML file links to by path or URL are therefore not pulled in. Markdown output embeds its pictures as data URIs, so the file stands alone.
- **Edge prints** with a throwaway profile and every network lookup blocked.
- **ARM64**: Pandoc publishes no Windows ARM64 build; the x64 one runs under Windows' emulation.

### 7.3 v3: file tools

Agreed in the v3 interview (2026-11-20). Tools change a file without changing its type. They open from the **Ctrl** wheel. 20 tools, offered according to the file type. ✎ marks a tool that opens a small editor window; every other tool runs as soon as you drop.

| Files | Tools |
|---|---|
| Images | Compress, Resize, Rotate/flip, Crop ✎, Annotate ✎, Redact ✎, Strip metadata, Read QR code, Copy text |
| Video | Compress, Trim ✎, Crop ✎, Redact ✎, Speed, Rotate, Mute, Extract audio, Strip metadata |
| Audio | Compress, Trim ✎, Bleep ✎, Speed, Normalize volume, Strip metadata |
| PDF | Merge (2 or more PDFs), Split (one file per page), Pages ✎ (reorder, rotate, delete, save a selection), Compress, Strip metadata |
| Any files and folders | Zip (the selection becomes one ZIP); Extract (an archive becomes a folder) |

**Which files.** Image tools that write the image work on JPG, PNG, WebP, TIFF, AVIF and BMP (Compress skips BMP). HEIC, camera RAW and SVG can't be written (§7.1), so they get only Read QR code and Copy text. Video tools work on MP4, MOV, MKV, WebM, AVI and WMV (Compress on the first four); GIF has no tools. Compress for audio works on the lossy formats (MP3, M4A, OGG, Opus, WMA). A tool is offered for several files only when it works for every one of them, like Convert (§4.3). Folders get Zip only.

**Options on the outer ring.** Dropping on the tool itself uses the default (in bold).

| Tool | Options |
|---|---|
| Compress | Light, **Medium**, Strong |
| Resize | **50%**, 25%, long side 3840, 1920 or 1280 px (only ever shrinks) |
| Speed | 0.5×, 1.5×, **2×**, 4× (sound keeps its pitch) |
| Rotate | Left, **Right**, 180°, Flip |

**How each tool works.**
- **Compress.** Images: re-encoded at a lower quality; Medium and Strong also cap the long side. PNG becomes a 256-color PNG at Medium and Strong. Video: H.264 (VP9 for WebM) at a higher CRF; Medium and Strong also cap the height. Audio: a lower bitrate in the same format. PDF: pictures are re-encoded as JPEG and scaled down to a maximum resolution; text and drawings are untouched. If the result isn't smaller, no file is written and the notification says the file is already as small as it gets.
- **Resize, Rotate** re-encode with the quality settings in §11. Video rotation re-encodes too, so every player shows it the same way.
- **Strip metadata** removes location, camera, author and editing details. Images keep their color profile, and their rotation is applied first so they don't turn sideways. JPEG and PNG without rotation are cleaned without re-encoding. Video and audio are copied without re-encoding, dropping tags, chapters and cover art. PDF loses its title/author fields and XMP metadata.
- **Speed, Normalize volume** re-encode with §11's settings. Normalize uses FFmpeg's two-pass loudness normalization (EBU R128, −16 LUFS).
- **Mute, Extract audio** copy streams without re-encoding, so they're instant and lossless. Extract audio picks the file type from the audio inside (AAC → M4A, MP3 → MP3, Opus → Opus, and so on).
- **Merge** joins the PDFs in file-name order (numbers sorted as numbers). **Split** puts one PDF per page in a `(pages)` folder, as in §7.2.
- **Zip** makes one ZIP of everything dropped; folders keep their contents. **Extract** unpacks ZIP, TAR, TAR.GZ and RAR into a new folder named after the archive. If everything inside sits in one folder, that folder's contents go straight in, so you don't get a folder inside a folder. Entries that would land outside the new folder are skipped.
- **Read QR code, Copy text** put the result on the clipboard and show it in a notification. Links are never opened. Copy text uses Windows' built-in text recognition (OCR) in the languages installed on the PC.

**Editors.** Each is a small Fluent window with a preview, its controls and Save/Cancel. All of them work from the keyboard (Enter saves, Esc cancels, arrow keys nudge, Ctrl+Z undoes). They appear only when one file is dropped.

| Editor | What you do |
|---|---|
| Crop (image, video) | Drag the frame or its handles; aspect presets Free, Original, 1:1, 4:3, 16:9, 9:16. Video shows a frame from the clip; a slider picks which. Video crops are rounded to even sizes (H.264 needs them) |
| Trim (audio, video) | Drag the start and end handles on a timeline: a waveform for audio, a strip of frames for video. Audio can play the selection. Trimming re-encodes, so cuts land exactly where you put them |
| Bleep (audio) | Mark one or more ranges on the waveform; each becomes a 1 kHz tone or silence |
| Pages (PDF) | Thumbnails of every page: drag or Ctrl+arrows to reorder, rotate, delete. **Save** writes the whole document; **Save selection** writes only the selected pages |
| Redact (image, video) | Draw boxes; each becomes a black box (the default, and the only style that can't be undone by sharpening), pixelation or blur. On video the boxes stay put and apply between a start and end time; they don't follow moving objects. A redacted image also loses its metadata, because a camera's embedded thumbnail would still show the original |
| Annotate (image) | Arrow, rectangle, highlighter, pen and text, in six colors and three sizes |

**Output names.** The original is never changed. The result goes next to it (or into the output folder from Settings) as `name (suffix).ext`, with the usual `(1)` on clashes: `(compressed)`, `(resized)`, `(rotated)`, `(no metadata)`, `(2x)`, `(muted)`, `(normalized)`, `(cropped)`, `(trimmed)`, `(bleeped)`, `(redacted)`, `(annotated)`, `(pages)`, `(selection)`, `(merged)`. Extract audio writes `name.m4a` (or whichever type), Zip writes `name.zip` for one item and `<folder name>.zip` for several, Extract writes the folder `name`.

## 8. Output rules

- Output goes in the same folder as the original by default. Settings can change it to a fixed folder.
- If the name is taken, the new file becomes `name (1).ext`, `name (2).ext`, and so on.
- Conversions that produce several files follow §7.2: a `(pages)` folder for pages, `name - Sheet1.csv` files for sheets.
- The original file is never modified, moved or deleted by a conversion.
- Dropping several files converts them in parallel. Images use up to one job per CPU core; video uses at most 2 jobs at once.
- A small progress popup near the tray shows each job and has a Cancel button. When the batch finishes, a Windows notification appears with **Open folder**.
- On failure, any half-written output is deleted. The notification says which file failed and has a **Details** button that opens the log entry.

## 9. Engines and licenses

| Engine | Use | License | How it ships |
|---|---|---|---|
| FFmpeg 9.0.2 (BtbN full GPL build, shared-library variant, x64 + ARM64) | Audio, video | GPL | Separate `ffmpeg.exe`/`ffprobe.exe` plus their DLLs, run as a child process. License notice and source link in About and `THIRD-PARTY-NOTICES.md` |
| Magick.NET 15.1 (ImageMagick 7.1.2, Q16) | Images | Apache-2.0 (ImageMagick license) | NuGet (`Magick.NET-Q16-AnyCPU`) |
| SharpCompress | Archives | MIT | NuGet |
| dnglab 0.8.0 | RAW → DNG | LGPL-2.1 | Separate `dnglab.exe`, run as a child process. x64 only for now |
| Pandoc 3.14 | Text documents | GPL-2.0-or-later | Separate `pandoc.exe` (230 MB), run as a child process. The x64 build on both CPU types |
| PdfPig 0.1.16 | PDF text and pictures | Apache-2.0 | NuGet |
| ExcelDataReader 3.9 | Reading XLS, XLSX | MIT | NuGet |
| MiniExcel 1.47 | Writing XLSX | Apache-2.0 | NuGet |
| PDFsharp 6.2.4 | PDF tools: merge, split, pages, compress, metadata | MIT | NuGet. Pinned: 6.2.4 is the last MIT release (6.5 moved to AGPL-3.0, which would force the whole app under AGPL) |
| ZXing.Net 0.16 | Reading QR codes and barcodes | Apache-2.0 | NuGet |
| Windows text recognition (Windows.Media.Ocr) | Copy text | Part of Windows | Not bundled; uses the PC's installed languages |
| Windows PDF renderer (Windows.Data.Pdf) | PDF pages → images | Part of Windows | Not bundled |
| Microsoft Edge | Markdown, HTML, EPUB, TXT → PDF | The PC's own | Not bundled |
| Microsoft Office or LibreOffice | Office layouts, OpenDocument, old formats | The PC's own | Not bundled; used only if installed |

Because FFmpeg runs as a separate program, Pomegranate's own code can stay MIT.

Engine binaries are downloaded at **build** time by `tools/fetch-engines.ps1` from pinned versions with SHA-256 checks. The app never downloads anything.

## 10. App shell

- **No main window.** Pomegranate lives in the system tray. Left-click opens Settings. Right-click shows Pause/Resume, Settings, Help, Quit.
- Starts when Windows starts (on by default, can be switched off).
- Only one copy runs at a time. A second launch hands its files to the running one.
- Logs and the welcome sample live in the app's data folder. For the installed app that's inside its package folder (`…\Packages\<family>\LocalCache\Local\Pomegranate`), because Windows redirects an installed app's AppData writes there and File Explorer would otherwise open the wrong folder.
- **First run**: a welcome window with a sample file you drag to try each wheel, and a link to Settings.
- **Icon**: an original, simple pomegranate drawn as SVG (deep red fruit, small crown). The brand red appears only on the icon and the Store page. The wheel uses the system accent color.

## 11. Settings (v1)

| Setting | Default |
|---|---|
| Convert / Tools / Actions keys | Shift / Ctrl / Alt |
| Hold delay | 250 ms |
| Hotkey | Win+Alt+C (changed from Ctrl+Alt+Space, which was already taken on the test PC and clashes with AltGr layouts) |
| Output location | Next to the original |
| Start when Windows starts | On (installed app only; development builds never touch the PC's startup programs) |
| Pause while a full-screen app or game is open | On |
| Apps where the wheel never opens | Empty list |
| JPEG / WebP / AVIF / HEIC quality | 85 / 80 / 60 / 70 |
| Video (MP4, MOV, MKV) | H.264, quality CRF 23, preset "medium", AAC 192 kbps |
| Video (AVI) | H.264, quality CRF 23, MP3 audio (AAC in AVI plays badly in many players) |
| Video (WebM) | VP9 CRF 32 + Opus 128 kbps |
| Video (WMV) | WMV2 + WMA 192 kbps |
| GIF from video | 15 fps, max width 640 px, generated color palette |
| Audio bitrate | MP3 VBR ~190 kbps, AAC/M4A 192 kbps, Opus 128 kbps, OGG Vorbis quality 5, WMA 192 kbps. WAV/AIFF 16-bit PCM, FLAC lossless |
| PDF pages as images (v2) | 300 DPI |
| Slides as images (v2) | 1920 px wide |
| Compress images (v3) | JPEG/WebP/AVIF quality Light 80, Medium 65, Strong 50; long side capped at 2560 px (Medium) and 1600 px (Strong) |
| Compress video (v3) | H.264 CRF Light 26, Medium 28, Strong 31 (VP9 +6); height capped at 1080 (Medium) and 720 (Strong); AAC 128 / 96 / 64 kbps |
| Compress audio (v3) | MP3 160 / 128 / 96 kbps; AAC, WMA and Opus scale the same way; OGG quality 4 / 3 / 1 |
| Compress PDF pictures (v3) | At most 200 / 150 / 100 DPI, JPEG quality 80 / 70 / 55 |

Converting never shows an options dialog; it always uses these defaults. The compress levels have their own section in Settings.

## 12. Architecture

### 12.1 Projects

```
src/
  Pomegranate.App/       WPF: tray, wheel overlay, settings, first run, Float tray, the tool editors (Editors/).
                         MSIX packaging lives here.
  Pomegranate.Core/      No UI: format catalog, tool catalog, job and tool runners, output naming, settings model.
                         Fully unit-testable.
  Pomegranate.Engines/   Adapters for FFmpeg, Magick.NET, SharpCompress, subtitles, dnglab, Pandoc, Office,
                         PdfPig, PDFsharp, ZXing.Net and Windows' PDF renderer and OCR (Tools/ holds the tools).
  Pomegranate.Shell/     Small C++ DLL: the Windows 11 right-click command (IExplorerCommand). Built by
                         tools/build-shell.ps1, not part of the .NET solution.
tests/
  Pomegranate.Core.Tests/
  Pomegranate.Engines.Tests/
  samples/               Optional real-world HEIC / RAW / DNG / RAR files for the tests that need them.
packaging/
  AppxManifest.template.xml
assets/
  pomegranate.svg        Source of the icon; tools/make-icons.cs renders the .ico and logos.
tools/
  fetch-engines.ps1      Build-time download of pinned engine binaries with SHA-256 checks.
  build-shell.ps1        Compiles the right-click DLL with MSVC.
  package-msix.ps1       Builds the MSIX for one CPU type.
  make-icons.cs          Renders the icon set.
docs/
  SPEC.md
  CHECKLIST.md           The by-hand test list for each phase.
```

### 12.2 Drag detection and the wheel

1. Low-level mouse and keyboard hooks (`WH_MOUSE_LL`, `WH_KEYBOARD_LL`) run on their own thread. They watch for: left button held, mouse moved past the system drag threshold, and an activation key held for the hold delay.
2. When that happens, the app shows a **transparent, topmost, borderless window** under the cursor and registers it as an OLE drop target. It sends a one-pixel mouse nudge so Windows' drag loop notices the new window right away.
3. The wheel is drawn only after `DragEnter` confirms the drag carries files (`CF_HDROP`). Dragging text, a window or a selection box never shows a wheel.
4. `DragOver` highlights the slice under the cursor. `Drop` runs that slice.
5. **The drop always reports "copy", never "move".** Otherwise Explorer could delete the source file after a drop.
6. In v1, only real files are supported. "Virtual" files (e.g. Outlook attachments, some browser downloads) that don't carry `CF_HDROP` show "Save the file first" in the wheel.

### 12.3 Right-click menu and hotkey

- The Windows 11 right-click entry is an `IExplorerCommand` in `Pomegranate.Shell.dll`. The MSIX manifest registers it as a COM surrogate server. It starts `Pomegranate.exe --wheel <paths>` (or `--wheel-list <file>` for very long selections), and that launch hands the paths to the running app over its single-instance pipe. It only exists in the installed MSIX; development builds have no right-click entry.
- The hotkey (`RegisterHotKey`) reads the selection from the foreground Explorer window or the desktop through the Shell COM API (`IShellWindows` → `IFolderView2`).

### 12.4 Jobs

- Each dropped file becomes a job in a `Channel`-based queue, with per-engine limits on how many run at once (§8).
- FFmpeg progress is read from `-progress pipe:1`. Cancel kills the process (and its children); the half-written temporary file is deleted.
- Output is written to a temporary name in the target folder and renamed to the final name only on success.

### 12.5 Privacy guard

The app contains no networking code. A unit test (run locally and in CI) fails if `System.Net.Http`, `HttpClient`, `System.Net.Sockets`, `WebClient` or similar appear in any `src/` file.

## 13. Engine check (done 2026-11-18, on x64)

| Question | Result |
|---|---|
| Magick.NET formats | AVIF, WebP, TIFF, JPEG, BMP, PNG read and write. SVG read (librsvg). CR2, CR3, NEF, ARW, RAF, ORF, RW2, DNG read. **HEIC/HEIF read only** (no encoder) → HEIC is input only |
| PDF writing | Works without Ghostscript (writes PDF 1.3). PDFsharp not needed |
| FFmpeg encoders | Everything v1 needs is present: libx264, libvpx-vp9, libopus, libmp3lame, libvorbis, aac, flac, pcm, wmav2, wmv2, gif (plus libaom/SVT-AV1/x265, unused) |
| FFmpeg size | The static build is 168 MB per executable. The shared build is 182 MB for ffmpeg + ffprobe together, so it's the one we bundle |
| dnglab | LGPL-2.1. Windows release is x64 only. ARM64 needs a Rust build from source; until then RAW → DNG is hidden on ARM64 |
| HEVC patents | Moot, since nothing encodes HEVC |
| .NET 10 SDK | Installed (10.0.404) |

Still to check:
1. **Magick.NET on ARM64**: confirm the same format list on an ARM64 machine (CI runner `windows-11-arm`).
2. **Drag reliability**: test drags from Explorer, the desktop, File Explorer search results, browsers, and Outlook. Check that Explorer's own move/copy cursor during the 250 ms delay doesn't flicker badly.
3. **Store policy**: confirm the Store accepts a free app that bundles GPL FFmpeg (VLC and others are listed there, so this is likely fine).
4. **Pinned FFmpeg URL**: BtbN removes old autobuild releases. Before release, mirror the pinned zips to Pomegranate's own GitHub release.

### 13.1 v2 engine check (2026-11-19, x64 with Microsoft 365 installed)

| Question | Result |
|---|---|
| Pandoc 3.14 | Converts every v2 format. DOCX → Markdown came back identical to the source |
| Word, Excel, PowerPoint automation | Hidden: DOCX → PDF 6 s, CSV → PDF 4 s, PPTX → PDF plus slide PNGs 7 s |
| Word PDF → DOCX | Hangs: Word shows a confirmation prompt that hidden automation can't answer. Not used; PdfPig instead |
| Excel `Workbooks.Open` | A dummy write-reservation password makes it fail on ordinary workbooks, so only a dummy open password is passed |
| Office processes | Excel and PowerPoint stayed running after `Quit` because .NET still held COM references. Each conversion now releases them and waits for (or closes) the app it started before reporting back. Verified: no Office process left after the full test run |
| Windows PDF renderer | A page → 2550×3300 PNG at 300 DPI in 1.4 s |
| PdfPig | All text recovered, including a table |
| LibreOffice | Not installed on the test PC, so its path is untested here. CI can install it to cover it |

### 13.2 v3 tool check (2026-11-20, x64)

| Question | Result |
|---|---|
| PDFsharp license | 6.5.0 (the newest) is **AGPL-3.0**; 6.2.4 is the last MIT release, so it's pinned there (§9) |
| PDFsharp's dependency | It asks for System.Security.Cryptography.Pkcs 8.0.1, which has known vulnerabilities; pinned to 10.0.15 |
| Windows OCR | Reads a rendered test line correctly with the PC's English language pack |
| ZXing.Net | Reads back a generated QR code exactly |
| Lossless metadata strip | JPEG and PNG come out pixel-identical, without EXIF, XMP, comments or text chunks; the color profile stays |
| FFmpeg filters | A separate `-vf` can't follow a `-filter_complex` stream, so Speed and Redact do the even-size scaling inside the graph |
| Editors | All six render in light and dark themes; the Annotate layer is saved at the picture's full size |

## 14. Known limits

- Windows blocks the wheel over apps running as administrator (e.g. an admin Explorer window or Task Manager). This is a Windows security rule (UIPI) that Pomegranate cannot work around.
- Touch and pen activation come later.
- Video redaction boxes don't follow moving objects (§7.3).
- Copy text only reads languages installed in Windows (Settings → Time & language → Language & region).

## 15. Testing

- **Unit tests** for `Core`: format catalog, which formats a mixed drop shows, output naming and clashes, settings.
- **Engine tests**: for each conversion pair, generate a tiny sample at test time (FFmpeg test patterns and tones, Magick-generated images, small archives, SRT/VTT text). Then convert it and check the output with `ffprobe` / Magick that it opens and is the right format.
- **CI**: GitHub Actions builds and tests on every push, on `windows-latest` (x64) and `windows-11-arm` (ARM64), and runs the networking check (§12.5).
- **By hand**: before a phase is called done, check drag, wheel, right-click and hotkey on your PC against a written checklist.

## 16. Decision log

| # | Decision |
|---|---|
| 1 | From-scratch Windows app, original name and look; nothing copied from Tangerine |
| 2 | Free and open source; no trial or paywall |
| 3 | Three phases: v1 media + actions, v2 documents, v3 tools |
| 4 | Shift / Ctrl / Alt with a 250 ms hold delay; keys can be changed |
| 5 | Right-click menu, global hotkey, keyboard control in the wheel; touch later |
| 6 | C# / .NET 10 / WPF |
| 7 | Windows 11 only, x64 + ARM64 |
| 8 | Output next to the original, `name (1)` on clashes, originals untouched, parallel batches, progress popup and notification |
| 9 | Name: **Pomegranate** |
| 10 | FFmpeg, Magick.NET, SharpCompress, own subtitle code; format lists match Tangerine's (HEIC input only after the engine check) |
| 11 | Fully offline, ~200 MB, MSIX via Store + GitHub + winget |
| 12 | Original Windows 11 wheel: accent color, ≤8 slices, outer overflow ring |
| 13 | Actions: Share, Float tray, Copy to…, Move to…, Copy Path, Terminal |
| 14 | Settings as in §11; no options dialog when converting |
| 15 | Code in `D:\Pomegranate` with git; push to GitHub only on request |
| 16 | Automated tests per conversion, CI for x64 + ARM64, manual check per phase |
| 17 | Original pomegranate icon; brand red only on icon and Store page |
| 18 | Tray app, no main window, starts with Windows |
| 19 | Mixed drops show shared formats only; folders get Actions only |
| 20 | No network access at all |
| 21 | First-run welcome with a practice file |
| 22 | English first, ready for translation; Narrator, high contrast, reduced motion |
| 23 | MIT license; FFmpeg as a separate GPL program |
| 24 | v2/v3 details decided in a short interview at the start of each phase |
| 25 | v2 formats: Tangerine's list without Apple-only formats, plus ODT, RTF, HTML, ODP, ODS, and DOC/PPT/XLS as input |
| 26 | Office files → PDF or images through the PC's Microsoft Office, else LibreOffice; hidden when neither is installed (nothing bundled) |
| 27 | PDF input: Windows' renderer for images, PdfPig for text; PDF → PPTX as picture slides |
| 28 | Pandoc for text documents; Edge prints Markdown, HTML, EPUB and TXT to PDF |
| 29 | Spreadsheet data conversions built in; one CSV per sheet |
| 30 | v2 before v3 |
| 31 | PDF → DOCX always through PdfPig, never Word |
| 32 | Microsoft Office for its own formats and RTF, LibreOffice for OpenDocument, each falling back to the other |
| 33 | Several pages go into a `(pages)` folder; one page is one file |
| 34 | 300 DPI for PDF pages and 1920 px for slides, both adjustable in Settings |
| 35 | Password-protected files fail with a clear message; no prompts |
| 36 | v3 tools: the 20 in §7.3; remove background, PDF passwords and watermarks later |
| 37 | Tool options on the outer ring; dropping on the tool uses its default; compress levels adjustable in Settings |
| 38 | Tools never overwrite: `name (suffix).ext` next to the original |
| 39 | Instant tools run on every selected file; Merge in file-name order; editors only for one file |
| 40 | QR and OCR results go to the clipboard and a notification; links never open |
| 41 | PDFsharp (MIT) for PDF tools, ZXing.Net (Apache-2.0) for codes, Windows OCR; no new bundled programs |
| 42 | Instant tools first, then editors: Crop, Trim/Bleep, Pages, Redact, Annotate |
| 43 | v2 committed on `v2/documents`; v3 on `v3/tools` |

Defaults I picked that weren't discussed (easy to change): the quality values in §11, the Win+Alt+C hotkey, the concurrency limits in §8, the Float tray holding references rather than copies, and dropping in the wheel's center to cancel.
