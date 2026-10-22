namespace DropWheel.Localization;

/// <summary>
/// Все строки интерфейса (трей, колесо, окно задач, уведомления, настройки, CLI, причины ошибок).
/// Значения по умолчанию — русские (<see cref="Ru"/>), английские — в Texts.En.cs (<see cref="En"/>).
/// Шаблоны с {0}, {1} подставляются через <see cref="Loc.F"/>; у английской строки те же подстановки.
/// Новая строка: свойство здесь + перевод в Texts.En.cs (тест LocalizationTests проверяет, что перевод есть).
/// Журнал (logs) остаётся по-русски — его читают при разборе ошибок, а не пользователь.
/// </summary>
public sealed partial class Texts
{
    public static Texts Ru { get; } = new();

    // ---------- трей ----------
    public string TrayJobs { get; init; } = "Задачи";
    public string TraySettings { get; init; } = "Настройки…";
    public string TrayContextMenu { get; init; } = "Пункт «Конвертировать…» в проводнике";
    public string TrayOpenLog { get; init; } = "Открыть журнал";
    public string TrayCheckUpdates { get; init; } = "Проверить обновления";
    public string TrayFromClipboard { get; init; } = "Из буфера обмена…";
    public string TrayExit { get; init; } = "Выход";
    public string TrayUpdateTo { get; init; } = "Обновить до сборки {0}";
    public string TrayBusy { get; init; } = "DropWheel — в работе: {0}";
    public string TrayRunningTitle { get; init; } = "DropWheel работает";
    public string TrayStartHint { get; init; } =
        "Значок — здесь, в трее. Потащите файл и нажмите {0} — или правой кнопкой по файлу → «Конвертировать…».";

    // ---------- буфер обмена ----------
    public string ClipboardNothingTitle { get; init; } = "Буфер обмена";
    public string ClipboardNothing { get; init; } = "В буфере нет картинки или файлов";
    public string ClipboardFailed { get; init; } = "Не удалось прочитать буфер обмена";

    // ---------- обновления ----------
    public string UpdateAvailableTitle { get; init; } = "Есть новая версия DropWheel";
    public string UpdateAvailableText { get; init; } = "Правый клик по значку → «Обновить».";
    public string UpdatedTitle { get; init; } = "DropWheel обновлён";
    public string UpdatedText { get; init; } = "Сборка {0}. Всё готово к работе.";
    public string NoUpdatesTitle { get; init; } = "Обновлений нет";
    public string NoUpdatesText { get; init; } = "У вас последняя сборка ({0}).";
    public string UpdateCheckFailed { get; init; } = "Не удалось проверить обновления";
    public string UpdatingTitle { get; init; } = "Обновление";
    public string UpdatingText { get; init; } = "Скачиваю сборку {0}… Программа перезапустится сама.";
    public string UpdateFailed { get; init; } = "Обновление не удалось";
    public string UpdateDownloadingApp { get; init; } = "Скачиваю программу…";
    public string UpdateDownloadingTools { get; init; } = "Скачиваю утилиты…";
    public string UpdateWaitingJobs { get; init; } = "Жду окончания задач…";
    public string UpdateChecksumMismatch { get; init; } = "Контрольная сумма {0} не совпала — обновление отменено";

    // ---------- колесо ----------
    public string WheelNothing { get; init; } = "Нечего предложить";
    public string Cancel { get; init; } = "Отмена";
    public string ModeConvert { get; init; } = "Конвертация";
    public string ModeTools { get; init; } = "Инструменты";
    public string WheelEmptyClick { get; init; } = "Щёлкните, чтобы закрыть";
    public string WheelEmptyDrag { get; init; } = "Отпустите — ничего не произойдёт";
    /// <summary>Меню выбора без колеса (запасное): заголовок для нескольких файлов.</summary>
    public string ChooserFiles { get; init; } = "Файлов: {0}";

