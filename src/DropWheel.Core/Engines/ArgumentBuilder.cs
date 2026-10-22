using DropWheel.Localization;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// Собирает список аргументов для ffmpeg / ImageMagick / qpdf / pdftoppm.
/// Пути передаются отдельными элементами списка — без кавычек и экранирования.
/// </summary>
public static class ArgumentBuilder
{
    public static IReadOnlyList<string> Build(Operation op, string input, string output, ConversionSettings settings) =>
        Build(op, [input], output, settings);

    /// <param name="output">Путь файла-результата или папки (ResultKind.Folder) — временный, его выбирает Converter.</param>
    public static IReadOnlyList<string> Build(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings)
    {
        var input = inputs[0];
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
            // Combine (картинки → один PDF): все входы, каждый — первым кадром, по странице на файл.
            EngineKind.ImageMagick =>
            [
                .. (op.Inputs == InputMode.Combine ? inputs : [input]).Select(i => i + "[0]"),
                .. middle,
                .. (outExt is "jpg" or "jpeg" or "pdf" ? FlattenOnWhite : []),
                output,
            ],
            // Остальные внешние утилиты: шаблон целиком, с явными {in} / {ins} / {out}.
            EngineKind.Qpdf or EngineKind.Pdftoppm or EngineKind.Pdftotext or EngineKind.SevenZip => Explicit(middle, inputs, output),
            _ => throw new ArgumentOutOfRangeException(nameof(op), op.Engine, "Встроенный движок — не процесс"),
        };
    }

    /// <summary>
    /// {in} — первый вход, {ins} — отдельный токен, заменяется всеми входами по одному аргументу,
    /// {out} — результат (файл или папка; допускается «{out}/стр» внутри токена).
    /// </summary>
    private static List<string> Explicit(string[] template, IReadOnlyList<string> inputs, string output)
    {
        var args = new List<string>(template.Length + inputs.Count);
        foreach (var token in template)
        {
            if (token == "{ins}") args.AddRange(inputs);
            // «{out}/стр» → разделитель текущей ОС (в Windows — «\»).
            else args.Add(token.Replace("{in}", inputs[0])
                .Replace("{out}/", output + Path.DirectorySeparatorChar).Replace("{out}", output));
        }
        return args;
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
            .Replace("{mp3k}", s.Mp3BitrateKbps.ToString())
            .Replace("{page}", Loc.T.PageFile);
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
