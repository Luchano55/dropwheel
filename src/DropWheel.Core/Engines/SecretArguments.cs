using System.Text;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// Секреты задачи (пароль PDF, решение D-PDFPASS) — подальше от журнала и командной строки.
/// <list type="bullet">
/// <item><see cref="Mask"/> — в строках журнала и подробностях ошибки значение заменяется на «••••••».</item>
/// <item><see cref="MoveToFiles"/> — qpdf: каждый аргумент с секретом пишется во временный файл (одна строка, UTF-8),
/// вместо аргумента — «@файл» (qpdf читает аргументы из файла). Так пароля нет в командной строке процесса,
/// которую видят другие программы. Файл — в скрытой временной папке результата, удаляется сразу после запуска.</item>
/// </list>
/// </summary>
public static class SecretArguments
{
    public const string Masked = "••••••";

    /// <summary>Утилиты, которые умеют читать аргументы из файла «@…».</summary>
    public static bool SupportsArgFiles(EngineKind engine) => engine == EngineKind.Qpdf;

    public static string Mask(string? text, IReadOnlyCollection<string> secrets)
    {
        if (string.IsNullOrEmpty(text) || secrets.Count == 0) return text ?? "";
        // Длинные — первыми: секрет, который входит в другой, не оставит хвост.
        foreach (var secret in secrets.Where(s => s.Length > 0).OrderByDescending(s => s.Length))
            text = text.Replace(secret, Masked, StringComparison.Ordinal);
        return text;
    }

    /// <summary>Журнал, в котором секреты замаскированы; null — без журнала.</summary>
    public static Action<string>? MaskLog(Action<string>? log, IReadOnlyCollection<string> secrets) =>
        log is null || secrets.Count == 0 ? log : line => log(Mask(line, secrets));

    /// <summary>
    /// Аргументы с секретом → файлы «@…» в папке <paramref name="dir"/>. Созданные файлы добавляются в
    /// <paramref name="created"/> — их удаляет вызывающий (в finally), даже если запуск не удался.
    /// </summary>
    public static IReadOnlyList<string> MoveToFiles(IReadOnlyList<string> args, IReadOnlyCollection<string> secrets,
        string dir, ICollection<string> created)
    {
        if (secrets.Count == 0) return args;
        var result = new List<string>(args.Count);
        foreach (var arg in args)
        {
            if (!secrets.Any(s => s.Length > 0 && arg.Contains(s, StringComparison.Ordinal)))
            {
                result.Add(arg);
                continue;
            }
            if (arg.IndexOfAny(['\r', '\n']) >= 0)
                throw new ArgumentException("Аргумент с секретом — в несколько строк", nameof(args));
            var file = Path.Combine(dir, $".dropwheel-{Guid.NewGuid():N}.arg");
            created.Add(file);
            File.WriteAllText(file, arg + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            result.Add("@" + file);
        }
        return result;
    }

    /// <summary>Удалить временные файлы с секретами (ошибки — не критично: папка всё равно скрытая и временная).</summary>
    public static void Delete(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* попробуем вместе с папкой */ }
        }
    }
}
