namespace DropWheel.Localization;

/// <summary>
/// Английские подписи и пометки операций каталога (по id). Чего здесь нет — то одинаково на обоих языках
/// (названия форматов: «JPG», «MP4», «720p»). Подписи — не длиннее 16 символов (помещаются в лепесток),
/// уточнение в скобках колесо пишет мелко под названием.
/// </summary>
internal static class OperationTexts
{
    public static readonly IReadOnlyDictionary<string, string> EnglishLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["img-compress"] = "Compress",
        ["img-half"] = "Half size",
        ["img-1920"] = "Fit 1920 px",
        ["img-strip"] = "No metadata",
        ["video-compress"] = "Compress",
        ["video-mute"] = "Mute",
        ["video-rotate-cw"] = "Rotate ↻",
        ["video-frames"] = "Frames",
        ["video-poster"] = "JPG (frame)",
        ["audio-normalize"] = "Normalize",
        ["pdf-to-png"] = "PNG (pages)",
        ["pdf-to-jpg"] = "JPG (pages)",
        ["pdf-merge"] = "Merge",
        ["pdf-split"] = "Split",
        ["pdf-optimize"] = "Optimize",
        ["images-to-pdf"] = "One PDF",
        [Engines.ZipEngine.PackId] = "To ZIP",
        [Engines.ZipEngine.ExtractId] = "Extract",
        ["img-ico"] = "ICO (icon)",
        [Conversions.OperationCatalog.OcrId] = "Text (OCR)",
        ["video-subs"] = "Subtitles SRT",
        [Engines.TextEngine.ToUtf8Id] = "To UTF-8",
        [Engines.SevenZip.ExtractId] = "Extract",
        [Engines.SevenZip.PackId] = "To 7Z",
        ["pdf-to-txt"] = "TXT (text)",
        ["pdf-rotate"] = "Rotate ↻",
        ["ppt-to-png"] = "Slides PNG",
    };

    public static readonly IReadOnlyDictionary<string, string> EnglishSuffixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["img-compress"] = " (compressed)",
        ["img-strip"] = " (no metadata)",
        ["video-compress"] = " (compressed)",
        ["video-mute"] = " (muted)",
        ["video-rotate-cw"] = " (rotated)",
        ["video-frames"] = " (frames)",
        ["video-poster"] = " (frame)",
        ["audio-normalize"] = " (normalized)",
        ["pdf-to-png"] = " (pages)",
        ["pdf-to-jpg"] = " (pages)",
        ["pdf-merge"] = " (merged)",
        ["pdf-split"] = " (split)",
        ["pdf-optimize"] = " (optimized)",
        ["images-to-pdf"] = " (all)",
        ["pdf-rotate"] = " (rotated)",
        ["ppt-to-png"] = " (slides)",
    };
}
