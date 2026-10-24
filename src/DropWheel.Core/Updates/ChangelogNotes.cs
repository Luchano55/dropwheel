using System.Text;
using System.Text.RegularExpressions;
using DropWheel.Localization;

namespace DropWheel.Updates;

/// <summary>Раздел «Что нового» одной версии: номер («1.2.0») и текст для окна.</summary>
public sealed record ChangelogNote(string Version, string Text);

/// <summary>
/// «Что нового» (D-WHATSNEW): разбор docs/CHANGELOG.md, вшитого в программу. Раздел версии начинается строкой
/// «## X.Y.Z»; в нём сначала русский текст, после строки «**English**» — английский. Вступление до первого
/// раздела пропускается. Markdown превращается в простой текст: «- » → «•», без «**», обратных кавычек и ссылок.
/// </summary>
public static partial class ChangelogNotes
{
    private const string EnglishMarker = "**English**";

    /// <summary>Разделы файла по порядку (как в файле — новые сверху): версия и сырой markdown раздела.</summary>
    public static IReadOnlyList<(string Version, string Body)> Parse(string markdown)
    {
        var result = new List<(string, string)>();
        string? version = null;
        var body = new StringBuilder();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (version is not null) result.Add((version, body.ToString()));
                version = line[3..].Trim();
                body.Clear();
            }
            else if (version is not null)
            {
                body.Append(line).Append('\n');
            }
        }
        if (version is not null) result.Add((version, body.ToString()));
        return result;
    }

    /// <summary>«1.2.0-dev.57» → «1.2.0», «1.2.0+abc» → «1.2.0»; null — не версия.</summary>
    public static Version? BaseVersion(string? display)
    {
        if (string.IsNullOrWhiteSpace(display)) return null;
        var s = display.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        return Version.TryParse(s, out var v) ? Normalize(v) : null;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>
    /// Разделы для окна «Что нового»: новее <paramref name="previous"/> и не новее <paramref name="current"/>
    /// (новые сверху). Прежняя версия неизвестна или та же (тестовые сборки одной версии) — только раздел текущей.
    /// <paramref name="current"/> null (сборка разработчика) — самый верхний раздел. Нет ни одного раздела — пусто.
    /// </summary>
    public static IReadOnlyList<ChangelogNote> Select(string markdown, string? current, string? previous, UiLanguage language)
    {
        var sections = Parse(markdown)
            .Select(s => (s.Version, s.Body, Number: BaseVersion(s.Version)))
            .Where(s => s.Number is not null)
            .ToList();
        var cur = BaseVersion(current);
        var prev = BaseVersion(previous);

        List<(string Version, string Body, Version? Number)> picked;
        if (cur is null)
            picked = sections.Take(1).ToList();
        else
        {
            picked = prev is not null && prev < cur
                ? sections.Where(s => s.Number > prev && s.Number <= cur).ToList()
                : [];
            if (picked.Count == 0) picked = sections.Where(s => s.Number == cur).ToList();
        }

        return picked
            .Select(s => new ChangelogNote(s.Number!.ToString(3), ToPlainText(Part(s.Body, language))))
            .Where(n => n.Text.Length > 0)
            .ToList();
    }

    /// <summary>Русская часть — до «**English**», английская — после (нет английской — русская).</summary>
    public static string Part(string body, UiLanguage language)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n');
        var marker = Array.FindIndex(lines, l => l.Trim() == EnglishMarker);
        if (marker < 0) return body;
        return language == UiLanguage.En
            ? string.Join('\n', lines.Skip(marker + 1))
            : string.Join('\n', lines.Take(marker));
    }

    /// <summary>
    /// Markdown → текст: пункты «- » → «• », строки-продолжения (с отступом) склеиваются с пунктом,
    /// абзацы разделены пустой строкой; «**жирный**», `код` и [ссылка](адрес) → просто текст.
    /// </summary>
    public static string ToPlainText(string markdown)
    {
        var lines = new List<string>();
        StringBuilder? block = null;
        var gap = false;

        void Start(string text)
        {
            End();
            if (gap && lines.Count > 0) lines.Add("");
            gap = false;
            block = new StringBuilder(text);
        }

        void End()
        {
            if (block is not null) lines.Add(block.ToString());
            block = null;
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Trim().Length == 0)
            {
                End();
                gap = true;
            }
            else if (IsHeading(raw))
            {
                Start(Inline(raw.Trim()));
                End();
            }
            else if (BulletRegex().Match(raw) is { Success: true } bullet)
            {
                Start((bullet.Groups[1].Value.Length >= 2 ? "    • " : "• ") + Inline(raw[bullet.Length..].Trim()));
            }
            else if (block is not null)
            {
                block.Append(' ').Append(Inline(raw.Trim()));
            }
            else
            {
                Start(Inline(raw.Trim()));
            }
        }
        End();
        return string.Join('\n', lines).Trim();
    }

    private static bool IsHeading(string line)
    {
        var t = line.Trim();
        return t.StartsWith('#') || t.Length > 4 && t.StartsWith("**", StringComparison.Ordinal) && t.EndsWith("**", StringComparison.Ordinal);
    }

    private static string Inline(string text)
    {
        text = text.TrimStart('#').Trim();
        text = LinkRegex().Replace(text, "$1");
        text = text.Replace("**", "").Replace("`", "");
        return text;
    }

    [GeneratedRegex(@"^(\s*)[-*] ")]
    private static partial Regex BulletRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex LinkRegex();
}

/// <summary>Когда показать окно «Что нового» при запуске (D-WHATSNEW).</summary>
public static class WhatsNew
{
    /// <summary>
    /// Сборка разработчика (версии нет) — никогда. Первый запуск (прежней версии в настройках нет) — только сразу после
    /// самообновления (настройки старой программы поля ещё не знали). Иначе — когда версия сменилась.
    /// </summary>
    public static bool ShouldShow(string? lastSeen, string? current, bool afterUpdate)
    {
        if (string.IsNullOrWhiteSpace(current)) return false;
        if (string.IsNullOrWhiteSpace(lastSeen)) return afterUpdate;
        return !string.Equals(lastSeen.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
