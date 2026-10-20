<p align="center">
  <img src="docs/assets/banner.svg" alt="DropWheel — Drop. Spin. Convert. A free offline file converter for Windows" width="100%">
</p>

<p align="center">
  <b>English</b> · <a href="README.ru.md">Русский</a>
</p>

<p align="center">
  <b>DropWheel</b> is a free, offline file converter for Windows that lives in your tray:<br>
  drag a file, press <kbd>Space</kbd>, drop it on a format — the converted file appears right next to the original.
</p>

<p align="center">
  <a href="https://github.com/Luchano55/dropwheel/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Luchano55/dropwheel/build.yml?label=build&logo=githubactions&logoColor=white" alt="Build status"></a>
  <a href="https://github.com/Luchano55/dropwheel/releases/tag/dev-build"><img src="https://img.shields.io/github/v/release/Luchano55/dropwheel?include_prereleases&sort=date&display_name=tag&label=latest%20build&color=0078D4" alt="Latest build"></a>
  <a href="#install"><img src="https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64)-0078D4" alt="Windows 10 | 11"></a>
  <a href="https://dotnet.microsoft.com/download/dotnet/8.0"><img src="https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet&logoColor=white" alt=".NET 8"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="License: MIT"></a>
  <a href="#privacy"><img src="https://img.shields.io/badge/100%25-offline-2ea44f" alt="100% offline"></a>
  <a href="#privacy"><img src="https://img.shields.io/badge/telemetry-none-2ea44f" alt="No telemetry"></a>
</p>

<p align="center">
  <a href="https://github.com/Luchano55/dropwheel/releases/tag/dev-build"><img src="https://img.shields.io/badge/Download-for%20Windows-0078D4?style=for-the-badge" alt="Download for Windows"></a>
</p>

<p align="center">
  <a href="#how-it-works">How it works</a> ·
  <a href="#features">Features</a> ·
  <a href="#supported-formats">Formats</a> ·
  <a href="#install">Install</a> ·
  <a href="#faq">FAQ</a> ·
  <a href="#roadmap">Roadmap</a>
</p>

<!--
  DEMO GIF. docs/assets/demo.gif is currently a drawn placeholder animation (it says "Illustration").
  Roman: record the real thing with ScreenToGif (about 800 px wide, 15 fps, under 5 MB) and overwrite
  docs/assets/demo.gif with the same file name — the README picks it up automatically.
  Step-by-step instructions: docs/github-setup.md, section "Демо-GIF".
-->
<p align="center">
  <img src="docs/assets/demo.gif" alt="Animation: drag photo.heic, press Space, drop it on JPG — photo.jpg appears next to the original" width="800">
</p>

## How it works

<p align="center">
  <img src="docs/assets/how-it-works.svg" alt="1. Drag a file. 2. Press Space while still dragging. 3. Drop it on a format — the result appears next to the original." width="100%">
</p>

1. **Drag** any file — from Explorer, the desktop, or a folder window. Keep holding the mouse button.
2. **Press <kbd>Space</kbd>.** A wheel of formats pops up right under your cursor. Press <kbd>Space</kbd> again to switch between **Convert** and **Tools**; <kbd>Esc</kbd> closes it.
3. **Drop** the file on a sector. That's it — `photo.heic` becomes `photo.jpg` in the same folder, and a notification tells you when it's done.

Not a fan of drag-and-drop? Right-click a file → **«Конвертировать…»** (Convert…) in the Explorer menu does the same.

## Features

