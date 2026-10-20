using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Cli;

public static class Program
{
    private const string Usage = """
        Использование:
          dropwheel convert <файл> [<файл>...] <операция> [параметры]
          dropwheel list [<файл>...]        — какие операции доступны (для файлов — только подходящие)

        Параметры:
          --out-dir <папка>   класть результат в эту папку (по умолчанию — рядом с исходником)
          --ffmpeg <путь>     путь к ffmpeg
          --magick <путь>     путь к magick
          -v, --verbose       печатать команды утилит

        Пример:
          dropwheel convert "Мои фото/тест 1.heic" to-jpg
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            return await RunAsync(args, Console.Out, Console.Error);
        }
        catch (UsageException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 2;
        }
    }

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            stdout.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        var positional = new List<string>();
        string? outDir = null, ffmpeg = null, magick = null;
        var verbose = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out-dir": outDir = Value(args, ref i); break;
                case "--ffmpeg": ffmpeg = Value(args, ref i); break;
                case "--magick": magick = Value(args, ref i); break;
                case "-v" or "--verbose": verbose = true; break;
                default: positional.Add(args[i]); break;
            }
        }

        return args[0] switch
        {
            "list" => List(positional, stdout),
            "convert" => await Convert(positional, outDir, new ToolLocator { FfmpegPath = ffmpeg, MagickPath = magick },
                verbose, stdout, stderr),
            _ => throw new UsageException($"Неизвестная команда: {args[0]}"),
        };
    }

    private static int List(List<string> files, TextWriter stdout)
    {
        var ops = files.Count == 0 ? OperationCatalog.All : OperationCatalog.ForFiles(files);
        if (ops.Count == 0)
        {
            stdout.WriteLine("Нечего предложить");
            return 1;
        }
        foreach (var op in ops)
            stdout.WriteLine($"{op.Id,-16} {op.Kind,-8} {(op.Mode == OperationMode.Convert ? "→ " : "")}{op.Label}");
        return 0;
    }

    private static async Task<int> Convert(List<string> positional, string? outDir, ToolLocator tools,
        bool verbose, TextWriter stdout, TextWriter stderr)
    {
        if (positional.Count < 2) throw new UsageException("Нужны файл и операция.");
        var opId = positional[^1];
        var files = positional[..^1];
        var op = OperationCatalog.Find(opId) ?? throw new UsageException($"Неизвестная операция: {opId}");

        var settings = ConversionSettings.Default with
        {
            OutputFolder = outDir is null ? null : Path.GetFullPath(outDir),
        };
        Action<string>? log = verbose ? line => { lock (stderr) stderr.WriteLine(line); } : null;
        var queue = new JobQueue(new Converter(tools, settings, [new ZipEngine(), new TextEngine()]), maxParallel: 2, log);

        var jobs = queue.EnqueueSelection(files, op);
        await Task.WhenAll(jobs.Select(j => j.Completion));

        var failed = 0;
        foreach (var job in jobs)
        {
            if (job.State == JobState.Done)
                stdout.WriteLine($"Готово: {job.OutputPath}");
            else
            {
                failed++;
                stderr.WriteLine($"Ошибка: {job.Input}: {job.Error}");
                if (verbose && job.ErrorDetails is not null) stderr.WriteLine(job.ErrorDetails);
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private static string Value(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new UsageException($"После {args[i - 1]} нужно значение.");
}

public sealed class UsageException(string message) : Exception(message);
