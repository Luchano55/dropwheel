using System.Globalization;
using DropWheel.Conversions;

namespace DropWheel.Localization;

/// <summary>Язык интерфейса.</summary>
public enum UiLanguage { Ru, En }

/// <summary>
/// Текущий язык интерфейса и всё, что от него зависит: строки (<see cref="T"/>), подписи и пометки операций,
/// склонение по числу, размер файла (решение D-LANG).
/// По умолчанию — русский: ядро и тесты не зависят от языка машины. Язык выбирают точки входа
/// (DropWheel.exe — по настройке и языку Windows, CLI — по языку системы или --lang).
/// </summary>
public static class Loc
{
    /// <summary>Значения настройки Language в settings.json.</summary>
    public const string Auto = "auto", Russian = "ru", English = "en";

    private static volatile int _current = (int)UiLanguage.Ru;

    public static UiLanguage Current
    {
        get => (UiLanguage)_current;
        set => _current = (int)value;
    }

    /// <summary>Строки на текущем языке.</summary>
    public static Texts T => For(Current);

    public static Texts For(UiLanguage language) => language == UiLanguage.En ? Texts.En : Texts.Ru;

    /// <summary>
    /// Язык по настройке: «ru» / «en» — как сказано; «auto» (и всё прочее) — по языку Windows:
    /// русский только для русской Windows, остальным — английский.
    /// </summary>
    public static UiLanguage Resolve(string? setting, CultureInfo? uiCulture = null)
    {
        switch (Normalize(setting))
        {
            case Russian: return UiLanguage.Ru;
            case English: return UiLanguage.En;
        }
        var culture = uiCulture ?? CultureInfo.CurrentUICulture;
        return string.Equals(culture.TwoLetterISOLanguageName, "ru", StringComparison.OrdinalIgnoreCase)
            ? UiLanguage.Ru
            : UiLanguage.En;
    }

    /// <summary>Значение настройки: «ru», «en» или «auto» (неизвестное и пустое → «auto»).</summary>
    public static string Normalize(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        "ru" or "rus" or "russian" => Russian,
        "en" or "eng" or "english" => English,
        _ => Auto,
    };

    /// <summary>Временно сменить язык (тесты): язык возвращается при Dispose.</summary>
    public static IDisposable Use(UiLanguage language)
    {
        var previous = Current;
        Current = language;
        return new Restore(previous);
    }

    private sealed class Restore(UiLanguage previous) : IDisposable
    {
        public void Dispose() => Current = previous;
    }

    /// <summary>Строка-шаблон с подстановкой ({0}, {1}…); числа — без разделителей тысяч и культуры.</summary>
    public static string F(string format, params object?[] args) =>
        string.Format(CultureInfo.InvariantCulture, format, args);

    /// <summary>«3 файла» / «3 files».</summary>
    public static string Count(long n, Noun noun) => $"{n} {noun.For(n)}";

    /// <summary>Размер как в проводнике: «1,1 МБ» по-русски, «1.1 MB» по-английски.</summary>
    public static string FileSize(long bytes) =>
        Current == UiLanguage.En ? EnglishText.FileSize(bytes) : Localization.Russian.FileSize(bytes);

    /// <summary>Название языка на нём самом — для выбора языка в настройках.</summary>
    public static string NativeName(UiLanguage language) => language == UiLanguage.En ? "English" : "Русский";

    // ---------- операции каталога ----------

    /// <summary>Подпись операции на текущем языке (в каталоге — русская; названия форматов одинаковы).</summary>
    public static string Label(Operation op) => Label(op, Current);

    public static string Label(Operation op, UiLanguage language) =>
        language == UiLanguage.En && OperationTexts.EnglishLabels.TryGetValue(op.Id, out var label) ? label : op.Label;

    /// <summary>Пометка в имени результата: « (сжато)» / « (compressed)».</summary>
    public static string Suffix(Operation op) => Suffix(op, Current);

    public static string Suffix(Operation op, UiLanguage language) =>
        language == UiLanguage.En && OperationTexts.EnglishSuffixes.TryGetValue(op.Id, out var suffix) ? suffix : op.Suffix;
}

/// <summary>Слово, которое склоняется по числу: по-русски три формы, по-английски две.</summary>
public sealed record Noun(string One, string Few, string Many, bool English = false)
{
    public static Noun En(string one, string other) => new(one, other, other, English: true);

    public string For(long n) => English
        ? (Math.Abs(n) == 1 ? One : Many)
        : Localization.Russian.Plural(n, One, Few, Many);
}
