using System.Globalization;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;

namespace DropWheel.Cli;

public static class Program
{
    private static string Usage => Loc.T.CliUsage.TrimEnd() + "\n\n" + Loc.T.CliTrimUsage;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        // Язык сообщений — как в системе (русский для русской, иначе английский); --lang переопределяет.
        Loc.Current = Loc.Resolve(Loc.Auto, CultureInfo.CurrentUICulture);
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

    /// <summary>Разобрать и выполнить команду. «--lang ru|en» в любом месте меняет язык сообщений (Loc.Current).</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        args = TakeLanguage(args);
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            stdout.WriteLine(Usage);
            return args.Length == 0 ? 2 : 0;
        }

        var positional = new List<string>();
        string? outDir = null, ffmpeg = null, magick = null, start = null, end = null;
        bool verbose = false, exact = false;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out-dir": outDir = Value(args, ref i); break;
                case "--ffmpeg": ffmpeg = Value(args, ref i); break;
                case "--magick": magick = Value(args, ref i); break;
                case "-v" or "--verbose": verbose = true; break;
                case "--start": start = Value(args, ref i); break;
                case "--end": end = Value(args, ref i); break;
                case "--exact": exact = true; break;
                default: positional.Add(args[i]); break;
            }
        }

        return args[0] switch
        {
            "list" => List(positional, stdout),
            "convert" => await Convert(positional, outDir, new ToolLocator { FfmpegPath = ffmpeg, MagickPath = magick },
                new TrimOptions(start, end, exact), verbose, stdout, stderr),
            _ => throw new UsageException(Loc.F(Loc.T.CliUnknownCommand, args[0])),
        };
    }

    private static int List(List<string> files, TextWriter stdout)
    {
        var ops = files.Count == 0 ? OperationCatalog.All : OperationCatalog.ForFiles(files);
        if (ops.Count == 0)
        {
            stdout.WriteLine(Loc.T.WheelNothing);
            return 1;
        }
        foreach (var op in ops)
            stdout.WriteLine($"{op.Id,-16} {op.Kind,-8} {(op.Mode == OperationMode.Convert ? "→ " : "")}{Loc.Label(op)}");
        return 0;
    }

    private static async Task<int> Convert(List<string> positional, string? outDir, ToolLocator tools,
        TrimOptions trim, bool verbose, TextWriter stdout, TextWriter stderr)
    {
        if (positional.Count < 2) throw new UsageException(Loc.T.CliNeedFileAndOp);
        var opId = positional[^1];
        var files = positional[..^1];
        var op = OperationCatalog.Find(opId) ?? throw new UsageException(Loc.F(Loc.T.CliUnknownOperation, opId));
        op = WithTrim(op, trim);

        var settings = ConversionSettings.Default with
        {
            OutputFolder = outDir is null ? null : Path.GetFullPath(outDir),
        };
        Action<string>? log = verbose ? line => { lock (stderr) stderr.WriteLine(line); } : null;
        var queue = new JobQueue(new Converter(tools, settings, [new ZipEngine(), new TextEngine(), new DocLibsEngine()]), maxParallel: 2, log);

        var jobs = queue.EnqueueSelection(files, op);
        await Task.WhenAll(jobs.Select(j => j.Completion));

        var failed = 0;
        foreach (var job in jobs)
        {
            if (job.State == JobState.Done)
                stdout.WriteLine(Loc.F(Loc.T.CliDone, job.OutputPath));
            else
            {
                failed++;
                stderr.WriteLine(Loc.F(Loc.T.CliError, job.Input, job.Error));
                if (verbose && job.ErrorDetails is not null) stderr.WriteLine(job.ErrorDetails);
            }
        }
        return failed == 0 ? 0 : 1;
    }

    private sealed record TrimOptions(string? Start, string? End, bool Exact)
    {
        public bool Any => Start is not null || End is not null || Exact;
    }

    /// <summary>
    /// video-trim / video-trim-exact: --start и --end (12, 0:05, 1:02.5…) → значения задачи; --exact — точный вариант.
    /// Без --start — с начала, без --end — до конца ролика. У других операций эти ключи — ошибка.
    /// </summary>
    private static Operation WithTrim(Operation op, TrimOptions trim)
    {
        var isTrim = op.Id is VideoTrim.Id or VideoTrim.ExactId;
        if (!isTrim)
        {
            if (trim.Any) throw new UsageException(Loc.F(Loc.T.CliNoParameters, op.Id));
            return op;
        }
        var start = 0.0;
        if (trim.Start is not null && !VideoTrim.TryParse(trim.Start, out start))
            throw new UsageException(Loc.F(Loc.T.CliBadTime, trim.Start));
        // «До конца»: заведомо дальше конца любого ролика — ffmpeg остановится на последнем кадре.
        var end = 360000.0;
        if (trim.End is not null && !VideoTrim.TryParse(trim.End, out end))
            throw new UsageException(Loc.F(Loc.T.CliBadTime, trim.End));
        if (end - start < VideoTrim.MinLength) throw new UsageException(Loc.T.CliTrimRange);
        return VideoTrim.Bind(trim.Exact || op.Id == VideoTrim.ExactId, start, end);
    }

    private static string Value(string[] args, ref int i) =>
        ++i < args.Length ? args[i] : throw new UsageException(Loc.F(Loc.T.CliNeedValue, args[i - 1]));

    /// <summary>Убрать «--lang ru|en» из аргументов и сменить язык; неизвестный язык — ошибка использования.</summary>
    private static string[] TakeLanguage(string[] args)
    {
        var index = Array.IndexOf(args, "--lang");
        if (index < 0) return args;
        var i = index;
        var value = Value(args, ref i);
        if (Loc.Normalize(value) == Loc.Auto && !string.Equals(value, Loc.Auto, StringComparison.OrdinalIgnoreCase))
            throw new UsageException(Loc.F(Loc.T.CliUnknownLanguage, value));
        Loc.Current = Loc.Resolve(value);
        return [.. args[..index], .. args[(index + 2)..]];
    }
}

public sealed class UsageException(string message) : Exception(message);
