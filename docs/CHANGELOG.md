# Что нового в DropWheel / Changelog

Раздел каждой версии начинается со строки `## <версия>` — CI берёт его текст в описание релиза
`v<версия>` (`.github/workflows/build.yml`). Сначала по-русски, затем коротко по-английски.

Each version section starts with `## <version>`; CI copies it into the GitHub release notes.

## 1.2.0

**Новое в этой версии**
- Оформить картинку: «Инструменты» → «Оформить…» — окно с живым предпросмотром: фон (три мягких градиента, белый, серый,
  тёмный или прозрачный), отступ, скругление углов и тень. Результат — `фото (оформлено).png` рядом с исходником;
  кнопка «Копировать» кладёт картинку сразу в буфер обмена, ничего не сохраняя. Удобно для снимков экрана.
  В командной строке: `img-beautify --style "bg=sunset;pad=8;radius=12;shadow=1"`.
- Настройка «Копировать результат в буфер обмена» («Основное», по умолчанию выключена): готовая картинка попадает
  в буфер картинкой (Ctrl+V в мессенджере или редакторе) и файлом, остальное — файлом (Ctrl+V в проводнике);
  в уведомлении — «Скопировано в буфер обмена».
- «Что нового»: после обновления программа сама показывает, что изменилось, — небольшое окно со списком изменений
  и ссылкой на все выпуски. Открыть ещё раз — пункт «Что нового» в меню значка.
- GIF из куска видео: в окне «Обрезать…» выберите «Результат: GIF» — выбранный кусок станет анимированным GIF
  (15 кадров в секунду, до 640 точек в ширину, без звука) — `видео (кусок).gif`. Если кусок длиннее 30 секунд,
  окно подскажет, что GIF выйдет большим.
- Пароль на PDF: «Инструменты» → «Защитить…» — PDF с паролем на открытие (шифрование AES-256), `документ (защищён).pdf`;
  «Снять пароль…» — копия без пароля, `документ (без пароля).pdf`. Неверный пароль — понятная ошибка. Пароль нигде
  не сохраняется и не пишется в журнал. Исходный файл не меняется.
- Снимок экрана: Ctrl+Alt+S или «Снимок экрана» в меню значка. Выделите область мышью,
  щёлкните по окну или нажмите Enter — весь монитор; работает с несколькими мониторами и любым масштабом. Затем
  «Копировать», «Сохранить» (PNG в «Изображения\DropWheel»), «Колесо» (сразу конвертировать или обрезать) или «Текст»
  (распознать текст и положить в буфер). Сочетание выключается в настройках.

**English**
- Beautify a picture: Tools → Beautify… opens a window with a live preview — background (three soft gradients, white,
  grey, dark or transparent), padding, rounded corners and a shadow. The result is `photo (styled).png` next to the
  original; *Copy* puts the picture straight into the clipboard without saving. CLI: `img-beautify --style "…"`.
- New setting *Copy the result to the clipboard* (General, off by default): a finished picture goes to the clipboard as
  a picture and a file, anything else as a file.
- What's new: after an update DropWheel shows a small window listing the changes, with a link to all releases.
  Open it again from the tray menu → What's new.
- GIF from a video clip: in the Trim window choose “Result: GIF” — the selected part becomes an animated GIF
  (15 fps, up to 640 px wide, no sound), `video (clip).gif`. Clips over 30 seconds get a “the GIF will be large” hint.
- PDF password: Tools → “Protect…” adds an open password (AES-256), `document (protected).pdf`; “Remove password…”
  makes an unlocked copy, `document (unlocked).pdf`. A wrong password gives a clear error. The password is never saved
  or written to the log. The original file is never changed.
- Screenshots: Ctrl+Alt+S or *Screenshot* in the tray menu. Drag to select an area,
  click a window or press Enter for the whole monitor — multiple monitors and any scaling. Then *Copy*, *Save*
  (PNG in `Pictures\DropWheel`), *Wheel* (convert or crop right away) or *Text* (recognise the text and copy it).
  The shortcut can be turned off in the settings.

## 1.1.1

**Исправлено**
- Word, Excel, PowerPoint: документы, пришедшие по почте или скачанные из интернета, больше не падают с ошибкой
  «Word не смог открыть файл» (Office открывал их в защищённом просмотре) — программа сама берёт чистую копию.
- Если Office установлен, но не работает (не активирован, висит окно первого запуска), Word → PDF, TXT, HTML, Markdown
  и Excel ↔ CSV делаются без Office (PDF — печатью через встроенный в Windows Edge), а не падают с ошибкой.
- DOC (старый формат Word) по умолчанию убран с колеса — включается в «Настройки → Колесо».
- В играх и полноэкранных приложениях (окно без рамки на весь экран) колесо больше не появляется от зажатой мыши + Пробела.

**English**
- Fixed: Office documents received by e-mail or downloaded from the internet no longer fail with “Word could not open
  the file” (Office opened them in Protected View) — DropWheel retries with a clean copy.