<table>
  <tr>
    <td width="33%" valign="top">
      <h3>🎯 Right under your cursor</h3>
      No app window to open, no “Save as” dialogs. The wheel appears exactly where you are already dragging.
    </td>
    <td width="33%" valign="top">
      <h3>✈️ 100% offline</h3>
      Every conversion runs on your PC with bundled ffmpeg, ImageMagick, qpdf and poppler. Nothing is uploaded — works on a plane.
    </td>
    <td width="33%" valign="top">
      <h3>🛡️ Originals stay untouched</h3>
      The source file is never modified or deleted. Name already taken? You get <code>photo (1).jpg</code>.
    </td>
  </tr>
  <tr>
    <td width="33%" valign="top">
      <h3>📚 Many files at once</h3>
      Select several files: convert them all, merge PDFs, put photos into one PDF, or pack everything into a single ZIP.
    </td>
    <td width="33%" valign="top">
      <h3>⚡ Quiet background queue</h3>
      Up to two jobs in parallel, progress in the tray, a Windows notification with <i>Show in folder</i> when done.
    </td>
    <td width="33%" valign="top">
      <h3>🖱️ Explorer integration</h3>
      «Конвертировать…» in the right-click menu, for one file or a whole selection. Per-user, no admin rights needed.
    </td>
  </tr>
  <tr>
    <td width="33%" valign="top">
      <h3>📦 Portable</h3>
      Unzip and run. No installer, no .NET runtime to install, no admin rights.
    </td>
    <td width="33%" valign="top">
      <h3>🔤 Any file name</h3>
      Cyrillic, spaces, long paths — <code>Мои фото\тест 1.heic</code> just works.
    </td>
    <td width="33%" valign="top">
      <h3>💸 Free &amp; open source</h3>
      MIT-licensed. No ads, no trial, no account, no limits.
    </td>
  </tr>
</table>

## Screenshots

<!--
  These PNGs are rendered by CI: tests/DropWheel.Windows.Tests (WheelScreenshots) → workflow artifact "screenshots".
  Download the artifact from the latest "build" run and copy the files into docs/assets/screenshots/ with the same names.
-->
<table>
  <tr>
    <td align="center" width="50%">
      <img src="docs/assets/screenshots/wheel-photo-convert.png" alt="The wheel for a photo: JPG, PNG, WebP, AVIF, PDF" width="300"><br>
      <sub><b>Photo → Convert</b> — the sector under the cursor is highlighted</sub>
    </td>
    <td align="center" width="50%">
      <img src="docs/assets/screenshots/wheel-photo-tools.png" alt="The wheel for a photo in Tools mode: compress, downscale, fit to 1920 px, strip metadata" width="300"><br>
      <sub><b>Photo → Tools</b> — press <kbd>Space</kbd> again to switch</sub>
    </td>
  </tr>
  <tr>
    <td align="center" width="50%">
      <img src="docs/assets/screenshots/wheel-video-convert.png" alt="The wheel for a video: MP4, WebM, GIF, MP3" width="300"><br>
      <sub><b>Video</b> — MP4, WebM, GIF or just the audio as MP3</sub>
    </td>
    <td align="center" width="50%">
      <img src="docs/assets/screenshots/wheel-pdf-convert.png" alt="The wheel for a PDF: pages to PNG or JPG" width="300"><br>
      <sub><b>PDF</b> — every page as an image</sub>
    </td>
  </tr>
</table>

<sub>The interface is in Russian for now — format names are the same in every language. See the <a href="#faq">FAQ</a>.</sub>

## Supported formats

The wheel only offers what makes sense for the file you are dragging: a `.jpg` never gets “→ JPG”, a video never gets “→ PDF”. With several files of different types you get the operations they have in common.

<details open>
<summary><b>🖼️ Images</b> — JPG, PNG, WebP, HEIC, AVIF, BMP, TIFF, GIF</summary>

<br>

**Input:** JPG / JPEG / JFIF, PNG, WebP, HEIC / HEIF (iPhone photos), AVIF, BMP, TIFF, GIF

