using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// 7-Zip (внешняя утилита 7z.exe + 7z.dll): «Распаковать» 7z/RAR/TAR/GZ/BZ2/XZ и «В 7Z».
/// Аргументы — в каталоге (OperationCatalog); здесь то, что одним запуском не выражается:
/// имя папки, распаковка .tar.gz в два шага, прогресс из -bsp1.
/// Безопасность распаковки — на стороне 7-Zip: «..» и абсолютные пути он отрезает (файлы остаются внутри папки),
/// ссылки за пределы папки не создаёт и завершается с ошибкой (см. docs/decisions.md, D-7ZIP).
/// </summary>
public static class SevenZip
{
    public const string ExtractId = "archive-extract";
    public const string PackId = "pack-7z";

    /// <summary>Сжатие одного потока: внутри обычно .tar (tar.gz, tgz, tar.bz2, tar.xz).</summary>
    private static readonly string[] SingleStream = ["gz", "tgz", "bz2", "xz"];

    /// <summary>
    /// Имя папки распаковки (без пометки): «photos.7z» → «photos», «photos.tar.gz» → «photos» (а не «photos.tar»).
    /// </summary>
    public static string FolderName(IReadOnlyList<string> inputs)
    {
        var file = Path.GetFileName(inputs[0]);
        var stem = Path.GetFileNameWithoutExtension(file);
        if (IsSingleStream(file) && stem.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) && stem.Length > 4)
            stem = stem[..^4];
        return stem.Length == 0 ? file : stem;
    }

    private static bool IsSingleStream(string path) =>
        SingleStream.Contains(Extensions.Normalize(Path.GetExtension(path)));

    /// <summary>
    /// Распаковать архив в пустую папку output. Для .gz/.tgz/.bz2/.xz 7-Zip снимает только сжатие и кладёт
    /// один .tar — тогда распаковываем и его вторым запуском, а сам .tar не оставляем.
    /// </summary>
    internal static async Task ExtractAsync(EngineRunner runner, Operation op, string archive, string output,
        ConversionSettings settings, IProgress<double>? progress, Action<string>? log, CancellationToken ct)
    {
        var twoSteps = IsSingleStream(archive);
        await runner.RunAsync(EngineKind.SevenZip, ArgumentBuilder.Build(op, archive, output, settings),
            twoSteps ? Part(progress, 0, 0.5) : progress, log, ct);
        if (!twoSteps) return;
        if (SingleTar(output) is not { } tar)
        {
            progress?.Report(1.0); // обычный .gz с одним файлом — готово
            return;
        }

        // .tar — рядом с временной папкой (в той же скрытой .dropwheel-tmp), чтобы его записи ни с чем не совпали.
        var inner = output + ".tar";
        File.Move(tar, inner);
        try
        {
            await runner.RunAsync(EngineKind.SevenZip, ArgumentBuilder.Build(op, inner, output, settings),
                Part(progress, 0.5, 1.0), log, ct);
        }
        finally
        {
            try { File.Delete(inner); } catch { /* не критично */ }
        }
    }

    /// <summary>Единственный файл в папке, если это tar (подпись «ustar» со смещения 257), иначе null.</summary>
    internal static string? SingleTar(string folder)
    {
        var entries = Directory.GetFileSystemEntries(folder);
        if (entries.Length != 1 || !File.Exists(entries[0])) return null;
        var header = new byte[262];
        using (var stream = File.OpenRead(entries[0]))
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length) return null;
        return Encoding.ASCII.GetString(header, 257, 5) == "ustar" ? entries[0] : null;
    }

    private static IProgress<double>? Part(IProgress<double>? progress, double from, double to) =>
        progress is null ? null : new PartProgress(progress, from, to);

    private sealed class PartProgress(IProgress<double> inner, double from, double to) : IProgress<double>
    {
        public void Report(double value) => inner.Report(from + (to - from) * Math.Clamp(value, 0, 1));
    }
}

/// <summary>
/// Прогресс 7-Zip (-bsp1): строка вида « 43% 2 + file.txt» без перевода строки, которую 7-Zip стирает
/// символами backspace (\b) и пишет заново. Поток stdout читается кусками; кусок может оборваться посреди строки.
/// </summary>
internal sealed partial class SevenZipProgress
{
    private readonly StringBuilder _segment = new();

    [GeneratedRegex(@"^\s*(\d{1,3})%")]
    private static partial Regex PercentRegex();

    /// <returns>Последний прочитанный процент (0…1) в этом куске или null.</returns>
    public double? Read(ReadOnlySpan<char> chunk)
    {
        double? last = null;
        foreach (var c in chunk)
        {
            if (c is '\b' or '\r' or '\n')
            {
                if (Parse() is { } p) last = p;
                _segment.Clear();
            }
            else if (_segment.Length < 256)
                _segment.Append(c);
        }
        return last;
    }

    private double? Parse()
    {
        var m = PercentRegex().Match(_segment.ToString());
        // 1.0 сообщает EngineRunner, когда 7-Zip действительно завершился.
        return m.Success ? Math.Min(int.Parse(m.Groups[1].Value) / 100.0, 0.999) : null;
    }
}
