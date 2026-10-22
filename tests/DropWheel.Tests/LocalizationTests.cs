using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Input;
using DropWheel.Localization;
using DropWheel.Shell;
using DropWheel.Wheel;

namespace DropWheel.Tests;

/// <summary>
/// Тесты, которые меняют язык интерфейса (Loc.Current — общий для процесса), идут в этой коллекции:
/// xUnit запускает её отдельно, после остальных, — русские тесты не видят чужой английский.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LanguageCollection
{
    public const string Name = "Язык интерфейса";
}

/// <summary>Строки на двух языках: полнота перевода, подстановки, склонение, размеры, подписи операций.</summary>
[Collection(LanguageCollection.Name)]
public partial class LocalizationTests
{
    [GeneratedRegex("[А-Яа-яЁё]")]
    private static partial Regex Cyrillic();

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    private static IEnumerable<PropertyInfo> TextProperties() =>
        typeof(Texts).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    private static TheoryData<string> Data(IEnumerable<string> values)
    {
        var data = new TheoryData<string>();
        foreach (var value in values) data.Add(value);
        return data;
    }

    public static TheoryData<string> StringKeys() =>
        Data(TextProperties().Where(p => p.PropertyType == typeof(string)).Select(p => p.Name));

    public static TheoryData<string> NounKeys() =>
        Data(TextProperties().Where(p => p.PropertyType == typeof(Noun)).Select(p => p.Name));

    [Fact]
    public void Texts_have_only_strings_and_nouns()
    {
        Assert.All(TextProperties(), p => Assert.True(p.PropertyType == typeof(string) || p.PropertyType == typeof(Noun), p.Name));
        Assert.True(TextProperties().Count() > 100);
    }

    /// <summary>У каждой русской строки есть английская: не пустая, без кириллицы, с теми же подстановками.</summary>
    [Theory]
    [MemberData(nameof(StringKeys))]
    public void Every_russian_string_has_english_counterpart(string key)
    {
        var property = typeof(Texts).GetProperty(key)!;
        var ru = (string)property.GetValue(Texts.Ru)!;
        var en = (string)property.GetValue(Texts.En)!;
        Assert.False(string.IsNullOrWhiteSpace(ru), key);
        Assert.False(string.IsNullOrWhiteSpace(en), key);
        Assert.DoesNotMatch(Cyrillic(), en);
        if (Cyrillic().IsMatch(ru)) Assert.NotEqual(ru, en);
        Assert.Equal(Placeholders(ru), Placeholders(en));
        // Шаблон должен форматироваться (нет непарных фигурных скобок).
        var args = Enumerable.Range(0, 5).Select(i => (object)$"<{i}>").ToArray();
        _ = Loc.F(ru, args);
        _ = Loc.F(en, args);
    }

