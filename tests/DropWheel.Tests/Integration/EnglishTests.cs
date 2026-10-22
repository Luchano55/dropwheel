using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;

namespace DropWheel.Tests.Integration;

/// <summary>
/// Английский интерфейс в деле: имена результатов, причины ошибок, CLI с --lang.
/// Меняет Loc.Current — поэтому в коллекции «Язык интерфейса» (не параллельно с русскими тестами).
/// </summary>
[Collection(LanguageCollection.Name)]
public class EnglishTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly IDisposable _language = Loc.Use(UiLanguage.En);
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose()
    {
        _language.Dispose();
        _dir.Dispose();
    }

    private string Pdf(string name = "test 1.pdf")
    {
        var p = Path.Combine(_dir.Path, name);
        File.Copy(Tools.Fixture("sample-3pages.pdf"), p);
        return p;
    }

    [SkippableFact]
    public async Task Result_names_use_english_suffix()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var output = await _converter.ConvertAsync(Pdf(), OperationCatalog.Find("pdf-optimize")!);
        Assert.Equal(Path.Combine(_dir.Path, "test 1 (optimized).pdf"), output);
    }

    [SkippableFact]
    public async Task Split_pages_are_named_in_english()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var output = await _converter.ConvertAsync(Pdf(), OperationCatalog.Find("pdf-split")!);
        Assert.Equal(Path.Combine(_dir.Path, "test 1 (split)"), output);
        Assert.Equal(["page-1.pdf", "page-2.pdf", "page-3.pdf"],
            Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Missing_file_error_is_english()
    {
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync(Path.Combine(_dir.Path, "no such.png"), OperationCatalog.Find("to-jpg")!));
        Assert.Equal("File not found", ex.Message);
    }

    [Fact]
    public async Task Min_files_error_names_english_label()
    {
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            _converter.ConvertAsync([Pdf()], OperationCatalog.Find("pdf-merge")!));
        Assert.Equal("“Merge” needs at least 2 files", ex.Message);
    }

    // ---------- CLI ----------

    [Theory]
    [InlineData("en", "To ZIP")]
    [InlineData("ru", "В ZIP")]
    public async Task Cli_lang_switches_labels(string lang, string label)
    {
        var stdout = new StringWriter();
        var code = await Program.RunAsync(["list", "data.bin", "--lang", lang], stdout, new StringWriter());
        Assert.Equal(0, code);
        var zip = stdout.ToString().Split('\n').Single(l => l.StartsWith("zip-pack"));
        Assert.EndsWith(label, zip.Trim());
    }

    [Fact]
    public async Task Cli_lang_before_command()
    {
        var stdout = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(["--lang", "en", "--help"], stdout, new StringWriter()));
        Assert.StartsWith("Usage:", stdout.ToString().Trim());
    }

    [Fact]
    public async Task Cli_errors_in_english()
    {
        var stderr = new StringWriter();
        var code = await Program.RunAsync(["--lang", "en", "convert", Path.Combine(_dir.Path, "no such.png"), "to-jpg"],
            new StringWriter(), stderr);
        Assert.Equal(1, code);
        Assert.Contains("Error: ", stderr.ToString());
        Assert.Contains("File not found", stderr.ToString());

        var usage = await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.png", "to-nothing", "--lang", "en"], new StringWriter(), new StringWriter()));
        Assert.Equal("Unknown operation: to-nothing", usage.Message);
    }

    [Theory]
    [InlineData("de")]
    [InlineData(null)]
    public async Task Cli_unknown_or_missing_language_is_usage_error(string? lang)
    {
        string[] args = lang is null ? ["list", "--lang"] : ["list", "--lang", lang];
        await Assert.ThrowsAsync<UsageException>(() => Program.RunAsync(args, new StringWriter(), new StringWriter()));
    }
}
