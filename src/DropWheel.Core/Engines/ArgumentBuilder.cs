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
        var middle = WithValues(Expand(op.Args, outExt, settings), op);

        IReadOnlyList<string> args = op.Engine switch
        {
            // Вход «-i файл» — сразу после общих ключей; если в шаблоне есть {in} — там, где он стоит
            // (параметры входа, например «-ss … -to … -i {in}»). {in} подставляется последним — после {out}.
            EngineKind.FFmpeg =>
            [
                "-hide_banner", "-nostdin", "-y", "-nostats", "-progress", "pipe:1",
                .. (middle.Contains("{in}") ? [] : new[] { "-i", input }),
                .. FfmpegOutput(middle, output).Select(t => t == "{in}" ? input : t),
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
        return RevealSecrets(args, op);
    }

    /// <summary>
    /// Секреты (пароль) подставляются метками, а настоящие значения — в самом конце: так «{in}» или «{out}»
    /// внутри пароля не заменятся путями.
    /// </summary>
    private static string SecretMark(int index) => $"\u0001secret{index}\u0001";

    private static IReadOnlyList<string> RevealSecrets(IReadOnlyList<string> args, Operation op)
    {
        if (op.SecretParameters.Count == 0) return args;
        return args.Select(a =>
        {
            for (var i = 0; i < op.SecretParameters.Count; i++)
                if (op.Values?.TryGetValue(op.SecretParameters[i], out var v) == true) a = a.Replace(SecretMark(i), v);
            return a;
        }).ToList();
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

    /// <summary>
    /// ffmpeg: результат — последним аргументом. Если в шаблоне есть {out} (серия кадров в папку:
    /// «{out}/{frame}-%03d.jpg»), подставляем папку туда, а «%» в самом пути удваиваем —
    /// иначе ffmpeg примет «%» из имени папки («100% фото») за номер кадра.
    /// </summary>
    private static string[] FfmpegOutput(string[] template, string output)
    {
        if (!template.Any(t => t.Contains("{out}"))) return [.. template, output];
        var escaped = output.Replace("%", "%%");
        return template.Select(t => t.Replace("{out}/", escaped + Path.DirectorySeparatorChar).Replace("{out}", escaped)).ToArray();
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

    /// <summary>
    /// Значения параметров задачи ({start}, {end} …, см. Operation.Values) — внутрь токенов, уже после разбиения
    /// шаблона по пробелам: значение с пробелом остаётся одним аргументом. Не задано — ConversionException.
    /// </summary>
    private static string[] WithValues(string[] tokens, Operation op)
    {
        if (op.Parameters.Count == 0) return tokens;
        if (op.MissingValues() is { Count: > 0 } missing)
            throw new ConversionException(Loc.F(Loc.T.ErrMissingValues, Loc.Label(op), string.Join(", ", missing)));
        return tokens.Select(t =>
        {
            foreach (var name in op.Parameters)
            {
                var secret = IndexOf(op.SecretParameters, name);
                t = t.Replace("{" + name + "}", secret >= 0 ? SecretMark(secret) : op.Values![name]);
            }
            return t;
        }).ToArray();
    }

    private static int IndexOf(IReadOnlyList<string> list, string name)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i] == name) return i;
        return -1;
    }

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
            .Replace("{page}", Loc.T.PageFile)
            .Replace("{frame}", Loc.T.FrameFile);
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