    // ---------- окно задач ----------
    public string JobsTitle { get; init; } = "DropWheel — задачи";
    public string JobsNone { get; init; } = "Задач нет";
    public string JobsActive { get; init; } = "в работе: {0}";
    public string JobsDone { get; init; } = "готово: {0}";
    public Noun Errors { get; init; } = new("ошибка", "ошибки", "ошибок");
    public string JobsAllCanceled { get; init; } = "Всё отменено";
    public string JobsClear { get; init; } = "Очистить";
    public string JobsClearHint { get; init; } = "Убрать завершённые из списка";
    public string JobsEmptyTitle { get; init; } = "Пока пусто";
    public string JobsEmptyHint { get; init; } =
        "Потащите файл и, не отпуская кнопку мыши, нажмите {0} — появится колесо форматов. " +
        "Или щёлкните по файлу правой кнопкой → «Конвертировать…».";
    public string JobQueued { get; init; } = "{0} · в очереди";
    public string JobRunning { get; init; } = "{0} · выполняется…";
    public string JobDone { get; init; } = "Готово";
    public string JobDoneResult { get; init; } = "Готово · {0}";
    public string JobFailed { get; init; } = "Не получилось";
    public string JobCanceled { get; init; } = "{0} · отменено";
    /// <summary>«photo.jpg и ещё 3» — имя задачи из нескольких файлов.</summary>
    public string AndMore { get; init; } = "{0} и ещё {1}";
    public string ActionOpen { get; init; } = "Открыть";
    public string ActionReveal { get; init; } = "Показать в папке";
    public string ActionDetails { get; init; } = "Подробнее";
    public string ActionDetailsHint { get; init; } = "Открыть журнал с подробностями ошибки";
    public string UnknownError { get; init; } = "Неизвестная ошибка";

    // ---------- уведомления ----------
    public string NotifyDone { get; init; } = "Готово: {0}";
    public string NotifyFailed { get; init; } = "Не получилось: {0}";
    public Noun Files { get; init; } = new("файл", "файла", "файлов");
    public string NotifyList { get; init; } = "{0}, {1} и ещё {2}";
    public string NotifyDetailsInLog { get; init; } = "Подробности — в журнале DropWheel";
    public string NotifyDifferentFolders { get; init; } = "В разных папках";
    public string NotifyOnDrive { get; init; } = "На диске {0}";
    public string NotifyInFolder { get; init; } = "В папке «{0}»";

    // ---------- меню проводника, клавиши ----------
    public string ContextMenuText { get; init; } = "Конвертировать…";
    public string KeySpace { get; init; } = "Пробел";
    public string KeyBackquote { get; init; } = "Ё";

