# Что нового в DropWheel / Changelog

Раздел каждой версии начинается со строки `## <версия>` — CI берёт его текст в описание релиза
`v<версия>` (`.github/workflows/build.yml`). Сначала по-русски, затем коротко по-английски.

Each version section starts with `## <version>`; CI copies it into the GitHub release notes.

## 0.3.0

Первая стабильная версия — с установщиком, номером версии и каналами обновлений.

**Установка и обновления**
- Установщик `DropWheel-Setup-0.3.0.exe`: без прав администратора, только для текущего пользователя,
  в `%LocalAppData%\Programs\DropWheel`. Ярлык в «Пуске», по желанию — на рабочем столе и автозапуск.
  Удаление через «Параметры → Приложения» убирает пункт меню проводника и автозапуск; настройки остаются.
- Два канала обновлений: **стабильный** (проверенные версии, как эта) и **тестовый** (каждая сборка из `main`).
  Выбор — «Настройки → Основное → Канал обновлений». Самообновление из трея работает и в установленной версии.
- Скоро — установка через `winget install Luchano55.DropWheel`.

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
- Update channels: *Stable* (tagged releases) and *Test builds* (every build from `main`), switchable in Settings.
  Self-update keeps working in the installed version. winget package `Luchano55.DropWheel` is coming soon.
- Everything built so far: the drag-and-Space format wheel, the Explorer “Convert…” menu, photos (HEIC/AVIF/WebP…),
  video/audio/GIF, PDF tools, Office documents (with or without Office), archives via 7-Zip, OCR, jobs flyout,
  notifications, settings window with a customizable wheel, English and Russian UI. Fully offline, originals are never changed.
