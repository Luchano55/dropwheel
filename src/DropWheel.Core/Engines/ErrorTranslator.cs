using DropWheel.Conversions;
using DropWheel.Localization;

namespace DropWheel.Engines;

/// <summary>Короткая человеческая причина ошибки по выводу утилиты — на языке интерфейса.</summary>
public static class ErrorTranslator
{
    /// <summary>
    /// Только для 7-Zip и раньше общих строк: у опасной ссылки 7-Zip пишет ещё и «No such file or directory»,
    /// а «Data Error» у других утилит может значить другое.
    /// Неверный пароль: «Data Error in encrypted file. Wrong password?» / «Cannot open encrypted archive. Wrong password?».
    /// </summary>
    private static readonly (string Needle, Func<Texts, string> Message)[] SevenZipKnown =
    [
        ("Wrong password", t => t.ErrArchivePassword),
        ("Dangerous link path", t => t.ErrArchiveUnsafeLinks),
        ("Cannot create hard link", t => t.ErrArchiveLink),
        ("Cannot create symbolic link", t => t.ErrArchiveLink),
        ("Duplicate filename on disk", t => t.ErrArchiveDuplicateNames),
        ("Unsupported Method", t => t.ErrArchiveMethod),
        ("Unexpected end of archive", t => t.ErrArchiveTruncated),
        ("Is not archive", t => t.ErrNotArchive),
        ("Cannot open the file as", t => t.ErrNotArchive),
        ("CRC Failed", t => t.ErrArchiveBroken),
        ("Data Error", t => t.ErrArchiveBroken),
        ("Headers Error", t => t.ErrArchiveBroken),
    ];

    private static readonly (string Needle, Func<Texts, string> Message)[] Known =
    [
        ("matches no streams", t => t.ErrNoStream),
        ("does not contain any stream", t => t.ErrNoStream),
        ("Invalid data found", t => t.ErrCorruptOrUnknown),
        ("moov atom not found", t => t.ErrVideoTruncated),
        ("No such file or directory", t => t.ErrFileNotFound),
        ("Permission denied", t => t.ErrAccessDenied),
        ("No space left", t => t.ErrDiskFull),
        ("Unknown encoder", t => t.ErrNoEncoder),
        ("only possible from text to text", t => t.ErrBitmapSubtitles),
        ("no decode delegate", t => t.ErrMagickCantRead),
        ("no encode delegate", t => t.ErrMagickCantWrite),
        ("NoDecodeDelegateForThisImageFormat", t => t.ErrMagickCantRead),
        ("NoEncodeDelegateForThisImageFormat", t => t.ErrMagickCantWrite),
        ("not authorized", t => t.ErrMagickPolicy),
        ("improper image header", t => t.ErrCorruptOrUnknown),
        ("Unsupported codec", t => t.ErrMagickCantRead),
        ("invalid password", t => t.ErrPdfPassword),        // qpdf
        ("Incorrect password", t => t.ErrPdfPassword),      // pdftoppm
        ("can't find PDF header", t => t.ErrNotPdf),
        ("May not be a PDF file", t => t.ErrNotPdf),
        ("Copying of text from this document is not allowed", t => t.ErrPdfNoCopy), // pdftotext
        ("being used by another process", t => t.ErrFileBusy), // Windows
        ("not enough space on the disk", t => t.ErrDiskFull),    // Windows
    ];

    public static string Translate(EngineKind engine, string stderr)
    {
        var t = Loc.T;
        foreach (var (needle, message) in engine == EngineKind.SevenZip ? SevenZipKnown.Concat(Known) : Known)
            if (stderr.Contains(needle, StringComparison.OrdinalIgnoreCase)) return message(t);
        return engine switch
        {
            EngineKind.FFmpeg => t.ErrFfmpegFailed,
            EngineKind.ImageMagick => t.ErrMagickFailed,
            EngineKind.Qpdf => t.ErrQpdfFailed,
            EngineKind.Pdftoppm or EngineKind.Pdftotext => t.ErrPdfUnreadable,
            EngineKind.SevenZip => t.ErrSevenZipFailed,
            _ => t.ErrGenericFailed,
        };
    }
}
