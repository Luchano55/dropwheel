using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Localization;

namespace DropWheel.Diagnostics;

/// <summary>Что известно о программе и компьютере для отчёта о проблеме.</summary>
/// <param name="Version">Версия для людей («1.3.0», «1.3.0-dev.57»); null — сборка разработчика.</param>
/// <param name="Build">Номер сборки (0 — разработчика).</param>
/// <param name="Channel">Канал обновлений: «stable» / «dev».</param>
/// <param name="Windows">Версия Windows (см. <see cref="ProblemReport.WindowsLine"/>).</param>
/// <param name="Language">Язык интерфейса и Windows: «ru (ru-RU)».</param>
/// <param name="Office">Установленные приложения Office («Word, Excel») или null — нет / неизвестно.</param>
public sealed record ProblemReportInfo(string? Version, int Build, string Channel, string Windows, string Language, string? Office);

/// <summary>
/// «Сообщить о проблеме» (решение D-REPORT): страница новой задачи на GitHub с заполненным текстом — версия, канал,
/// Windows, язык, Office и последние строки сегодняшнего журнала. Личное из журнала убирается (<see cref="Mask"/>):
/// папка профиля «C:\Users\Имя» → «%USERPROFILE%», имя пользователя и компьютера → «&lt;user&gt;» / «&lt;pc&gt;».
/// Имена файлов и остальная структура папок остаются — без них не понять, на каком файле сломалось;
/// человек видит текст на GitHub до отправки и может его поправить. Адрес — не длиннее <see cref="MaxUrlLength"/>:
/// лишнее срезается со старых строк журнала.
/// </summary>
public static partial class ProblemReport
{
    public const string NewIssueUrl = "https://github.com/Luchano55/dropwheel/issues/new";

    /// <summary>GitHub не принимает адрес длиннее ~8 КБ — с запасом.</summary>
    public const int MaxUrlLength = 7000;

    /// <summary>Сколько последних строк журнала брать.</summary>
    public const int LogLines = 40;

    public const string LogFolderHint = @"Win+R → %AppData%\DropWheel\logs";

    // «C:\Users\Имя» и «C:/Users/Имя» — профиль любого пользователя (имя — до разделителя, пробела или кавычки).
    [GeneratedRegex(@"\b[A-Za-z]:[\\/]Users[\\/][^\\/\s""'<>|:*?]+", RegexOptions.IgnoreCase)]
    private static partial Regex AnyProfile();

