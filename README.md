# DropWheel

Офлайн-конвертер файлов для Windows: тащишь файл, жмёшь клавишу — под курсором колесо форматов, бросаешь на нужный — рядом появляется готовый файл. Всё локально, без интернета.

Сейчас готов **этап 1** — сама конвертация (без окон и колеса), её можно попробовать из консоли.

## Как попробовать на Windows

1. Откройте страницу **Releases** репозитория → релиз **«Тестовая сборка»** (`dev-build`) → скачайте `DropWheel-win-x64.zip`.
2. Распакуйте архив куда угодно, например в `C:\DropWheel`. Внутри: `dropwheel-cli.exe`, папка `tools` (ffmpeg, ImageMagick, qpdf, poppler, 7-Zip) и `try-dropwheel.cmd`.
3. **Самый простой способ:** перетащите любой файл (фото, видео, музыку) мышкой на `try-dropwheel.cmd`. Откроется чёрное окно со списком операций — введите нужную (например `to-jpg`) и нажмите Enter. Готовый файл появится рядом с исходным.
4. Если Windows покажет «Windows защитила ваш компьютер» — нажмите «Подробнее» → «Выполнить в любом случае» (программа не подписана, это нормально для личного проекта).

Консольный вариант: `dropwheel-cli.exe convert "C:\Мои фото\тест 1.heic" to-jpg`, список операций — `dropwheel-cli.exe list`.

## Что умеет

| Тип | Конвертация | Инструменты |
|---|---|---|
| Фото (JPG, PNG, WebP, HEIC, AVIF, BMP, TIFF, GIF) | `to-jpg` `to-png` `to-webp` `to-avif` `to-pdf` | `img-compress` сжать · `img-half` уменьшить ×2 · `img-1920` до 1920 px · `img-strip` убрать метаданные · `images-to-pdf` несколько фото в один PDF |
| Видео (MP4, MOV, MKV, WebM, AVI) | `to-mp4` `to-webm` `to-gif` `video-to-mp3` | `video-compress` сжать · `video-720p` · `video-mute` убрать звук |
| Звук (MP3, M4A, WAV, FLAC, OGG, Opus) | `to-mp3` `to-m4a` `to-wav` `to-flac` `to-ogg` | `audio-normalize` выровнять громкость |
| PDF | `pdf-to-png` `pdf-to-jpg` — каждая страница картинкой, в папку `документ (страницы)` · `pdf-to-txt` текст (у сканов текста нет — будет понятная ошибка) | `pdf-merge` склеить несколько PDF · `pdf-split` разбить по страницам · `pdf-optimize` оптимизировать без потери качества · `pdf-rotate` повернуть все страницы на 90° по часовой |
| Любые файлы, ZIP | — | `zip-pack` упаковать в ZIP (несколько файлов — в один архив) · `pack-7z` то же в 7Z · `zip-extract` распаковать ZIP в папку |
| Архивы (7Z, RAR, TAR, GZ, TGZ, BZ2, XZ) | — | `archive-extract` распаковать в папку с именем архива (`.tar.gz` — сразу файлы); архивы с паролем не поддерживаются |

Исходный файл никогда не меняется и не удаляется. Если имя занято — `photo (1).jpg`. Инструменты добавляют пометку: `photo (сжато).jpg`.

## Для разработки

- .NET 8 SDK; для интеграционных тестов — `ffmpeg` и ImageMagick в PATH (или переменные `DROPWHEEL_FFMPEG` / `DROPWHEEL_MAGICK`); для PDF — `qpdf`, `pdftoppm` и `pdftotext` (poppler-utils; `DROPWHEEL_QPDF` / `DROPWHEEL_PDFTOPPM` / `DROPWHEEL_PDFTOTEXT`); для архивов — 7-Zip (`7z`, пакет `7zip`; `DROPWHEEL_7Z`).
- `dotnet test` — все тесты; без утилит интеграционные тесты пропускаются.
- Сборка для Windows делается автоматически (GitHub Actions, `.github/workflows/build.yml`): тесты на Linux и Windows, затем архив с `tools/` выкладывается в релиз `dev-build`. Утилиты скачивает `build/fetch-tools.ps1`.
- Документы: `docs/spec.md` (ТЗ), `docs/decisions.md`, `docs/questions.md`, `CLAUDE.md`.

## Лицензии сторонних программ

Архив сборки содержит:
- **ffmpeg** — GPL-сборка [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) (лицензия GPL v3, исходный код — там же и на [ffmpeg.org](https://ffmpeg.org)); текст лицензии — `tools/ffmpeg/LICENSE.txt`.
- **ImageMagick** — portable-сборка с [imagemagick.org](https://imagemagick.org) (лицензия ImageMagick, Apache-2.0-совместимая).
- **qpdf** — официальная сборка [qpdf/qpdf](https://github.com/qpdf/qpdf) (лицензия Apache-2.0).
- **poppler** (`pdftoppm`, `pdftotext`) — сборка [oschwartz10612/poppler-windows](https://github.com/oschwartz10612/poppler-windows) (лицензия GPL-2.0 или новее, исходный код — на [poppler.freedesktop.org](https://poppler.freedesktop.org)).
- **7-Zip** (`7z.exe`, `7z.dll`) — официальная сборка Игоря Павлова ([7-zip.org](https://www.7-zip.org), релизы [ip7z/7zip](https://github.com/ip7z/7zip)): лицензия GNU LGPL-2.1 или новее, часть кода — BSD-3-Clause; код распаковки RAR — под лицензией unRAR с ограничением: его нельзя использовать, чтобы воссоздать алгоритм сжатия RAR (DropWheel RAR только распаковывает). Текст лицензии — `tools/7zip/License.txt`, исходный код — на 7-zip.org.

DropWheel запускает их как отдельные программы и не изменяет.
