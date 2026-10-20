using DropWheel.Conversions;
using DropWheel.Engines;

namespace DropWheel.Jobs;

/// <summary>
/// Одна конвертация от начала до конца: временный файл/папка → движок → переименование в свободное имя.
/// Исходники только читаются.
/// </summary>
public sealed class Converter(ToolLocator tools, ConversionSettings settings, IEnumerable<IInternalEngine>? internalEngines = null)
{
    private readonly EngineRunner _runner = new(tools);
    private readonly Dictionary<EngineKind, IInternalEngine> _internal =
        (internalEngines ?? []).ToDictionary(e => e.Kind);

    public ConversionSettings Settings { get; } = settings;

    /// <returns>Полный путь готового файла.</returns>
    public Task<string> ConvertAsync(string input, Operation op,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default) =>
        ConvertAsync([input], op, progress, log, ct);

    /// <summary>
    /// PerFile-операция — ровно один вход; Combine — все входы в один результат.
    /// Имя результата — по первому входу (+ пометка операции).
    /// </summary>
    /// <returns>Полный путь готового файла или папки.</returns>
    public async Task<string> ConvertAsync(IReadOnlyList<string> inputs, Operation op,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default)
    {
        if (inputs.Count == 0) throw new ArgumentException("Нет файлов", nameof(inputs));
        if (op.Inputs == InputMode.PerFile && inputs.Count != 1)
            throw new ArgumentException("Операция обрабатывает файлы по одному", nameof(inputs));
        if (inputs.Count < op.MinFiles)
            throw new ConversionException($"Для «{op.Label}» нужно файлов: не меньше {op.MinFiles}");

        var full = inputs.Select(Path.GetFullPath).ToList();
        foreach (var input in full)
        {
            if (!File.Exists(input)) throw new ConversionException("Файл не найден", input);
            if (!op.AppliesTo(Path.GetExtension(input)))
                throw new ConversionException($"Операция «{op.Label}» не подходит для этого файла", $"{op.Id}: {input}");
        }

        var first = full[0];
        var folder = op.Result == ResultKind.Folder;
        var outExt = folder ? "" : op.Output.Resolve(Path.GetExtension(first));
        var desired = folder
            ? OutputNamer.DesiredFolder(first, op.Suffix, Settings.OutputFolder)
            : OutputNamer.DesiredPath(first, outExt, op.Suffix, Settings.OutputFolder);
        var dir = Path.GetDirectoryName(desired)!;
        Directory.CreateDirectory(dir);

        // Временное имя без пробелов и скобок: расширение нужно утилите, чтобы выбрать формат.
        var temp = Path.Combine(dir, folder ? $".dropwheel-{Guid.NewGuid():N}" : $".dropwheel-{Guid.NewGuid():N}.{outExt}");
        try
        {
            if (folder) Directory.CreateDirectory(temp);
            await RunEngineAsync(op, full, temp, progress, log, ct);

            if (folder)
            {
                if (!Directory.EnumerateFileSystemEntries(temp).Any())
                    throw new ConversionException("Утилита ничего не создала", temp);
            }
            else
            {
                var info = new FileInfo(temp);
                if (!info.Exists || info.Length == 0)
                    throw new ConversionException("Утилита не создала файл", temp);
            }

            return MoveToFreeName(temp, desired, folder);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task RunEngineAsync(Operation op, IReadOnlyList<string> inputs, string output,
        IProgress<double>? progress, Action<string>? log, CancellationToken ct)
    {
        if (_internal.TryGetValue(op.Engine, out var engine))
        {
            if (!engine.IsAvailable)
                throw new ConversionException(op.Engine == EngineKind.Office
                    ? "Не найден Microsoft Office"
                    : $"Недоступен движок {op.Engine}");
            log?.Invoke($"> [{op.Engine}] {op.Id} {string.Join(" | ", inputs)} → {output}");
            await engine.RunAsync(op, inputs, output, Settings, progress, ct);
            progress?.Report(1.0);
            return;
        }

        var args = ArgumentBuilder.Build(op, inputs, output, Settings);
        await _runner.RunAsync(op.Engine, args, progress, log, ct);
    }

    /// <summary>Имя выбираем в последний момент; если его успели занять — берём следующее.</summary>
    private static string MoveToFreeName(string temp, string desired, bool folder)
    {
        for (var attempt = 0; ; attempt++)
        {
            var target = OutputNamer.FirstFree(desired, folder: folder);
            try
            {
                if (folder) Directory.Move(temp, target);
                else File.Move(temp, target, overwrite: false);
                return target;
            }
            catch (IOException) when (attempt < 10 && (File.Exists(target) || Directory.Exists(target)))
            {
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { /* не критично */ }
    }
}
