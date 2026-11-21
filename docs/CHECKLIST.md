# By-hand checklist

Automated tests cover the conversion table, file naming, settings and wheel geometry. The things below need a person, a mouse and real apps. Tick them on your PC before calling v1 done.

## Run the development build

```
dotnet run --project src/Pomegranate.App
```

The tray icon appears and, the first time, the welcome window opens. Quit from the tray icon's menu.

## Wheel by drag

- [ ] Drag the welcome window's sample, hold **Shift**: after about a quarter second the Convert wheel opens at the cursor.
- [ ] Drop on **WebP**: `sample.webp` appears in the sample folder; a notification says "Converted to WebP"; clicking it shows the file in Explorer.
- [ ] Drag a file **from File Explorer**, hold Shift, drop on a format. Same result next to the original.
- [ ] Same from the **desktop** and from **Explorer search results**.
- [ ] While the wheel is open, press **Alt**: it switches to Actions. **Ctrl**: Tools.
- [ ] Let go of Shift with the wheel open: it stays open.
- [ ] Drop on the **center**: nothing happens, and the file stays where it was.
- [ ] Press **Esc** mid-drag: the wheel and the drag both cancel.
- [ ] Drag the cursor **out of the wheel**: it closes, and the drag carries on normally.
- [ ] A **quick tap** of Shift during a drag doesn't open the wheel, and Explorer still moves the file on drop.
- [ ] Holding **Ctrl before** starting a drag (normal Explorer copy) never opens the wheel.
- [ ] Select **several images** of different types: the wheel offers formats they share; files already in the chosen format are skipped.
- [ ] An **image plus a video**: "These files have no format in common."
- [ ] A **folder**: Convert says folders can't be converted; Actions works.
- [ ] A file with more than 8 targets (e.g. a WAV): **More** opens an outer ring.
- [ ] **Outlook attachment** or browser download-bar item: "Save the file first…".
- [ ] Drag near a **screen edge** and on a **second monitor** (ideally one with a different scale): the wheel stays fully visible and sized right.
- [ ] Over an app running **as administrator**: the wheel doesn't open (known Windows limit).

## Conversions

- [ ] A long video (a minute or more) to MP4/WebM: the progress window appears near the tray; **Cancel** stops it and leaves no half-written file.
- [ ] Something that fails (e.g. rename a .txt to .mp4 and convert it): the notification says what failed; clicking it opens the log.
- [ ] A **HEIC** photo from a phone converts to JPG upright (rotation applied).
- [ ] A camera **RAW** file converts to DNG and JPG.
- [ ] A **RAR** archive converts to ZIP.

## Actions (hold Alt)

- [ ] **Share** opens the Windows share panel with the file.
- [ ] **Float** adds the file to the floating tray; dragging it out of the tray into another app works.
- [ ] **Copy to…** with no folders yet asks for a folder; afterwards the folder appears on the outer ring when hovering Copy to….
- [ ] **Move to…** moves the file; a name clash shows Windows' Replace/Skip dialog.
- [ ] **Copy path** puts the full path on the clipboard.
- [ ] **Terminal** opens Windows Terminal (or PowerShell) in the file's folder.

## Hotkey and keyboard

- [ ] Select files in Explorer and press **Win+Alt+C**: the wheel opens at the cursor.
- [ ] With **two Explorer tabs**, the hotkey uses the active tab's selection.
- [ ] Select files on the **desktop** and press the hotkey.
- [ ] In the wheel: **arrow keys** move, **Up/Down** enter/leave the outer ring, **Enter** runs, **Tab** switches wheels, **Esc** closes.
- [ ] Turn on **Narrator**: moving through the wheel reads out each slice.

## Settings and tray

- [ ] Change the wheel keys: picking a key another wheel uses swaps them. The new keys work at once.
- [ ] Change the hold delay to 0 and to 1000 ms: the wheel timing follows.
- [ ] Set a new hotkey in Settings: the old one stops, the new one works.
- [ ] Set output to a fixed folder: conversions land there.
- [ ] Tray → **Pause**: no wheel. **Resume**: wheel again.
- [ ] Start a full-screen game or video with "Pause while a full-screen app is open" on: no wheel.
- [ ] Add `explorer.exe` to the excluded apps: no wheel when dragging from Explorer. Remove it again.
- [ ] Switch Windows between **light and dark** mode, change the **accent color**, turn on **high contrast**, turn **Animation effects** off: the wheel follows each.

## v2: documents, slides and spreadsheets

