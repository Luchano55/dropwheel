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
    // Утилиты в tools/ за время работы не появляются и не пропадают — ищем один раз.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<EngineKind, bool> _toolFound = new();

    private ConversionSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    /// <summary>
    /// Качество и папка результата. Можно менять на ходу (окно настроек): новые задачи берут новые значения,
    /// уже начатые доделываются со своими — каждая конвертация читает настройки один раз в начале.
    /// </summary>
    public ConversionSettings Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Можно ли выполнить операцию на этом компьютере (основным или запасным движком).</summary>
    public bool CanRun(Operation op) =>
        EngineFor(op) is not null;

    /// <summary>То же для файла с этим расширением — для фильтра каталога (ForFiles).</summary>
    public bool CanRun(Operation op, string extension) =>
        EngineFor(op, extension) is not null;

    /// <summary>Пользователь сейчас выберет одну из операций — дать движкам подготовиться (прогрев Office).</summary>
    public void Prepare(IReadOnlyList<Operation> offered)
    {
        foreach (var group in offered.GroupBy(o => EngineFor(o)))
            if (group.Key is { } kind && _internal.TryGetValue(kind, out var engine))
                engine.Prepare(group.ToList());
    }

    /// <summary>
    /// Движок, которым операция будет выполнена здесь: основной, иначе запасной, иначе null.
    /// extension — входное расширение (без точки); null — не проверять расширение.
    /// </summary>
    public EngineKind? EngineFor(Operation op, string? extension = null)
    {
        if (CanUse(op.Engine, op, extension)) return op.Engine;
        if (op.Fallback is { } fb && CanUse(fb, op, extension)) return fb;
        return null;
    }

    private bool CanUse(EngineKind kind, Operation op, string? extension)
    {
        if (_internal.TryGetValue(kind, out var engine))
            return extension is null ? engine.CanRun(op) : engine.CanRun(op, Extensions.Normalize(extension));
        return kind is EngineKind.FFmpeg or EngineKind.ImageMagick or EngineKind.Qpdf
                or EngineKind.Pdftoppm or EngineKind.Pdftotext or EngineKind.SevenZip
            && _toolFound.GetOrAdd(kind, tools.IsAvailable);
    }

    /// <returns>Полный путь готового файла.</returns>
    public Task<string> ConvertAsync(string input, Operation op,
        IProgress<double>? progress = null, Action<string>? log = null, CancellationToken ct = default) =>
        ConvertAsync([input], op, progress, log, ct);

    /// <summary>
    /// PerFile-операция — ровно один вход; Combine — все входы в один результат.
    /// Имя результата — по первому входу или Operation.ResultStem (+ пометка операции).
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

        var settings = Settings;
        var first = full[0];
        var folder = op.Result == ResultKind.Folder;
        var outExt = folder ? "" : op.Output.Resolve(Path.GetExtension(first));
        var stem = op.ResultStem?.Invoke(full);
        var desired = folder
            ? OutputNamer.DesiredFolder(first, op.Suffix, settings.OutputFolder, stem)
            : OutputNamer.DesiredPath(first, outExt, op.Suffix, settings.OutputFolder, stem);
        var dir = Path.GetDirectoryName(desired)!;
        Directory.CreateDirectory(dir);

        // Временный результат — в скрытой папке рядом (в проводнике его не видно; точка в начале имени
        // Windows файл не прячет). Имя без пробелов и скобок; расширение нужно утилите, чтобы выбрать формат.
        var tempDir = AcquireTempDir(dir);
        var temp = Path.Combine(tempDir, folder ? $".dropwheel-{Guid.NewGuid():N}" : $".dropwheel-{Guid.NewGuid():N}.{outExt}");
        try
        {
            if (folder) Directory.CreateDirectory(temp);
            await RunEngineAsync(op, full, temp, settings, progress, log, ct);

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
            ReleaseTempDir(tempDir);
        }
    }

    /// <summary>Скрытая папка для временных результатов. Её делят задачи в одной папке — удаляет последняя.</summary>
    public const string TempDirName = ".dropwheel-tmp";

    private static readonly object TempLock = new();
    private static readonly Dictionary<string, int> TempUsers = new(StringComparer.OrdinalIgnoreCase);

    private static string AcquireTempDir(string dir)
    {
        var path = Path.Combine(dir, TempDirName);
        lock (TempLock)
        {
            var info = Directory.CreateDirectory(path);
            try { info.Attributes |= FileAttributes.Hidden; } catch { /* не на Windows — точки достаточно */ }
            TempUsers[path] = TempUsers.GetValueOrDefault(path) + 1;
        }
        return path;
    }

    private static void ReleaseTempDir(string path)
    {
        lock (TempLock)
        {
            var users = TempUsers.GetValueOrDefault(path) - 1;
            if (users > 0)
            {
                TempUsers[path] = users;
                return;
            }
            TempUsers.Remove(path);
            try
            {
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path);
            }
            catch { /* не критично */ }
        }
    }

    private async Task RunEngineAsync(Operation op, IReadOnlyList<string> inputs, string output,
        ConversionSettings settings, IProgress<double>? progress, Action<string>? log, CancellationToken ct)
    {
        var kind = EngineFor(op, Path.GetExtension(inputs[0])) ?? throw new ConversionException(op.Engine == EngineKind.Office
            ? "Не найден Microsoft Office"
            : $"На этом компьютере нечем выполнить «{op.Label}»");

        if (_internal.TryGetValue(kind, out var engine))
        {
            log?.Invoke($"> [{kind}] {op.Id} {string.Join(" | ", inputs)} → {output}");
            await engine.RunAsync(op, inputs, output, settings, progress, ct);
            progress?.Report(1.0);
            return;
        }

        // Запасной внешний движок получает операцию «как свою» — с подменённым Engine.
        var effective = kind == op.Engine ? op : op with { Engine = kind };
        // Распаковка 7-Zip: .tar.gz — в два запуска (см. SevenZip.ExtractAsync).
        if (kind == EngineKind.SevenZip && op.Result == ResultKind.Folder)
        {
            await SevenZip.ExtractAsync(_runner, effective, inputs[0], output, settings, progress, log, ct);
            return;
        }
        var args = ArgumentBuilder.Build(effective, inputs, output, settings);
        await _runner.RunAsync(kind, args, progress, log, ct);
        // У скана pdftotext «успешно» пишет пустой текст.
        if (kind == EngineKind.Pdftotext) PdfText.EnsureHasText(output);
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