    // ---------- окно настроек ----------
    public string SettingsTitle { get; init; } = "Настройки DropWheel";
    public string PageGeneral { get; init; } = "Основное";
    public string PageQuality { get; init; } = "Качество";
    public string PageWheel { get; init; } = "Колесо";
    public string HotkeyTitle { get; init; } = "Горячая клавиша";
    public string HotkeyHint { get; init; } =
        "Тащите файл и, не отпуская кнопку мыши, нажмите эту клавишу — появится колесо. Ещё раз — другой режим.";
    public string OutputTitle { get; init; } = "Куда класть результат";
    public string OutputBeside { get; init; } = "Рядом с исходником";
    public string OutputShared { get; init; } = "В одну папку";
    public string OutputBrowse { get; init; } = "Выбрать папку…";
    public string OutputBrowseTitle { get; init; } = "Папка для результатов DropWheel";
    public string LanguageTitle { get; init; } = "Язык интерфейса";
    public string LanguageHint { get; init; } = "«Как в Windows» — русский, если Windows на русском, иначе английский.";
    public string LanguageAuto { get; init; } = "Как в Windows";
    public string QualityIntro { get; init; } = "Действует на новые задачи сразу. Уже идущие доделываются с прежними настройками.";
    public string JpegHint { get; init; } = "Качество фото в JPG и PDF из картинок.";
    public string WebpHint { get; init; } = "Качество картинок WebP.";
    public string CrfTitle { get; init; } = "Видео (CRF)";
    public string CrfHint { get; init; } = "Меньше — лучше качество и больше файл. 23 — обычно незаметно глазу.";
    public string Mp3Hint { get; init; } = "Битрейт звука в MP3: больше — лучше и больше файл.";
    public string Kbps { get; init; } = "{0} кбит/с";
    public string AutoStart { get; init; } = "Запускать вместе с Windows";
    public string AutoStartHint { get; init; } = "DropWheel тихо появится в трее после входа в Windows.";
    public string ContextMenu { get; init; } = "Пункт «Конвертировать…» в проводнике";
    public string ContextMenuHint { get; init; } = "Правый клик по файлу → «Конвертировать…» — то же колесо, но щелчком.";
    public string ClipboardHotkey { get; init; } = "Ctrl+Alt+V — колесо для буфера обмена";
    public string ClipboardHotkeyHint { get; init; } =
        "Скопируйте файлы или снимок экрана и нажмите Ctrl+Alt+V — колесо появится у курсора.";
    public string WheelOpacityTitle { get; init; } = "Прозрачность колеса";
    public string WheelOpacityHint { get; init; } = "Насколько сквозь колесо видно окна под ним. Подписи остаются чёткими.";
    public string WheelOpacitySolid { get; init; } = "плотнее";
    public string WheelOpacityClear { get; init; } = "прозрачнее";
    public string Updates { get; init; } = "Проверять обновления";
    public string UpdatesHint { get; init; } = "Раз в сутки — единственное обращение программы в интернет.";
    public string WheelIntro { get; init; } =
        "Какие форматы показывать и в каком порядке. Первый в списке — верхний лепесток, дальше — по часовой стрелке.";
    public string MruFirst { get; init; } = "Последний выбор — первым";
    public string MruFirstHint { get; init; } = "Формат, выбранный в прошлый раз для этого типа файлов, встаёт наверх.";
    public string Reset { get; init; } = "Сбросить";
    public string ResetHint { get; init; } = "Вернуть порядок и видимость для этого типа как было";
    public string MoveUp { get; init; } = "Выше";
    public string MoveDown { get; init; } = "Ниже";
    public string ShowOnWheel { get; init; } = "Показывать на колесе";
    public string Preview { get; init; } = "Так будет выглядеть";
    public string OverLimit { get; init; } = "На колесе помещается 8 — этот не виден";
    public string NeedsOffice { get; init; } = "нужен Microsoft Office";
    public string OnlyFor { get; init; } = "только {0}";
    public string FromFiles { get; init; } = "от {0} файлов";
    public string NothingInMode { get; init; } = "В этом режиме для этого типа ничего нет.";
    public string OpenLogs { get; init; } = "Открыть папку журналов";
    public string Version { get; init; } = "DropWheel, сборка {0}";
    public string VersionDev { get; init; } = "DropWheel, сборка разработчика";
    public string GroupPhoto { get; init; } = "Фото";
    public string GroupVideo { get; init; } = "Видео";
    public string GroupAudio { get; init; } = "Звук";
    public string GroupText { get; init; } = "Текст";
    public string GroupArchive { get; init; } = "Архивы";
    public string GroupAny { get; init; } = "Любые файлы";

    // ---------- CLI ----------
    public string CliUsage { get; init; } = """
        Использование:
          dropwheel convert <файл> [<файл>...] <операция> [параметры]
          dropwheel list [<файл>...]        — какие операции доступны (для файлов — только подходящие)

        Параметры:
          --out-dir <папка>   класть результат в эту папку (по умолчанию — рядом с исходником)
          --ffmpeg <путь>     путь к ffmpeg
          --magick <путь>     путь к magick
          --lang ru|en        язык сообщений (по умолчанию — как в системе)
          -v, --verbose       печатать команды утилит

        Пример:
          dropwheel convert "Мои фото/тест 1.heic" to-jpg
        """;
    public string CliUnknownCommand { get; init; } = "Неизвестная команда: {0}";
    public string CliNeedFileAndOp { get; init; } = "Нужны файл и операция.";
    public string CliUnknownOperation { get; init; } = "Неизвестная операция: {0}";
    public string CliNeedValue { get; init; } = "После {0} нужно значение.";
    public string CliUnknownLanguage { get; init; } = "Неизвестный язык: {0} (можно ru или en)";
    public string CliDone { get; init; } = "Готово: {0}";
    public string CliError { get; init; } = "Ошибка: {0}: {1}";

    // ---------- имена в результатах ----------
    /// <summary>Файлы страниц PDF: «стр-1.png».</summary>
    public string PageFile { get; init; } = "стр";
    /// <summary>Кадры из видео: «кадр-001.jpg».</summary>
    public string FrameFile { get; init; } = "кадр";
    /// <summary>Картинка из буфера обмена: «Буфер 2026-10-22 10-31-05.png» ({0} — дата и время).</summary>
    public string ClipboardFile { get; init; } = "Буфер {0}";
    /// <summary>Файлы слайдов: «слайд-1.png».</summary>
    public string SlideFile { get; init; } = "слайд";
    /// <summary>Лист XLSX из CSV.</summary>
    public string SheetName { get; init; } = "Лист1";
    /// <summary>Картинка в Markdown из Word: «*[Рисунок: подпись]*».</summary>
    public string MdPicture { get; init; } = "Рисунок";

