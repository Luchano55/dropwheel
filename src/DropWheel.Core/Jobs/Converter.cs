using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Jobs;

/// <summary>
/// Одна конвертация от начала до конца: временный файл → утилита → переименование в свободное имя.
/// Исходник только читается.
/// </summary>
public sealed class Converter(ToolLocator tools, ConversionSettings settings)
{
    private readonly EngineRunner _runner = new(tools);

    public ConversionSettings Settings { get; } = settings;

    /// <returns>Полный путь готового файла.</returns>
    public async Task<string> ConvertAsync(string input, Operation op,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        input = Path.GetFullPath(input);
        if (!File.Exists(input)) throw new ConversionException("Файл не найден", input);
        if (!op.AppliesTo(Path.GetExtension(input)))
            throw new ConversionException($"Операция «{op.Label}» не подходит для этого файла", $"{op.Id}: {input}");

        var outExt = op.Output.Resolve(Path.GetExtension(input));
        var desired = OutputNamer.DesiredPath(input, outExt, op.Suffix, Settings.OutputFolder);
        var dir = Path.GetDirectoryName(desired)!;
        Directory.CreateDirectory(dir);

        // Временное имя без пробелов и скобок: расширение нужно утилите, чтобы выбрать формат.
        var temp = Path.Combine(dir, $".dropwheel-{Guid.NewGuid():N}.{outExt}");
        try
        {
            var args = ArgumentBuilder.Build(op, input, temp, Settings);
            await _runner.RunAsync(op.Engine, args, progress, log, ct);

            var info = new FileInfo(temp);
            if (!info.Exists || info.Length == 0)
                throw new ConversionException("Утилита не создала файл", temp);

            return MoveToFreeName(temp, desired);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    /// <summary>Имя выбираем в последний момент; если его успели занять — берём следующее.</summary>
    private static string MoveToFreeName(string temp, string desired)
    {
        for (var attempt = 0; ; attempt++)
        {
            var target = OutputNamer.FirstFree(desired);
            try
            {
                File.Move(temp, target, overwrite: false);
                return target;
            }
            catch (IOException) when (attempt < 10 && File.Exists(target))
            {
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* не критично */ }
    }
}