- [ ] A **PDF** → PNG: a `(pages)` folder with one image per page; the notification says "N pages" and clicking it opens the folder.
- [ ] A one-page PDF → JPG: a single `name.jpg`, no folder.
- [ ] A **PDF** → DOCX opens in Word with the text and pictures (layout simplified).
- [ ] A **DOCX** → PDF looks like it does in Word (headers, fonts, page breaks).
- [ ] A **DOCX** → Markdown, and the Markdown → DOCX again.
- [ ] A **PPTX** → PNG: one image per slide, 1920 px wide.
- [ ] An **XLSX with several sheets** → CSV: one `name - Sheet.csv` per sheet; Excel opens them with the right columns.
- [ ] A **CSV** → XLSX keeps numbers as numbers and postcodes like `01234` as text.
- [ ] A password-protected DOCX or PDF: the notification says it's password-protected; nothing hangs.
- [ ] While a Word/Excel conversion runs, open Word or Excel yourself: your window works normally, and after the conversion no hidden Office copy is left in Task Manager.
- [ ] With PowerPoint already open, convert a PPTX → PDF: your PowerPoint stays open.

## v3: tools (hold Ctrl)

Instant tools:

- [ ] A phone photo → **Compress** (drop on the tool itself = Medium): `photo (compressed).jpg`, smaller, upright. Hover Compress: Light / Medium / Strong on the outer ring.
- [ ] Compress the result again with Light: the notification says it's already as small as it gets, and no file appears.
- [ ] **Resize** → 1920 px on a big photo; on a small one it says it's already that small.
- [ ] **Rotate** → Left, Right, 180°, Flip on a photo and on a video.
- [ ] **Strip metadata** on a phone photo with location: File Explorer → Properties → Details shows no GPS, camera or date taken; the picture is upright and its colors unchanged.
- [ ] **Read QR code** on a screenshot of a QR code: the text is on the clipboard and in the notification; a link isn't opened.
- [ ] **Copy text** on a screenshot of a web page: the text is on the clipboard.
- [ ] A video → **Compress**, **Speed** (2×, 0.5×), **Mute**, **Extract audio** (`clip.m4a` for an iPhone video), **Strip metadata**.
- [ ] A quiet recording → **Normalize**: clearly louder, no clipping.
- [ ] Two PDFs `Part 2.pdf` and `Part 10.pdf` → **Merge**: `Part 2 (merged).pdf`, Part 2's pages first.
- [ ] A PDF → **Split**: a `(pages)` folder; a one-page PDF says it has only one page.
- [ ] A scanned PDF → **Compress**: smaller, text still selectable.
- [ ] A folder → the Tools wheel offers only **Zip**; several files → `<folder name>.zip`.
- [ ] A ZIP whose contents sit in one folder → **Extract**: one folder, not a folder inside a folder.
- [ ] Select an image and a video together: Compress, Rotate and Strip metadata are offered; editors aren't.

Editors (one file at a time; Enter saves, Esc cancels, Ctrl+Z undoes):

- [ ] The editor window comes to the front with the keyboard focus after a drop from Explorer.
- [ ] **Crop** a photo with 16:9, then a video (the slider shows other frames). Output sizes are right; the video plays.
- [ ] **Trim** an audio file: Space plays the kept part; the result starts and ends where the handles were.
- [ ] **Trim** a video: the large picture follows the handle; the result is cut at those frames.
- [ ] **Bleep** two ranges with Bleep, then with Silence; listen to the results.
- [ ] **Pages**: drag pages to reorder, Ctrl+Left/Right, rotate, delete, Undo; Save gives `(pages).pdf`, Save selection gives `(selection).pdf`.
- [ ] **Redact** a photo with all three styles: in the result, black boxes are solid, the others unreadable; Properties → Details shows no camera data.
- [ ] **Redact** a video between two times: the boxes appear only then.
- [ ] **Annotate**: arrow, rectangle, highlighter, pen and text in a few colors and sizes; the result matches what was drawn.
- [ ] Narrator reads the editor's buttons, colors and pages.
- [ ] Settings → **Tools**: change Strong's image quality to 30; Compress → Strong is visibly stronger. Reset to defaults works.

## Installed MSIX (right-click menu and start with Windows)

The right-click entry and start-with-Windows only exist in the installed package. Installing a package you built yourself needs one of these, and both change Windows settings, so they're yours to do:

1. **Developer Mode** (Settings → System → For developers), then in PowerShell:
   `Add-AppxPackage -Register artifacts\msix-layout\x64\AppxManifest.xml`
2. Or sign the package with a certificate trusted in *Trusted People*, then double-click the `.msix`.

Then:

- [ ] Right-click a file in Explorer: **Open in Pomegranate** is in the main Windows 11 menu and opens the wheel.
- [ ] Right-click a **folder** and a selection of many files.
- [ ] Sign out and in: Pomegranate starts by itself (if "Start when Windows starts" is on). Turning it off in Settings stops that.
- [ ] Settings → **Open logs** shows today's log, and Welcome → **Open the sample folder** shows `sample.png` (the installed app keeps these inside its package folder).
- [ ] Uninstall from Settings → Apps: the right-click entry disappears.