    // ---------- ошибки: конвертер и утилиты ----------
    public string ErrMinFiles { get; init; } = "Для «{0}» нужно файлов: не меньше {1}";
    public string ErrFileNotFound { get; init; } = "Файл не найден";
    public string ErrNotApplicable { get; init; } = "Операция «{0}» не подходит для этого файла";
    public string ErrToolMadeNothing { get; init; } = "Утилита ничего не создала";
    public string ErrToolMadeNoFile { get; init; } = "Утилита не создала файл";
    public string ErrNoOffice { get; init; } = "Не найден Microsoft Office";
    public string ErrNoEngine { get; init; } = "На этом компьютере нечем выполнить «{0}»";
    public string ErrUnexpected { get; init; } = "Непредвиденная ошибка";
    public string ErrToolMissing { get; init; } = "Не найден {0}. Положите {1} в папку tools рядом с программой.";
    public string ErrSevenZipMissing { get; init; } = "Не найден 7-Zip. Положите 7z.exe и 7z.dll в папку tools рядом с программой.";
    public string ErrCantStart { get; init; } = "Не удалось запустить {0}";
    /// <summary>«ZIP не умеет «Сжать»», «Word не умеет …».</summary>
    public string ErrCantDo { get; init; } = "{0} не умеет «{1}»";
    public string ErrTextNoSuchOp { get; init; } = "Нет такой операции с текстом: «{0}»";
    public string ErrTextTooBig { get; init; } = "Файл слишком большой для перекодировки";
    public string ErrAlreadyUtf8 { get; init; } = "Файл уже в UTF-8";
    public string ErrNotText { get; init; } = "Это не текст или кодировка не распознана";
    public string ErrTextBroken { get; init; } = "Файл повреждён: ошибка в тексте {0}";
    public string ErrNoOfficeFor { get; init; } = "Без Microsoft Office «{0}» не работает для .{1}";
    public string ErrDocUnknownOp { get; init; } = "Неизвестная операция для документов";
    public string ErrPdfScan { get; init; } = "В PDF нет текстового слоя (это скан)";
    public string ErrSheetPassword { get; init; } = "Таблица защищена паролем";
    public string ErrSheetOpen { get; init; } = "Не удалось открыть таблицу: файл повреждён или это не Excel";
    public string ErrSheetRead { get; init; } = "Не удалось прочитать таблицу: файл повреждён";
    public string ErrWordNoBody { get; init; } = "В файле Word нет текста документа";
    public string ErrWordParse { get; init; } = "Не удалось разобрать документ Word";
    public string ErrWordOpen { get; init; } = "Не удалось открыть документ Word: файл повреждён, защищён паролем или это не DOCX";
    public string ErrCsvTooManyRows { get; init; } = "В CSV больше строк, чем помещается на лист Excel (1 048 576)";
    public string ErrCsvTooManyColumns { get; init; } = "В CSV больше столбцов, чем помещается на лист Excel (16 384)";

    // ---------- ошибки: архивы ----------
    public string ErrArchivePassword { get; init; } = "Архив с паролем не поддерживается";
    public string ErrArchiveUnsafeLinks { get; init; } = "Архив небезопасен: в нём ссылки за пределы папки";
    public string ErrArchiveUnsafePaths { get; init; } = "Архив небезопасен: в нём пути за пределы папки";
    public string ErrArchiveLink { get; init; } = "Не удалось создать ссылку из архива";
    public string ErrArchiveDuplicateNames { get; init; } = "В 7Z нельзя положить файлы с одинаковыми именами из разных папок";
    public string ErrArchiveMethod { get; init; } = "Архив сжат неподдерживаемым способом";
    public string ErrArchiveTruncated { get; init; } = "Архив повреждён или не докачан";
    public string ErrNotArchive { get; init; } = "Файл повреждён или это не архив";
    public string ErrArchiveBroken { get; init; } = "Архив повреждён";
    public string ErrArchiveBrokenOrMethod { get; init; } = "Архив повреждён или сжат неподдерживаемым способом";
    public string ErrArchiveTooManyFiles { get; init; } = "В архиве слишком много файлов";
    public string ErrArchiveEmpty { get; init; } = "Архив пуст";
    public string ErrArchiveTooBig { get; init; } = "Архив слишком большой: больше 20 ГБ после распаковки";
    public string ErrArchiveEntryTooBig { get; init; } = "Архив повреждён: файл больше заявленного размера";