- If Office is installed but not working (not activated, first-run dialog), conversions that also work without Office
  (Word → PDF via the built-in Edge, TXT, HTML, Markdown; Excel ↔ CSV) now fall back to that instead of failing.
- DOC (the legacy Word format) is hidden on the wheel by default — show it in Settings → Wheel.
- The wheel no longer pops up in games and other full-screen apps (borderless full-screen windows) on mouse hold + Space.

## 1.1.0

**Новое в этой версии**
- Обрезка картинки: «Инструменты» → «Обрезать…» — окно с картинкой и рамкой: тяните углы и края, рамку — за середину,
  вне рамки — затемнено, под картинкой — размер в пикселях. Пропорции: «Свободно», «1:1», «4:3», «16:9», «Как было».
  Результат — `фото (обрезано).jpg` рядом, формат тот же (HEIC, RAW и подобные — в JPG), прозрачность PNG сохраняется,
  фото с телефона не ложится на бок. По одной картинке. В командной строке: `img-crop --crop 800x600+100+50`.
- Своя горячая клавиша: «Основное» → «Горячая клавиша» → «Своя…» → нажмите любую клавишу (Tab, Num 5, F13, буква…).
  Ctrl, Shift, Alt, Win, Esc, Enter и Print Screen не принимаются — окно объясняет почему.
- PDF → Markdown: с Word — заголовки, списки, жирный и таблицы (Word перестраивает PDF, как для DOCX — это небыстро); «MD (быстро)» — мгновенно, но только текст, работает и без Word.
- Исправлено: самообновление больше не останавливается с ошибкой «Access to the path …DropWheel.exe.old is denied».

**English**
- Picture cropping: Tools → Crop… opens a window with a crop frame — drag the corners and edges, drag inside to move;
  the outside is dimmed and the size is shown in pixels. Aspect presets: Free, 1:1, 4:3, 16:9, Original.
  The result is `photo (cropped).jpg` next to the original, same format, transparency kept, phone photos stay upright.
  One picture at a time; CLI: `img-crop --crop 800x600+100+50`.
- Custom hotkey: General → Hotkey → Custom… → press any key. Ctrl, Shift, Alt, Win, Esc, Enter and Print Screen are not accepted.
- PDF → Markdown: with Word — headings, lists, bold and tables (Word rebuilds the PDF, as for DOCX — not fast); “MD (fast)” — instant plain text, works without Word too.
- Fixed: self-update no longer stops with “Access to the path …DropWheel.exe.old is denied”.

## 1.0.0

Первый полноценный выпуск: всё из 0.3.0 плюс папки целиком, картинки из браузера и обрезка видео.

**Новое в этой версии**
- Папки целиком: правый клик по папке → «Конвертировать…» (или перетащите папку и нажмите Пробел) — колесо для файлов внутри.
  Результат — в папке рядом: `Отпуск` → `Отпуск (JPG)`, с теми же подпапками; файлы, которые уже в нужном формате, просто копируются,
  неподходящие пропускаются. «Один PDF» и «Склеить» дают один файл по имени папки. Одно уведомление на всю папку. До 1000 файлов за раз.
- Картинки из браузера: тащите картинку с сайта (Chrome, Edge, Firefox), нажмите Пробел — колесо форматов,
  как для файла. Картинка сохраняется в ту же папку, что и картинки из буфера (по умолчанию «Изображения\DropWheel»),
  с именем с сайта, результат — рядом. Формат определяется по содержимому (WebP под именем .jpg — это WebP).
  Ничего не скачивается: если браузер дал только ссылку — подсказка. При перетаскивании текста колесо больше не висит.
- Обрезка видео: «Инструменты» → «Обрезать…» — окно с кадром и полосой миниатюр, ручки «начало» и «конец».
  По умолчанию мгновенно и без потери качества (`видео (обрезано).mp4`, формат тот же); галочка
  «Точно по кадру» — ровно с выбранного кадра, с пересжатием. «В 7Z» для видео теперь девятый лепесток —
  по умолчанию не виден, его можно поднять в «Настройки → Колесо».

**English**
- First full release: everything from 0.3.0 plus whole folders, browser pictures and video trimming.
- Whole folders: right-click a folder → “Convert…” (or drag a folder and press Space) to get the wheel for the files inside.
  Results go to a sibling folder — `Vacation` → `Vacation (JPG)` — with the same subfolders; files already in the target format are copied,
  unsuitable ones are skipped; “One PDF” and “Merge” make a single file named after the folder. One notification per folder, up to 1000 files.
- Pictures from the browser: drag an image from a web page (Chrome, Edge, Firefox) and press Space to get the format wheel;
  the picture is saved to the clipboard pictures folder and converted there. Fully offline: a bare link shows a hint instead of downloading.
- Video trimming: Tools → “Trim…” opens a window with a frame preview, a thumbnail strip and start/end handles.
  Instant and lossless by default (same container); “Frame-accurate” re-encodes to start exactly on the chosen frame.

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
