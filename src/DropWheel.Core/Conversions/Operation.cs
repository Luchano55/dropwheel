namespace DropWheel.Conversions;

/// <summary>Тип файла. Any — любой файл (например, «упаковать в ZIP»).</summary>
public enum MediaKind { Image, Video, Audio, Pdf, Document, Archive, Text, Any }

public enum OperationMode { Convert, Tool }

/// <summary>
/// Внешние утилиты (FFmpeg, ImageMagick, Qpdf, Pdftoppm, Pdftotext, SevenZip) запускаются процессом с аргументами из шаблона;
/// остальные — встроенные движки (IInternalEngine):
/// Zip — System.IO.Compression; Office — Word/Excel/PowerPoint через COM (только если установлен);
/// DocLibs — бесплатные библиотеки (OpenXML, ClosedXML, Markdig…); WebPdf — HTML → PDF через Edge (WebView2);
/// Ocr — распознавание текста Windows; Text — перекодировка текста.
/// </summary>
public enum EngineKind { FFmpeg, ImageMagick, Qpdf, Pdftoppm, Pdftotext, Zip, Office, DocLibs, WebPdf, Ocr, SevenZip, Text }

/// <summary>PerFile — каждый файл отдельно; Combine — все выбранные файлы в один результат (склеить PDF, ZIP).</summary>
public enum InputMode { PerFile, Combine }

/// <summary>Результат — файл или папка (разбить PDF на страницы, распаковать архив).</summary>
public enum ResultKind { File, Folder }

/// <summary>
/// Правило выбора расширения результата.
/// Fixed — всегда одно расширение; иначе — как у исходника, если оно в списке Keep, иначе Fallback.
/// </summary>
public sealed record OutputRule(string Fallback, IReadOnlyList<string>? Keep = null)
{
    public static OutputRule Fixed(string ext) => new(ext);
    public static OutputRule Same(string fallback, params string[] keep) => new(fallback, keep);

    public string Resolve(string inputExtension)
    {
        var ext = Extensions.Normalize(inputExtension);
        return Keep is not null && Keep.Contains(ext) ? ext : Fallback;
    }
}

/// <summary>
/// Одна операция каталога. Args — шаблон аргументов движка, токены разделены пробелами;
/// плейсхолдеры подставляет ArgumentBuilder. Для папки-результата Output.Fallback не используется.
/// </summary>
public sealed record Operation(
    string Id,
    string Label,
    OperationMode Mode,
    MediaKind Kind,
    EngineKind Engine,
    string Args,
    OutputRule Output,
    string Suffix = "")
{
    public InputMode Inputs { get; init; } = InputMode.PerFile;
    public ResultKind Result { get; init; } = ResultKind.File;
    /// <summary>Сколько файлов нужно минимум (склеить PDF — 2).</summary>
    public int MinFiles { get; init; } = 1;
    /// <summary>Сузить вход до этих расширений внутри типа (Word / Excel / PowerPoint). null — весь тип.</summary>
    public IReadOnlyList<string>? InputExtensions { get; init; }
    /// <summary>
    /// Запасной движок, если основной на этом компьютере недоступен
    /// (DOCX → PDF: Word, а без Office — через Edge). Шаблон Args общий, движки читают из него что им нужно.
    /// </summary>
    public EngineKind? Fallback { get; init; }
    /// <summary>
    /// Основа имени результата (без пометки и расширения) по полным путям всех входов.
    /// null — имя первого входа (photo.heic → photo). Пример: ZIP из нескольких файлов — по имени папки.
    /// </summary>
    public Func<IReadOnlyList<string>, string>? ResultStem { get; init; }
    /// <summary>Сколько файлов можно максимум (обрезка видео — только один: у окна обрезки один ролик).</summary>
    public int MaxFiles { get; init; } = int.MaxValue;
    /// <summary>
    /// false — операция не предлагается колесом (и не видна в настройках колеса): это вариант другой операции,
    /// его выбирает её окно или CLI (точная обрезка — галочка в окне «Обрезать…», <c>--exact</c>).
    /// </summary>
    public bool OnWheel { get; init; } = true;
    /// <summary>
    /// true — по умолчанию лепесток скрыт (редкий формат, например DOC), но его можно показать в «Настройки → Колесо».
    /// Как только пользователь сам настроил режим, действует только его список скрытых.
    /// </summary>
    public bool HiddenByDefault { get; init; }
    /// <summary>
    /// Имена параметров задачи, которые задаёт пользователь (обрезка: start, end). В шаблоне — {имя}.
    /// Пока значений нет (<see cref="Values"/>), приложение сначала спрашивает их (окно обрезки).
    /// </summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];
    /// <summary>
    /// Значения параметров одной задачи (см. <see cref="WithValues"/>). Подставляются в шаблон как есть,
    /// каждое — внутрь своего токена, так что пробелы значений не разбивают аргументы.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Values { get; init; }
    /// <summary>
    /// Параметры-секреты (пароль PDF, решение D-PDFPASS): их значения не попадают ни в журнал, ни в подробности
    /// ошибки, ни в командную строку утилиты (Converter передаёт такие аргументы qpdf через временный файл @…).
    /// Фигурные скобки в них можно (в отличие от остальных параметров).
    /// </summary>
    public IReadOnlyList<string> SecretParameters { get; init; } = [];

    /// <summary>Есть параметры, а значений ещё нет — сначала спросить пользователя.</summary>
    public bool NeedsValues => Parameters.Count > 0 && Values is null;

    /// <summary>Та же операция со значениями параметров для одной задачи (каталог не меняется).</summary>
    public Operation WithValues(IReadOnlyDictionary<string, string> values) =>
        this with { Values = new Dictionary<string, string>(values, StringComparer.Ordinal) };

    /// <summary>Параметры, для которых не задано значение (пустые тоже не в счёт).</summary>
    public IReadOnlyList<string> MissingValues() =>
        Parameters.Where(p => Values is null || !Values.TryGetValue(p, out var v) || string.IsNullOrWhiteSpace(v)).ToList();

    /// <summary>Заданные значения параметров-секретов (для маскировки в журнале); пустые не в счёт.</summary>
    public IReadOnlyList<string> SecretValues() =>
        Values is null ? [] : SecretParameters
            .Select(p => Values.TryGetValue(p, out var v) ? v : null)
            .Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Операция-конвертация не предлагается файлу, который уже в целевом формате.</summary>
    public bool AppliesTo(string inputExtension)
    {
        var ext = Extensions.Normalize(inputExtension);
        if (Kind != MediaKind.Any && Extensions.KindOf(ext) != Kind) return false;
        if (InputExtensions is not null && !InputExtensions.Contains(ext)) return false;
        if (Mode == OperationMode.Convert && Result == ResultKind.File && Output.Keep is null
            && Extensions.SameFormat(ext, Output.Fallback)) return false;
        return true;
    }

    /// <summary>
    /// Файл уже в формате результата этой конвертации (photo.jpg и «JPG»): AppliesTo его не берёт,
    /// а в папку-результат «Отпуск (JPG)» он копируется как есть (решение D-FOLDERS).
    /// </summary>
    public bool AlreadyIn(string inputExtension)
    {
        var ext = Extensions.Normalize(inputExtension);
        if (Mode != OperationMode.Convert || Result != ResultKind.File || Output.Keep is not null) return false;
        if (Kind != MediaKind.Any && Extensions.KindOf(ext) != Kind) return false;
        if (InputExtensions is not null && !InputExtensions.Contains(ext)) return false;
        return Extensions.SameFormat(ext, Output.Fallback);
    }
}