| | Operation | What you get | CLI id |
|---|---|---|---|
| Convert | → JPG · PNG · WebP · AVIF | same picture, new format (EXIF rotation applied, transparency on white for JPG) | `to-jpg` `to-png` `to-webp` `to-avif` |
| Convert | → PDF | a one-page PDF | `to-pdf` |
| Tool | Compress | JPG/WebP at quality 80 → `photo (сжато).jpg` | `img-compress` |
| Tool | Downscale ×2 | half the width and height → `photo (50%).jpg` | `img-half` |
| Tool | Fit to 1920 px | longest side ≤ 1920 px, small pictures are left as is → `photo (1920).jpg` | `img-1920` |
| Tool | Strip metadata | removes EXIF, GPS location, camera info → `photo (без метаданных).jpg` | `img-strip` |
| Tool | One PDF (2+ images) | every image becomes a page → `photo (все).pdf` | `images-to-pdf` |

</details>

<details>
<summary><b>🎬 Video</b> — MP4, MOV, MKV, WebM, AVI</summary>

<br>

**Input:** MP4 / M4V, MOV, MKV, WebM, AVI

| | Operation | What you get | CLI id |
|---|---|---|---|
| Convert | → MP4 | H.264 + AAC, plays everywhere, fast start in browsers | `to-mp4` |
| Convert | → WebM | VP9 + Opus | `to-webm` |
| Convert | → GIF | 15 fps, up to 640 px wide, optimised palette | `to-gif` |
| Convert | → MP3 | just the soundtrack | `video-to-mp3` |
| Tool | Compress | H.264 at CRF 28 → `clip (сжато).mp4` | `video-compress` |
| Tool | 720p | short side ≤ 720 px, vertical videos stay vertical → `clip (720p).mp4` | `video-720p` |
| Tool | Mute | removes the audio without re-encoding → `clip (без звука).mp4` | `video-mute` |

</details>

<details>
<summary><b>🎵 Audio</b> — MP3, M4A, WAV, FLAC, OGG, Opus</summary>

<br>

**Input:** MP3, M4A, WAV, FLAC, OGG, Opus

| | Operation | What you get | CLI id |
|---|---|---|---|
| Convert | → MP3 · M4A · WAV · FLAC · OGG | MP3 at 192 kbps, the rest with sensible defaults | `to-mp3` `to-m4a` `to-wav` `to-flac` `to-ogg` |
| Tool | Normalize loudness | EBU R128 loudness normalisation (−16 LUFS), same format → `song (норм).flac` | `audio-normalize` |

</details>

<details>
<summary><b>📄 PDF</b> — merge, split, pages to images, optimize</summary>

<br>

| | Operation | What you get | CLI id |
|---|---|---|---|
| Convert | → PNG / JPG (pages) | every page as an image (150 dpi) in a folder `report (страницы)` | `pdf-to-png` `pdf-to-jpg` |
| Tool | Merge (2+ PDFs) | one PDF, in the order the files came → `report (объединено).pdf` | `pdf-merge` |
| Tool | Split | one PDF per page in a folder `report (по страницам)` | `pdf-split` |
| Tool | Optimize | lossless: object streams + maximum Flate compression → `report (оптим.).pdf` | `pdf-optimize` |

</details>

<details>
<summary><b>🗜️ Archives</b> — ZIP pack and extract</summary>

<br>

| | Operation | What you get | CLI id |
|---|---|---|---|
| Tool | Pack to ZIP (any files) | one file → `photo.zip`; several files → one archive named after their folder | `zip-pack` |
| Tool | Extract ZIP | into a folder named after the archive; Cyrillic names from old Windows archivers are read correctly; archives with paths outside the folder (Zip Slip) are refused | `zip-extract` |

</details>

<details>
<summary><b>🚧 In progress</b> — Office documents, OCR, 7-Zip, icons, RAW photos</summary>

<br>

