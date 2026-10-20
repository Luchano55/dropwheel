namespace DropWheel.Conversions;

public enum MediaKind { Image, Video, Audio }

public enum OperationMode { Convert, Tool }

public enum EngineKind { FFmpeg, ImageMagick }

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
/// Одна операция каталога. Args — шаблон аргументов движка между входом и выходом,
/// токены разделены пробелами; плейсхолдеры подставляет ArgumentBuilder.
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
    /// <summary>Операция-конвертация не предлагается файлу, который уже в целевом формате.</summary>
    public bool AppliesTo(string inputExtension)
    {
        var ext = Extensions.Normalize(inputExtension);
        if (Extensions.KindOf(ext) != Kind) return false;
        if (Mode == OperationMode.Convert && Output.Keep is null
            && Extensions.SameFormat(ext, Output.Fallback)) return false;
        return true;
    }
}
