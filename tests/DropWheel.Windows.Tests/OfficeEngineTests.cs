using System.Diagnostics;
using System.Text;
using DropWheel.AppEngines;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using Xunit.Abstractions;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Настоящий Microsoft Office через COM. В CI (windows-latest) Office нет — тесты пропускаются;
/// на ПК с Office — работают. Время каждой конвертации пишется в вывод теста.
/// Папка — с кириллицей и пробелом.
/// </summary>
public sealed class OfficeEngineTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _dir;
    private readonly OfficeEngine _engine;
    private readonly Converter _converter;

    public OfficeEngineTests(ITestOutputHelper output)
    {
        _output = output;
        _dir = Path.Combine(Path.GetTempPath(), "dropwheel-тесты", Guid.NewGuid().ToString("N")[..8], "Мои документы");
        Directory.CreateDirectory(_dir);
        _engine = new OfficeEngine(Log);
        _converter = new Converter(new ToolLocator(), ConversionSettings.Default, [_engine]);
    }

    public void Dispose()
    {
        _engine.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); }
        catch { /* не важно */ }
    }

    private void Log(string message)
    {
        try { _output.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {message}"); }
        catch { /* тест уже закончился */ }
    }

    private void SkipWithout(OfficeApp app) => Skip.IfNot(_engine.IsInstalled(app), $"Не установлен Microsoft {app}");

    private string In(string name) => Path.Combine(_dir, name);

    private async Task<string> Convert(string input, string opId)
    {
        var watch = Stopwatch.StartNew();
        var result = await _converter.ConvertAsync(input, OperationCatalog.Find(opId)!);
        Log($"{opId}: {watch.ElapsedMilliseconds} мс → {Path.GetFileName(result)}");
        return result;
    }

    private static string Header(string path, int count = 4)
    {
        var bytes = new byte[count];
        using var file = File.OpenRead(path);
        file.ReadExactly(bytes);
        return Encoding.ASCII.GetString(bytes);
    }

    private void AssertNoTempLeft() =>
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(_dir), p => Path.GetFileName(p).StartsWith(".dropwheel-"));

    [SkippableFact]
    public async Task Word_document_to_every_format()
    {
        SkipWithout(OfficeApp.Word);
        var docx = OfficeSamples.Word(In("тест 1.docx"), "Привет, мир! Проверка DropWheel.");

        foreach (var (op, ext) in new[] { ("word-to-pdf", "pdf"), ("word-to-doc", "doc"), ("word-to-odt", "odt"),
                     ("word-to-rtf", "rtf"), ("word-to-txt", "txt") })
        {
            var result = await Convert(docx, op);
            Assert.Equal(In("тест 1." + ext), result);
            Assert.True(new FileInfo(result).Length > 0, result);
        }

        Assert.Equal("%PDF", Header(In("тест 1.pdf")));
        Assert.Equal("{\\rt", Header(In("тест 1.rtf")));
        var txt = File.ReadAllBytes(In("тест 1.txt"));
        Assert.False(txt.Length >= 2 && txt[0] == 0xFF && txt[1] == 0xFE, "TXT должен быть в UTF-8, а не UTF-16");
        Assert.Contains("Привет, мир!", Encoding.UTF8.GetString(txt));

        // DOC → DOCX обратно.
        File.Delete(docx);
        Assert.Equal(In("тест 1.docx"), await Convert(In("тест 1.doc"), "word-to-docx"));
        Assert.Equal("PK", Header(In("тест 1.docx"), 2));
        AssertNoTempLeft();
    }

    [SkippableFact]
    public async Task Pdf_to_word()
    {
        SkipWithout(OfficeApp.Word);
        var docx = OfficeSamples.Word(In("скан.docx"), "Текст, который Word должен вытащить из PDF.");
        var pdf = await Convert(docx, "word-to-pdf");
        File.Delete(docx);

        // Первый раз Word может перезапуститься (отключаем его вопрос о преобразовании PDF).
        var result = await Convert(pdf, "pdf-to-docx");
        Assert.Equal(In("скан.docx"), result);
        Assert.Equal("PK", Header(result, 2));
    }

    [SkippableFact]
    public async Task Excel_workbook_to_every_format()
    {
        SkipWithout(OfficeApp.Excel);
        var xlsx = OfficeSamples.Excel(In("таблица 1.xlsx"), [["Имя", "Сумма"], ["Анна", "10"], ["Борис", "20"]]);

        foreach (var (op, ext) in new[] { ("excel-to-pdf", "pdf"), ("excel-to-xls", "xls"), ("excel-to-ods", "ods"),
                     ("excel-to-csv", "csv") })
            Assert.Equal(In("таблица 1." + ext), await Convert(xlsx, op));

        Assert.Equal("%PDF", Header(In("таблица 1.pdf")));
        var csv = File.ReadAllText(In("таблица 1.csv"), Encoding.UTF8);
        Log("CSV:\n" + csv);
        Assert.Contains("Анна", csv);
        Assert.DoesNotContain("второй лист", csv); // CSV — первый лист, хотя книга сохранена со вторым активным
        AssertNoTempLeft();
    }

    [SkippableFact]
    public async Task PowerPoint_to_pdf_and_slides()
    {
        SkipWithout(OfficeApp.PowerPoint);
        var pptx = OfficeSamples.PowerPoint(In("доклад 1.pptx"), "Первый слайд", "Второй слайд");

        Assert.Equal(In("доклад 1.pdf"), await Convert(pptx, "ppt-to-pdf"));
        Assert.Equal("%PDF", Header(In("доклад 1.pdf")));
        Assert.Equal(In("доклад 1.odp"), await Convert(pptx, "ppt-to-odp"));
        Assert.Equal(In("доклад 1.ppt"), await Convert(pptx, "ppt-to-ppt"));

        var folder = await Convert(pptx, "ppt-to-png");
        Assert.Equal(In("доклад 1 (слайды)"), folder);
        Assert.Equal(["слайд-1.png", "слайд-2.png"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        var png = File.ReadAllBytes(Path.Combine(folder, "слайд-1.png"));
        Assert.Equal("PNG", Encoding.ASCII.GetString(png, 1, 3));
        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]; // IHDR
        Assert.Equal(1920, width);
        AssertNoTempLeft();
    }

    [SkippableFact]
    public async Task Password_protected_documents_fail_quickly_with_a_clear_message()
    {
        SkipWithout(OfficeApp.Word);
        var watch = Stopwatch.StartNew();
        // DOCX с паролем — узнаём ещё до Office; DOC с паролем — Word отвечает ошибкой на пароль-заглушку.
        foreach (var (name, format) in new[] { ("секрет.docx", 16), ("секрет.doc", 0) })
        {
            var file = OfficeSamples.Word(In(name), "тайна", password: "1234", format: format);
            var ex = await Assert.ThrowsAsync<ConversionException>(() => Convert(file, "word-to-pdf"));
            Log($"{name}: {ex.Message} ({ex.Details})");
            Assert.Equal("Документ защищён паролем", ex.Message);
        }
        if (_engine.IsInstalled(OfficeApp.Excel))
        {
            var xls = OfficeSamples.Excel(In("секрет.xls"), [["тайна"]], password: "1234", format: 56);
            var ex = await Assert.ThrowsAsync<ConversionException>(() => Convert(xls, "excel-to-pdf"));
            Log($"секрет.xls: {ex.Message} ({ex.Details})");
            Assert.Equal("Документ защищён паролем", ex.Message);
        }
        Assert.True(watch.Elapsed < TimeSpan.FromMinutes(1), "Office не должен ждать ввода пароля");
        AssertNoTempLeft();
    }

    [SkippableFact]
    public async Task Prewarm_does_not_block_and_makes_conversion_fast()
    {
        SkipWithout(OfficeApp.Word);
        var docx = OfficeSamples.Word(In("тест.docx"), "прогрев");

        var watch = Stopwatch.StartNew();
        _engine.Prepare([OperationCatalog.Find("word-to-pdf")!]);
        Assert.True(watch.ElapsedMilliseconds < 500, $"Prepare занял {watch.ElapsedMilliseconds} мс");
        while (!_engine.IsRunning(OfficeApp.Word) && watch.Elapsed < TimeSpan.FromMinutes(1)) await Task.Delay(50);
        Log($"Word прогрет за {watch.ElapsedMilliseconds} мс");
        Assert.True(_engine.IsRunning(OfficeApp.Word));

        var warm = Stopwatch.StartNew();
        await Convert(docx, "word-to-pdf");
        Log($"Конвертация после прогрева: {warm.ElapsedMilliseconds} мс");
    }

    [SkippableFact]
    public async Task Dispose_closes_our_word()
    {
        SkipWithout(OfficeApp.Word);
        var docx = OfficeSamples.Word(In("тест.docx"), "выход");
        await Convert(docx, "word-to-pdf");
        var pid = _engine.ProcessIdOf(OfficeApp.Word);
        Assert.True(pid != 0, "Не удалось узнать процесс Word — по таймауту его нечем будет завершить");

        using var process = Process.GetProcessById(pid);
        _engine.Dispose();
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(20)), "Word не закрылся после выхода из программы");
    }

    [SkippableFact]
    public async Task Dead_word_is_restarted_transparently()
    {
        SkipWithout(OfficeApp.Word);
        var docx = OfficeSamples.Word(In("тест.docx"), "перезапуск");
        await Convert(docx, "word-to-pdf");
        var pid = _engine.ProcessIdOf(OfficeApp.Word);
        Skip.If(pid == 0, "процесс Word неизвестен");

        // Пользователь закрыл Word через диспетчер задач (или Word упал).
        using (var process = Process.GetProcessById(pid))
        {
            process.Kill();
            process.WaitForExit(10_000);
        }

        await Convert(docx, "word-to-rtf");
        Assert.NotEqual(pid, _engine.ProcessIdOf(OfficeApp.Word));
    }

    [SkippableFact]
    public async Task Users_own_word_is_never_touched()
    {
        SkipWithout(OfficeApp.Word);
        var docx = OfficeSamples.Word(In("тест.docx"), "чужой Word");

        // «Пользователь»: свой Word с несохранённым документом.
        dynamic user = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
        dynamic userDoc = user.Documents.Add();
        userDoc.Content.Text = "мой несохранённый текст";
        try
        {
            await Convert(docx, "word-to-pdf");
            _engine.Dispose(); // закрывает только свой Word

            Assert.Equal(1, (int)user.Documents.Count);
            Assert.Contains("мой несохранённый текст", (string)userDoc.Content.Text);
        }
        finally
        {
            userDoc.Close(SaveChanges: 0);
            user.Quit(SaveChanges: 0);
        }
    }
}