- **Office documents → PDF** — DOC/DOCX/RTF/ODT, XLS/XLSX/ODS/CSV, PPT/PPTX/ODP. Through your installed Microsoft Office (kept warm in the background so it's fast), or built-in libraries and Edge printing when Office isn't installed.
- **Text & web pages → PDF** — TXT, Markdown, HTML.
- **OCR** — get the text out of scans and photos.
- **7-Zip archives** — 7z, RAR and friends.
- **Icons** — make an `.ico` from a picture.
- **RAW photos** — straight from your camera to JPG.

</details>

## Install

1. Download **`DropWheel-win-x64.zip`** from the [latest build](https://github.com/Luchano55/dropwheel/releases/tag/dev-build).
2. Unzip it anywhere, for example to `C:\DropWheel`. Keep the `tools` folder next to `DropWheel.exe` — that's where the converters live.
3. Run **`DropWheel.exe`**. A ring icon appears in the tray (it may hide under the <kbd>^</kbd> arrow next to the clock). That's all — drag a file and press <kbd>Space</kbd>.

After that DropWheel keeps itself up to date: when a new build is out, the tray menu shows **«Обновить до сборки N»** (Update to build N) — one click, and it downloads, verifies and restarts itself.

> [!NOTE]
> **“Windows protected your PC”?** DropWheel is a personal project and the build isn't code-signed, so Microsoft Defender SmartScreen warns about it. Click **More info → Run anyway**. Every build is made by [GitHub Actions](https://github.com/Luchano55/dropwheel/actions/workflows/build.yml) straight from the source code in this repository.

**Requirements:** Windows 10 (1809 or newer) or Windows 11, 64-bit. Nothing else — .NET is built in.

**Uninstall:** right-click the tray icon → untick «Пункт „Конвертировать…“ в проводнике» (removes the Explorer menu entry) → «Выход» (Exit). Then delete the folder and, if you like, `%AppData%\DropWheel` (settings and logs).

## Usage

### 🎡 The wheel: drag + <kbd>Space</kbd>

| You do | DropWheel does |
|---|---|
| Drag a file and press <kbd>Space</kbd> | shows the wheel under the cursor |
| Press <kbd>Space</kbd> again | switches **Convert** ↔ **Tools** |
| Hover a sector | highlights it and shows the format in the centre |
| Drop on a sector | converts; the result appears next to the original |
| Drop in the centre, press <kbd>Esc</kbd> or release outside | cancels, nothing happens |

<kbd>Space</kbd> is only intercepted while you are dragging something — the rest of the time it types spaces and pauses videos as usual.

### 🖱️ Explorer context menu

Right-click one or more files → **«Конвертировать…»** → pick an operation in the menu that opens at the cursor. On Windows 11 the item is under **Show more options** (or press <kbd>Shift</kbd>+<kbd>F10</kbd>). If DropWheel isn't running, it starts automatically.

### 🔔 Tray

- **Left click** — the job list: progress bars and a *Cancel* button.
- **Right click** — jobs, the Explorer menu on/off switch, «Открыть журнал» (open the log folder), «Проверить обновления» (check for updates), exit.
- The icon turns blue with a counter while something is being converted.

### ⌨️ Command line

The archive also contains `dropwheel-cli.exe` for scripts and testing:

```bat
dropwheel-cli.exe list "C:\Мои фото\тест 1.heic"
dropwheel-cli.exe convert "C:\Мои фото\тест 1.heic" to-jpg
```

Or simply drag a file onto `try-dropwheel.cmd`: it lists the available operations and runs the one you type.

## Privacy

- **100% offline.** Every conversion happens on your PC. Your files are never uploaded anywhere — there is no server to upload them to.
- **No telemetry, no analytics, no account, no ads.**
- **The only thing DropWheel does online** is the update check: once a day (and when you click «Проверить обновления») it reads a small `version.json` from this repository's [releases](https://github.com/Luchano55/dropwheel/releases/tag/dev-build). It sends no files and no personal data, and an update is only installed when you click it — the download is verified with SHA-256. Don't want even that? Set `"checkForUpdates": false` in `%AppData%\DropWheel\settings.json`.
- Settings and logs stay on your computer in `%AppData%\DropWheel`. Logs contain file paths and error messages — have a quick look before attaching one to a bug report.

## FAQ

<details>
<summary><b>Is it really free?</b></summary>

Yes. DropWheel is MIT-licensed open source: no ads, no trial, no “pro” version, no limits.
</details>

<details>
<summary><b>Will it change or delete my original file?</b></summary>

Never. The result is written to a temporary file first and only then renamed, so a failed conversion leaves nothing half-done behind. If the name is taken, you get `photo (1).jpg`, `photo (2).jpg`, …
</details>

<details>
<summary><b>Where does the converted file go? What are the tags like “(сжато)”?</b></summary>

Right next to the original: `photo.heic` → `photo.jpg`. Tools keep the format, so they add a short tag (in Russian, for now) to the name: `photo (сжато).jpg` = compressed, `clip (720p).mp4`, `song (норм).flac` = normalised, `clip (без звука).mp4` = muted.
</details>

<details>
<summary><b>Is the interface available in English?</b></summary>

Not yet — menus and notifications are in Russian, since this started as a personal project. Format names (JPG, MP4, PDF…) are the same in every language, so the wheel is easy to use anyway. An English interface is on the <a href="#roadmap">roadmap</a>.
</details>

<details>
<summary><b>The wheel doesn't appear when I press Space.</b></summary>

- Make sure DropWheel is running (ring icon in the tray).
- Press <kbd>Space</kbd> <i>while</i> you are still holding the mouse button and dragging the file.
- Windows doesn't let normal apps interact with windows that run as administrator. If you drag from an elevated app, the wheel won't work there.
- Files dragged from a browser are not always real files on disk yet — save them first.
</details>

<details>
<summary><b>Space is busy in my workflow. Can I use another key?</b></summary>

Yes. Exit DropWheel, open `%AppData%\DropWheel\settings.json` and set `"hotkey"` to `"Backquote"` (the <kbd>`</kbd> / <kbd>Ё</kbd> key), `"CapsLock"` or `"F1"`…`"F12"`, then start DropWheel again. A settings window is coming.
</details>

<details>
<summary><b>Does it start with Windows?</b></summary>

Not yet — autostart arrives with the settings window. Until then press <kbd>Win</kbd>+<kbd>R</kbd>, type `shell:startup` and put a shortcut to `DropWheel.exe` there.
</details>

<details>
<summary><b>How do I update? Which build do I have?</b></summary>

DropWheel checks for a new build once a day and offers «Обновить до сборки N» in the tray menu. Right-click the tray icon → «Проверить обновления» to check right now — it also tells you your current build number. You can always download the full archive again from <a href="https://github.com/Luchano55/dropwheel/releases/tag/dev-build">Releases</a> and unzip it over the old folder.
</details>

<details>
<summary><b>Why is the download so big?</b></summary>

It bundles ffmpeg, ImageMagick, qpdf and poppler so that everything works offline right after unzipping — no extra downloads, no PATH tweaking. Those tools are most of the size.
</details>

<details>
<summary><b>Something went wrong. How do I report it?</b></summary>

Right-click the tray icon → «Открыть журнал» (open log), take the newest `.log` file and attach it to a <a href="https://github.com/Luchano55/dropwheel/issues/new/choose">bug report</a> together with your Windows version and DropWheel build number.
</details>

## Roadmap

- [x] Conversion core: images, video, audio — ffmpeg + ImageMagick, background queue, safe file naming
- [x] Tray app, notifications, «Конвертировать…» in the Explorer menu
- [x] The wheel: drag + <kbd>Space</kbd>, Convert/Tools modes, multi-monitor and high-DPI aware
- [x] PDF: merge, split, pages → images, lossless optimize
- [x] ZIP: pack and extract
- [x] Self-update from GitHub Releases (verified with SHA-256)
- [ ] Office documents → PDF (via installed Office, or built-in libraries / Edge printing) — *in progress*
- [ ] Text, Markdown, HTML → PDF — *in progress*
- [ ] OCR — *in progress*
- [ ] 7-Zip archives (7z, RAR, …) — *in progress*
- [ ] Icons (ICO) — *in progress*
- [ ] RAW photos — *in progress*
- [ ] Settings window: hotkey, output folder, quality, autostart
- [ ] English interface
- [ ] Whole-folder batch processing
- [ ] Video trimming and cropping

Have an idea? [Open a feature request](https://github.com/Luchano55/dropwheel/issues/new/choose).

## Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/Luchano55/dropwheel.git
cd dropwheel
dotnet build
dotnet test
```

- The core and the CLI (`src/DropWheel.Core`, `src/DropWheel.Cli`) are cross-platform `net8.0` and are fully tested on Linux too. The tray app `src/DropWheel` is WPF (`net8.0-windows`): it builds anywhere thanks to `EnableWindowsTargeting`, but runs only on Windows.
- Integration tests need the real tools: `ffmpeg`, ImageMagick (`magick` or `convert`), `qpdf` and `pdftoppm` (poppler-utils) in `PATH`, or the variables `DROPWHEEL_FFMPEG`, `DROPWHEEL_MAGICK`, `DROPWHEEL_QPDF`, `DROPWHEEL_PDFTOPPM`. Without them those tests are skipped.
- Windows builds of the tools: `./build/fetch-tools.ps1 -Destination tools` (PowerShell).
- Every push runs [`.github/workflows/build.yml`](.github/workflows/build.yml): tests on Linux and Windows, wheel screenshots, then a self-contained single-file build zipped together with `tools/` and published to the [`dev-build`](https://github.com/Luchano55/dropwheel/releases/tag/dev-build) release.

```
src/DropWheel.Core/   conversion catalog, engines, job queue, settings, IPC, wheel geometry (no UI)
src/DropWheel.Cli/    dropwheel-cli.exe
src/DropWheel/        the WPF tray app: hooks, wheel window, Explorer menu, notifications
tests/                xUnit tests (+ Windows-only UI tests that render wheel screenshots)
build/                fetch-tools.ps1 — downloads ffmpeg, ImageMagick, qpdf, poppler for Windows
docs/                 spec, design decisions, manual test checklist
```

**Adding a format is one line:** operations are declared in [`OperationCatalog.cs`](src/DropWheel.Core/Conversions/OperationCatalog.cs) — id, label, input type, engine, argument template, output extension. Issues and pull requests are welcome!

## Credits

DropWheel is a thin, friendly layer over excellent open-source tools. The release archive bundles:

| Tool | Used for | License |
|---|---|---|
| [**FFmpeg**](https://ffmpeg.org) — GPL build by [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) | video, audio, GIF | GPL v3 (source code at BtbN/FFmpeg-Builds and ffmpeg.org); license text in `tools/ffmpeg/LICENSE.txt` |
| [**ImageMagick**](https://imagemagick.org) — portable build from imagemagick.org | images, HEIC, AVIF, PDF from images | ImageMagick License (Apache-2.0 compatible) |
| [**qpdf**](https://github.com/qpdf/qpdf) — official build | PDF merge, split, optimize | Apache-2.0 |
| [**poppler**](https://poppler.freedesktop.org) (`pdftoppm`) — build by [oschwartz10612/poppler-windows](https://github.com/oschwartz10612/poppler-windows) | PDF pages → images | GPL-2.0 or later (source code at poppler.freedesktop.org) |

DropWheel runs them as separate programs and doesn't modify them. Planned additions such as [7-Zip](https://www.7-zip.org) keep their own licenses and will be listed here when they ship.

Built with [.NET 8](https://dotnet.microsoft.com) and WPF; notifications via [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit) (MIT). Artwork lettering is set in [Inter](https://rsms.me/inter/) (SIL OFL 1.1).

## License

**MIT** — see [`LICENSE`](LICENSE). The bundled tools keep their own licenses (see [Credits](#credits)).

<p align="center"><sub>Made with ☕ by <a href="https://github.com/Luchano55">Roman</a>. If DropWheel saves you a trip to an online converter, consider giving it a ⭐</sub></p>