    private static int[] Placeholders(string text) =>
        Placeholder().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).Distinct().Order().ToArray();

    [Theory]
    [MemberData(nameof(NounKeys))]
    public void Every_russian_noun_has_english_counterpart(string key)
    {
        var property = typeof(Texts).GetProperty(key)!;
        var ru = (Noun)property.GetValue(Texts.Ru)!;
        var en = (Noun)property.GetValue(Texts.En)!;
        Assert.False(ru.English);
        Assert.True(en.English);
        Assert.All(new[] { en.One, en.Few, en.Many }, w => Assert.DoesNotMatch(Cyrillic(), w));
        Assert.All(new[] { ru.One, ru.Few, ru.Many }, w => Assert.Matches(Cyrillic(), w));
    }

    [Fact]
    public void Default_language_is_russian_and_T_follows_current()
    {
        Assert.Equal(UiLanguage.Ru, Loc.Current);
        Assert.Same(Texts.Ru, Loc.T);
        using (Loc.Use(UiLanguage.En))
        {
            Assert.Same(Texts.En, Loc.T);
            Assert.Equal("Space", Hotkeys.DisplayName("Space"));
            Assert.Equal("Convert…", ContextMenuRegistration.MenuText);
        }
        Assert.Equal(UiLanguage.Ru, Loc.Current);
        Assert.Equal("Пробел", Hotkeys.DisplayName("Space"));
        Assert.Equal("Конвертировать…", ContextMenuRegistration.MenuText);
    }

    // ---------- язык по настройке и Windows ----------

    [Theory]
    [InlineData("auto", "ru-RU", UiLanguage.Ru)]
    [InlineData("auto", "en-US", UiLanguage.En)]
    [InlineData("auto", "de-DE", UiLanguage.En)]
    [InlineData("auto", "uk-UA", UiLanguage.En)]
    [InlineData("auto", "be-BY", UiLanguage.En)]
    [InlineData("auto", "kk-KZ", UiLanguage.En)]
    [InlineData(null, "ru-RU", UiLanguage.Ru)]
    [InlineData("что-то", "en-GB", UiLanguage.En)]
    [InlineData("ru", "en-US", UiLanguage.Ru)]
    [InlineData("RU", "de-DE", UiLanguage.Ru)]
    [InlineData("en", "ru-RU", UiLanguage.En)]
    [InlineData(" En ", "ru-RU", UiLanguage.En)]
    public void Resolve_follows_setting_then_windows_language(string? setting, string culture, UiLanguage expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("Auto", "auto")]
    [InlineData("de", "auto")]
    [InlineData("ru", "ru")]
    [InlineData("English", "en")]
    public void Normalize_setting(string? value, string expected) => Assert.Equal(expected, Loc.Normalize(value));

    // ---------- склонение и размеры ----------

    [Theory]
    [InlineData(0, "0 файлов", "0 files")]
    [InlineData(1, "1 файл", "1 file")]
    [InlineData(2, "2 файла", "2 files")]
    [InlineData(5, "5 файлов", "5 files")]
    [InlineData(11, "11 файлов", "11 files")]
    [InlineData(21, "21 файл", "21 files")]
    [InlineData(102, "102 файла", "102 files")]
    public void Count_declines_in_both_languages(long n, string ru, string en)
    {
        Assert.Equal(ru, Loc.Count(n, Texts.Ru.Files));
        Assert.Equal(en, Loc.Count(n, Texts.En.Files));
    }

    [Theory]
    [InlineData(1, "1 ошибка", "1 error")]
    [InlineData(3, "3 ошибки", "3 errors")]
    public void Errors_noun(long n, string ru, string en)
    {
        Assert.Equal(ru, Loc.Count(n, Texts.Ru.Errors));
        Assert.Equal(en, Loc.Count(n, Texts.En.Errors));
    }

    [Theory]
    [InlineData(0L, "0 B", "0 Б")]
    [InlineData(812L, "812 B", "812 Б")]
    [InlineData(64L * 1024, "64 KB", "64 КБ")]
    [InlineData(1153434L, "1.1 MB", "1,1 МБ")]
    [InlineData(12L * 1024 * 1024, "12 MB", "12 МБ")]
    [InlineData(1048166L, "1 MB", "1 МБ")]
    [InlineData(1000L * 1024, "1,000 KB", "1 000 КБ")]
    [InlineData(3650722201L, "3.4 GB", "3,4 ГБ")]
    public void File_size_in_both_languages(long bytes, string en, string ru)
    {
        Assert.Equal(en, EnglishText.FileSize(bytes).Replace(' ', ' '));
        using (Loc.Use(UiLanguage.En)) Assert.Equal(en, Loc.FileSize(bytes).Replace(' ', ' '));
        using (Loc.Use(UiLanguage.Ru)) Assert.Equal(ru, Loc.FileSize(bytes).Replace(' ', ' '));
    }

    // ---------- операции каталога ----------

    public static TheoryData<string> OperationIds() => Data(OperationCatalog.All.Select(o => o.Id));

    [Theory]
    [MemberData(nameof(OperationIds))]
    public void Every_operation_has_label_and_suffix_in_both_languages(string id)
    {
        var op = OperationCatalog.Find(id)!;
        Assert.Equal(op.Label, Loc.Label(op, UiLanguage.Ru));
        Assert.Equal(op.Suffix, Loc.Suffix(op, UiLanguage.Ru));

        var label = Loc.Label(op, UiLanguage.En);
        var suffix = Loc.Suffix(op, UiLanguage.En);
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.True(label.Length <= 16, label); // помещается в лепесток колеса
        Assert.DoesNotMatch(Cyrillic(), label);
        Assert.DoesNotMatch(Cyrillic(), suffix);
        // Пометка либо пустая, либо « (…)» — как у русской.
        Assert.Equal(op.Suffix.Length == 0, suffix.Length == 0);
        if (suffix.Length > 0) Assert.Matches(@"^ \(.+\)$", suffix);
    }

    [Fact]
    public void English_tables_name_only_existing_operations()
    {
        var ids = OperationCatalog.All.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(OperationTexts.EnglishLabels.Keys, id => Assert.Contains(id, ids));
        Assert.All(OperationTexts.EnglishSuffixes.Keys, id => Assert.Contains(id, ids));
    }

    [Theory]
    [InlineData("img-compress", "Compress", " (compressed)")]
    [InlineData("img-half", "Half size", " (50%)")]
    [InlineData("img-1920", "Fit 1920 px", " (1920)")]
    [InlineData("img-strip", "No metadata", " (no metadata)")]
    [InlineData("video-mute", "Mute", " (muted)")]
    [InlineData("video-720p", "720p", " (720p)")]
    [InlineData("video-subs", "Subtitles SRT", "")]
    [InlineData("audio-normalize", "Normalize", " (normalized)")]
    [InlineData("pdf-to-png", "PNG (pages)", " (pages)")]
    [InlineData("pdf-merge", "Merge", " (merged)")]
    [InlineData("pdf-split", "Split", " (split)")]
    [InlineData("pdf-optimize", "Optimize", " (optimized)")]
    [InlineData("pdf-rotate", "Rotate ↻", " (rotated)")]
    [InlineData("images-to-pdf", "One PDF", " (all)")]
    [InlineData("zip-pack", "To ZIP", "")]
    [InlineData("pack-7z", "To 7Z", "")]
    [InlineData("zip-extract", "Extract", "")]
    [InlineData("img-ocr", "Text (OCR)", "")]
    [InlineData("img-ico", "ICO (icon)", "")]
    [InlineData("text-to-utf8", "To UTF-8", " (UTF-8)")]
    [InlineData("ppt-to-png", "Slides PNG", " (slides)")]
    [InlineData("to-jpg", "JPG", "")]
    public void English_labels_and_suffixes(string id, string label, string suffix)
    {
        var op = OperationCatalog.Find(id)!;
        Assert.Equal((label, suffix), (Loc.Label(op, UiLanguage.En), Loc.Suffix(op, UiLanguage.En)));
    }

    [Fact]
    public void Wheel_items_use_current_language()
    {
        var ops = OperationCatalog.ForFiles(["photo.heic"]);
        Assert.Equal("Сжать", WheelItems.For(ops, OperationMode.Tool)[0].Label);
        using (Loc.Use(UiLanguage.En))
        {
            Assert.Equal("Compress", WheelItems.For(ops, OperationMode.Tool)[0].Label);
            Assert.Equal("JPG", WheelItems.For(ops, OperationMode.Convert)[0].Label);
            var (_, tools) = WheelArrangement.Arrange(ops, WheelGroups.Photo, new WheelPreferences());
            Assert.Equal("Compress", tools[0].Label);
        }
    }

    // ---------- ошибки ----------

    [Theory]
    [InlineData(EngineKind.FFmpeg, "x: No such file or directory", "Файл не найден", "File not found")]
    [InlineData(EngineKind.FFmpeg, "moov atom not found", "Видео повреждено или не докачано", "The video is damaged or incomplete")]
    [InlineData(EngineKind.SevenZip, "Wrong password?", "Архив с паролем не поддерживается", "Password-protected archives are not supported")]
    [InlineData(EngineKind.Qpdf, "что-то непонятное", "qpdf не смог обработать PDF", "qpdf couldn't process the PDF")]
    [InlineData(EngineKind.Zip, "", "Не удалось обработать файл", "Couldn't process the file")]
    public void Error_translator_speaks_current_language(EngineKind engine, string stderr, string ru, string en)
    {
        using (Loc.Use(UiLanguage.Ru)) Assert.Equal(ru, ErrorTranslator.Translate(engine, stderr));
        using (Loc.Use(UiLanguage.En)) Assert.Equal(en, ErrorTranslator.Translate(engine, stderr));
    }

    [Fact]
    public void Arguments_name_pdf_pages_in_current_language()
    {
        var op = OperationCatalog.Find("pdf-split")!;
        var output = Path.Combine(Path.GetTempPath(), "out");
        Assert.Equal(Path.Combine(output, "стр.pdf"), ArgumentBuilder.Build(op, "a.pdf", output, ConversionSettings.Default)[^1]);
        using (Loc.Use(UiLanguage.En))
            Assert.Equal(Path.Combine(output, "page.pdf"), ArgumentBuilder.Build(op, "a.pdf", output, ConversionSettings.Default)[^1]);
        using (Loc.Use(UiLanguage.En)) Assert.Equal("slide-01.png", OfficeFormats.SlideFileName(1, 12));
    }
}