    /// <summary>
    /// Убрать личное из строки журнала: сначала точная папка профиля (имя может быть с пробелом), затем любые
    /// «X:\Users\кто-то», затем имя пользователя и компьютера отдельными словами (если длиннее двух букв —
    /// короткие совпадали бы с обычными словами).
    /// </summary>
    public static string Mask(string line, string? userProfile, string? userName, string? machineName = null)
    {
        if (string.IsNullOrEmpty(line)) return line;
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var profile = userProfile.TrimEnd('\\', '/');
            line = ReplaceIgnoreCase(line, profile, "%USERPROFILE%");
            line = ReplaceIgnoreCase(line, profile.Replace('\\', '/'), "%USERPROFILE%");
        }
        line = AnyProfile().Replace(line, "%USERPROFILE%");
        line = MaskWord(line, userName, "<user>");
        line = MaskWord(line, machineName, "<pc>");
        return line;
    }

    private static string ReplaceIgnoreCase(string text, string what, string with) =>
        what.Length == 0 ? text : text.Replace(what, with, StringComparison.OrdinalIgnoreCase);

    private static string MaskWord(string text, string? word, string with)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Trim().Length < 3) return text;
        return Regex.Replace(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(word.Trim()) + @"(?![\p{L}\p{N}_])", with,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>«Windows 11 (10.0.22631) · Microsoft Windows 10.0.22631»: Windows 11 — сборка 22000 и новее.</summary>
    public static string WindowsLine(Version os, string description)
    {
        var name = os.Major == 10 && os.Build >= 22000 ? "Windows 11" : os.Major == 10 ? "Windows 10" : "Windows";
        return $"{name} ({os.Major}.{os.Minor}.{os.Build}) · {description.Trim()}";
    }

    /// <summary>Последние <paramref name="count"/> строк файла журнала (файл открыт программой — читаем с общим доступом).</summary>
    public static IReadOnlyList<string> ReadTail(string? path, int count = LogLines)
    {
        if (path is null || !File.Exists(path)) return [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var lines = new Queue<string>(count + 1);
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > count) lines.Dequeue();
            }
            return [.. lines];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Самый свежий журнал в папке (сегодняшний; после полуночи его может ещё не быть — тогда вчерашний).</summary>
    public static string? LatestLog(string directory)
    {
        try
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*.log").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string Title(ProblemReportInfo info) => Loc.F(Loc.T.ReportTitle, info.Version ?? "dev");

    /// <summary>
    /// Текст отчёта: что случилось (заполняет человек), сведения о системе, журнал. <paramref name="log"/> — уже очищенные
    /// строки; <paramref name="cut"/> — журнал обрезан, добавить подсказку, где лежит полный.
    /// </summary>
    public static string Body(ProblemReportInfo info, IReadOnlyList<string> log, bool cut = false)
    {
        var t = Loc.T;
        var sb = new StringBuilder();
        sb.Append("**").Append(t.ReportWhat).Append("**\n\n\n");
        sb.Append("**").Append(t.ReportSteps).Append("**\n\n\n");
        sb.Append("---\n");
        sb.Append("- DropWheel: ").Append(info.Version ?? "dev").Append(", build ").Append(info.Build)
            .Append(", channel ").Append(info.Channel).Append('\n');
        sb.Append("- Windows: ").Append(info.Windows).Append('\n');
        sb.Append("- UI language: ").Append(info.Language).Append('\n');
        sb.Append("- Office: ").Append(info.Office ?? "—").Append('\n');
        sb.Append('\n').Append(t.ReportPrivacy).Append('\n');
        if (log.Count > 0)
        {
            sb.Append("\n<details><summary>").Append(Loc.F(t.ReportLog, log.Count)).Append("</summary>\n\n```\n");
            foreach (var line in log) sb.Append(line.Replace("```", "'''", StringComparison.Ordinal)).Append('\n');
            sb.Append("```\n</details>\n");
        }
        if (cut) sb.Append('\n').Append(Loc.F(t.ReportLogCut, LogFolderHint)).Append('\n');
        return sb.ToString();
    }

    public static string Url(string title, string body) =>
        NewIssueUrl + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(body);

    /// <summary>
    /// Адрес страницы новой задачи и полный текст (для буфера обмена). Журнал очищается от личного;
    /// не помещается в <paramref name="maxLength"/> — срезаются самые старые строки и добавляется подсказка о папке журнала.
    /// </summary>
    public static (string Url, string FullText) Build(ProblemReportInfo info, IEnumerable<string> logLines,
        string? userProfile, string? userName, string? machineName = null, int maxLength = MaxUrlLength)
    {
        var log = logLines.TakeLast(LogLines).Select(l => Mask(l, userProfile, userName, machineName)).ToList();
        var title = Title(info);
        var full = title + "\n\n" + Body(info, log);
        var url = Url(title, Body(info, log));
        if (url.Length <= maxLength) return (url, full);
        // Сколько строк помещается — двоичным поиском (кодирование длинное, перебор по одной — лишняя работа).
        int lo = 0, hi = log.Count - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (Url(title, Body(info, log.GetRange(log.Count - mid, mid), cut: true)).Length <= maxLength) lo = mid;
            else hi = mid - 1;
        }
        return (Url(title, Body(info, log.GetRange(log.Count - lo, lo), cut: true)), full);
    }
}