    // ---------- ошибки: вывод утилит (ErrorTranslator) ----------
    public string ErrNoStream { get; init; } = "В файле нет нужной дорожки (например, звука)";
    public string ErrCorruptOrUnknown { get; init; } = "Файл повреждён или формат не распознан";
    public string ErrVideoTruncated { get; init; } = "Видео повреждено или не докачано";
    public string ErrAccessDenied { get; init; } = "Нет доступа к файлу или папке";
    public string ErrDiskFull { get; init; } = "Закончилось место на диске";
    public string ErrNoEncoder { get; init; } = "В этой сборке ffmpeg нет нужного кодировщика";
    public string ErrBitmapSubtitles { get; init; } = "Субтитры в видео — картинками, в текст (SRT) их не перевести";
    public string ErrMagickCantRead { get; init; } = "ImageMagick не умеет читать этот формат";
    public string ErrMagickCantWrite { get; init; } = "ImageMagick не умеет сохранять в этот формат";
    public string ErrMagickPolicy { get; init; } = "ImageMagick запретил этот формат своей политикой безопасности";
    public string ErrPdfPassword { get; init; } = "PDF защищён паролем";
    public string ErrNotPdf { get; init; } = "Файл повреждён или это не PDF";
    public string ErrPdfNoCopy { get; init; } = "Автор PDF запретил копировать из него текст";
    public string ErrFileBusy { get; init; } = "Файл занят другой программой";
    public string ErrFfmpegFailed { get; init; } = "ffmpeg не смог обработать файл";
    public string ErrMagickFailed { get; init; } = "ImageMagick не смог обработать файл";
    public string ErrQpdfFailed { get; init; } = "qpdf не смог обработать PDF";
    public string ErrPdfUnreadable { get; init; } = "Не удалось прочитать PDF";
    public string ErrSevenZipFailed { get; init; } = "7-Zip не смог обработать архив";
    public string ErrGenericFailed { get; init; } = "Не удалось обработать файл";

    // ---------- ошибки: Office, Edge, распознавание текста ----------
    public string ErrAppNotFound { get; init; } = "Не найден Microsoft {0}";
    public string ErrDocPassword { get; init; } = "Документ защищён паролем";
    public string ErrAppTimeout { get; init; } = "{0} не ответил за {1} мин — остановлен";
    public string ErrOfficeNotResponding { get; init; } = "Office не отвечает — попробуйте ещё раз";
    public string ErrFileOpenIn { get; init; } = "Файл открыт в {0} — закройте его и повторите";
    public string ErrAppCantConvert { get; init; } = "{0} не смог сконвертировать файл";
    public string ErrAppCantOpen { get; init; } = "{0} не смог открыть файл";
    public string ErrAppCantSave { get; init; } = "{0} не смог сохранить файл";
    public string ErrAppClosed { get; init; } = "{0} неожиданно закрылся";
    public string ErrSlideSize { get; init; } = "PowerPoint не смог прочитать размер слайдов";
    public string ErrNoSlides { get; init; } = "В презентации нет слайдов";
    public string ErrSlideSave { get; init; } = "PowerPoint не смог сохранить слайд {0}";
    public string ErrEdgeExtension { get; init; } = "Edge не печатает в PDF файлы .{0}";
    public string ErrNoWebView2 { get; init; } = "Не найден Microsoft Edge WebView2 Runtime";
    public string ErrEdgeTimeout { get; init; } = "Edge не успел напечатать PDF за {0} секунд";
    public string ErrEdgeSave { get; init; } = "Edge не смог сохранить PDF";
    public string ErrEdgePrint { get; init; } = "Edge (WebView2) не смог напечатать PDF";
    public string ErrEdgeOpen { get; init; } = "Edge не смог открыть файл";
    public string ErrOcrNoLanguage { get; init; } = "В Windows не установлен язык для распознавания текста";
    public string ErrOcrNoText { get; init; } = "Текст не найден";
    public string ErrOcrCantOpen { get; init; } = "Windows не смогла открыть картинку";
}
