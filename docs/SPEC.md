# Pomegranate — Spec

Status: agreed in the design interview on 2026-11-18. v1 is fully specified. v2 and v3 are scoped here; their details get decided in a short interview at the start of each phase.

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
| Install size | Measured on x64: **175 MB download, 442 MB installed**: engines 196 MB (FFmpeg 182, dnglab 13) and the app with its own .NET runtime ~245 MB (Store MSIX packages can't use a shared .NET install). Every engine is bundled; nothing is downloaded at runtime. Possible later cut: replace the Windows Forms tray icon with direct Win32 calls (~20 MB) |
| Language | English in v1. All strings live in `.resx` files so the app can be translated later |

## 3. Phases

| Phase | Scope |
|---|---|
| **v1** | App shell (tray, settings, first run), drag detection and the wheel, right-click menu, hotkey, keyboard control, conversions for **images, audio, video, archives, subtitles**, all **6 actions** |
| **v2** | Conversions for **PDF, documents, slides, spreadsheets, EPUB**. Engine choice (LibreOffice vs Pandoc, how to do PDF → Word) decided at the start of v2 |
| **v3** | The **file tools**: compress, crop, trim, split, merge, redact, annotate, strip metadata, speed change, extract audio, read QR, reorder PDF pages, and others. Which tools need their own editor window is decided at the start of v3 |
| Later | Touch and pen activation, translations |

## 4. How you use it

### 4.1 Drag and hold

| Hold while dragging | Wheel |
|---|---|
| **Shift** | Convert |
| **Ctrl** | Tools (v3; in v1 it shows a "Coming soon" ring) |
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

## 7. Conversions in v1

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
- **SVG** is input only. **PDF** is image output only in v1. PDF input arrives in v2.
- **HEIC is input only.** The bundled ImageMagick build can read HEIC but has no HEIC encoder, and FFmpeg has no HEIF writer either (engine check, §13).
- **Correction from the interview:** Magick.NET can read camera RAW but cannot write DNG. RAW → DNG uses the separate `dnglab` command-line tool.
- **Several files at once:** a target is offered when every file either converts to it or is already in it. Files already in the target format are skipped.
- **Missing engines:** if an engine isn't installed (e.g. dnglab on ARM64), its conversions are removed from the wheel instead of failing.

## 8. Output rules

- Output goes in the same folder as the original by default. Settings can change it to a fixed folder.
- If the name is taken, the new file becomes `name (1).ext`, `name (2).ext`, and so on.
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

Converting never shows an options dialog; it always uses these defaults.

## 12. Architecture

### 12.1 Projects

```
src/
  Pomegranate.App/       WPF: tray, wheel overlay, settings, first run, Float tray. MSIX packaging lives here.
  Pomegranate.Core/      No UI: format catalog, job queue, output naming, settings model. Fully unit-testable.
  Pomegranate.Engines/   Adapters for FFmpeg, Magick.NET, SharpCompress, subtitles, dnglab.
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

## 14. Known limits

- Windows blocks the wheel over apps running as administrator (e.g. an admin Explorer window or Task Manager). This is a Windows security rule (UIPI) that Pomegranate cannot work around.
- Touch and pen activation come later.

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

Defaults I picked that weren't discussed (easy to change): the quality values in §11, the Win+Alt+C hotkey, the concurrency limits in §8, the Float tray holding references rather than copies, and dropping in the wheel's center to cancel.
