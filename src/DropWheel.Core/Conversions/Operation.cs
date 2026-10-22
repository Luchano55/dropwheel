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
