# Что нового в DropWheel / Changelog

Раздел каждой версии начинается со строки `## <версия>` — CI берёт его текст в описание релиза
`v<версия>` (`.github/workflows/build.yml`). Сначала по-русски, затем коротко по-английски.

Each version section starts with `## <version>`; CI copies it into the GitHub release notes.

## 0.4.0

**Новое в этой версии**
- Папки целиком: правый клик по папке → «Конвертировать…» (или перетащите папку и нажмите Пробел) — колесо для файлов внутри.
  Результат — в папке рядом: `Отпуск` → `Отпуск (JPG)`, с теми же подпапками; файлы, которые уже в нужном формате, просто копируются,
  неподходящие пропускаются. «Один PDF» и «Склеить» дают один файл по имени папки. Одно уведомление на всю папку. До 1000 файлов за раз.
- Картинки из браузера: тащите картинку с сайта (Chrome, Edge, Firefox), нажмите Пробел — колесо форматов,
  как для файла. Картинка сохраняется в ту же папку, что и картинки из буфера (по умолчанию «Изображения\DropWheel»),
  с именем с сайта, результат — рядом. Формат определяется по содержимому (WebP под именем .jpg — это WebP).
  Ничего не скачивается: если браузер дал только ссылку — подсказка. При перетаскивании текста колесо больше не висит.

**English**
- Whole folders: right-click a folder → “Convert…” (or drag a folder and press Space) to get the wheel for the files inside.
  Results go to a sibling folder — `Vacation` → `Vacation (JPG)` — with the same subfolders; files already in the target format are copied,
  unsuitable ones are skipped; “One PDF” and “Merge” make a single file named after the folder. One notification per folder, up to 1000 files.
- Pictures from the browser: drag an image from a web page (Chrome, Edge, Firefox) and press Space to get the format wheel;
  the picture is saved to the clipboard pictures folder and converted there. Fully offline: a bare link shows a hint instead of downloading.

## 0.3.0

Первая стабильная версия — с установщиком, номером версии и каналами обновлений.

**Установка и обновления**
- Установщик `DropWheel-Setup-0.3.0.exe`: без прав администратора, только для текущего пользователя,
  в `%LocalAppData%\Programs\DropWheel`. Ярлык в «Пуске», по желанию — на рабочем столе и автозапуск.
  Удаление через «Параметры → Приложения» убирает пункт меню проводника и автозапуск; настройки остаются.
- Два канала обновлений: **стабильный** (проверенные версии, как эта) и **тестовый** (каждая сборка из `main`).
  Выбор — «Настройки → Основное → Канал обновлений». Самообновление из трея работает и в установленной версии.
- Скоро — установка через `winget install Luchano55.DropWheel`.

**Новое в этой версии**
- Колесо для буфера обмена: Ctrl+Alt+V (или пункт трея «Из буфера обмена…») — для скопированных файлов
  или снимка экрана. В настройках — папка для картинок из буфера и «Оставлять исходную картинку».
- Видео: «Повернуть ↻», «Кадры» (кадр в секунду в папку), «JPG (кадр)» — обложка.
- Прозрачность колеса — ползунок в настройках.

**Что умеет (всё, что сделано до 0.3.0)**
- Колесо форматов под курсором: тащите файл, нажмите Пробел — бросьте на сектор, рядом появится результат.
  Два режима: «Конвертация» и «Инструменты». Работает в проводнике и на рабочем столе, на нескольких мониторах и при любом масштабе.
- «Конвертировать…» в меню проводника — то же колесо щелчком, для одного файла или целого выделения.
- Фото: HEIC, AVIF, WebP, JPG, PNG, ICO, PDF; SVG/PSD/RAW на входе; сжатие, уменьшение, «до 1920 px», без метаданных, несколько картинок → один PDF.
- Видео и звук: MP4, WebM, GIF, MP3/M4A/WAV/FLAC/OGG; сжатие, 720p, без звука, субтитры в SRT, GIF → видео, выравнивание громкости.
- PDF: склеить, разбить, оптимизировать, страницы в PNG/JPG, PDF → TXT/DOCX, поворот; картинки → один PDF.
- Документы: Word, Excel, PowerPoint через установленный Office; без Office — DOCX → TXT/HTML/Markdown,
  Excel ↔ CSV, Markdown → HTML, печать в PDF через Edge.
- Архивы: ZIP (упаковать/распаковать), 7Z, RAR, TAR, GZ и другие через 7-Zip.
- Распознавание текста (OCR) средствами Windows, «В UTF-8» для текстов.
- Окно задач в стиле Windows 11, уведомления с превью; окно настроек: клавиша, папка результата, качество,
  автозапуск, своё колесо (порядок и видимость форматов, «последний выбор — первым»).
- Интерфейс на русском и английском (по языку Windows или по выбору в настройках).
- Всё офлайн: ffmpeg, ImageMagick, qpdf, poppler и 7-Zip — внутри. Исходный файл никогда не меняется,
  занятое имя → `photo (1).jpg`. Кириллица и пробелы в путях — без проблем.

**English**
- First stable release: a per-user installer (no admin rights, `%LocalAppData%\Programs\DropWheel`, Start menu
  shortcut, optional desktop shortcut and autostart; uninstall removes the Explorer menu entry and autostart, keeps settings).
- Clipboard wheel (Ctrl+Alt+V) for copied files or a screenshot, with a folder setting and an option to drop the intermediate PNG; video rotate, frames and poster; wheel transparency slider.
- Update channels: *Stable* (tagged releases) and *Test builds* (every build from `main`), switchable in Settings.
  Self-update keeps working in the installed version. winget package `Luchano55.DropWheel` is coming soon.
- Everything built so far: the drag-and-Space format wheel, the Explorer “Convert…” menu, photos (HEIC/AVIF/WebP…),
  video/audio/GIF, PDF tools, Office documents (with or without Office), archives via 7-Zip, OCR, jobs flyout,
  notifications, settings window with a customizable wheel, English and Russian UI. Fully offline, originals are never changed.
