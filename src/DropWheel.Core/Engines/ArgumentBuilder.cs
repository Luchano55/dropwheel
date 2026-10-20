using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// Собирает список аргументов для ffmpeg / ImageMagick.
/// Пути передаются отдельными элементами списка — без кавычек и экранирования.
/// </summary>
public static class ArgumentBuilder
{
    public static IReadOnlyList<string> Build(Operation op, string input, string output, ConversionSettings settings)
    {
        var outExt = Extensions.Normalize(Path.GetExtension(output));
        var middle = Expand(op.Args, outExt, settings);

        return op.Engine switch
        {
            EngineKind.FFmpeg =>
            [
                "-hide_banner", "-nostdin", "-y", "-nostats", "-progress", "pipe:1",
                "-i", input,
                .. middle,
                output,
            ],
            // [0] — только первый кадр/страница: статичный результат из GIF/TIFF/HEIC-серий.
            EngineKind.ImageMagick =>
            [
                input + "[0]",
                .. middle,
                .. (outExt is "jpg" or "jpeg" or "pdf" ? FlattenOnWhite : []),
                output,
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(op)),
        };
    }

    /// <summary>JPEG (и PDF с JPEG-сжатием) не умеет прозрачность — подкладываем белый фон.</summary>
    private static readonly string[] FlattenOnWhite = ["-background", "white", "-alpha", "remove", "-alpha", "off"];

    public static string AudioCodec(string outExt, ConversionSettings s) => outExt switch
    {
        "mp3" => $"-c:a libmp3lame -b:a {s.Mp3BitrateKbps}k",
        "m4a" => "-c:a aac -b:a 192k",
        "wav" => "-c:a pcm_s16le",
        "flac" => "-c:a flac",
        "ogg" => "-c:a libvorbis -q:a 6",
        "opus" => "-c:a libopus -b:a 128k",
        _ => throw new ArgumentOutOfRangeException(nameof(outExt), outExt, "Нет аудиокодека для формата"),
    };

    private static string[] Expand(string template, string outExt, ConversionSettings s)
    {
        var text = template;
        if (text.Contains("{acodec}")) text = text.Replace("{acodec}", AudioCodec(outExt, s));
        text = text
            .Replace("{jpgq}", s.JpegQuality.ToString())
            .Replace("{webpq}", s.WebpQuality.ToString())
            .Replace("{crf}", s.VideoCrf.ToString())
            .Replace("{crfsmall}", s.CompressCrf.ToString())
            .Replace("{mp3k}", s.Mp3BitrateKbps.ToString());
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
