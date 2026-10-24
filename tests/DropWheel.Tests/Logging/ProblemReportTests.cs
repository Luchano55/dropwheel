using DropWheel.Diagnostics;

namespace DropWheel.Tests.Logging;

/// <summary>«Сообщить о проблеме» (D-REPORT): очистка журнала от личного, длина адреса, кодирование.</summary>
public class ProblemReportTests : IDisposable
{
    private const string Profile = @"C:\Users\Роман Л";
    private static readonly ProblemReportInfo Info = new("1.3.0", 321, "stable",
        ProblemReport.WindowsLine(new Version(10, 0, 22631, 0), "Microsoft Windows 10.0.22631"), "ru (ru-RU)", "Word, Excel");

    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(@"Готово: C:\Users\Роман Л\Мои фото\тест 1.jpg", @"Готово: %USERPROFILE%\Мои фото\тест 1.jpg")]
    [InlineData(@"c:\users\роман л\Desktop\a.png", @"%USERPROFILE%\Desktop\a.png")] // регистр не важен
    [InlineData(@"C:/Users/Роман Л/Downloads/x.pdf", @"%USERPROFILE%/Downloads/x.pdf")]
    [InlineData(@"чужой профиль D:\Users\guest\a.txt", @"чужой профиль %USERPROFILE%\a.txt")]
    [InlineData(@"короткое имя C:\Users\9C7A~1\AppData\Local\Temp\x", @"короткое имя %USERPROFILE%\AppData\Local\Temp\x")]
    [InlineData("Пользователь Роман открыл файл", "Пользователь <user> открыл файл")]
    [InlineData("Романов — не имя пользователя", "Романов — не имя пользователя")] // только целым словом
    [InlineData("компьютер DESKTOP-42 готов", "компьютер <pc> готов")]
    [InlineData(@"C:\Program Files\DropWheel\tools\ffmpeg.exe", @"C:\Program Files\DropWheel\tools\ffmpeg.exe")]
    public void Mask_hides_user_and_profile(string line, string expected) =>
        Assert.Equal(expected, ProblemReport.Mask(line, Profile + @"\", "Роман", "DESKTOP-42"));

    [Fact]
    public void Mask_skips_short_names() => Assert.Equal("ok и он", ProblemReport.Mask("ok и он", null, "он"));

    [Theory]
    [InlineData(10, 0, 22631, "Windows 11 (10.0.22631)")]
    [InlineData(10, 0, 19045, "Windows 10 (10.0.19045)")]
    public void Windows_name(int major, int minor, int build, string start) =>
        Assert.StartsWith(start, ProblemReport.WindowsLine(new Version(major, minor, build, 0), " Microsoft Windows "));

    [Fact]
    public void Url_contains_everything_and_is_encoded()
    {
        string[] log = [@"10:00 Готово: C:\Users\Роман Л\Мои фото\тест 1.jpg", "10:01 ffmpeg & co ?x=1#y"];
        var (url, full) = ProblemReport.Build(Info, log, Profile, "Роман");
        Assert.StartsWith("https://github.com/Luchano55/dropwheel/issues/new?title=", url);
        Assert.DoesNotContain(" ", url);
        Assert.DoesNotContain("#", url[url.IndexOf('?')..]);
        Assert.Equal(1, url.Count(c => c == '&')); // только между title и body: «&» из журнала закодирован
        var body = Uri.UnescapeDataString(url[(url.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        Assert.Contains("1.3.0, build 321, channel stable", body);
        Assert.Contains("Windows 11 (10.0.22631)", body);
        Assert.Contains("ru (ru-RU)", body);
        Assert.Contains("Word, Excel", body);
        Assert.Contains(@"%USERPROFILE%\Мои фото\тест 1.jpg", body);
        Assert.Contains("ffmpeg & co ?x=1#y", body);
        Assert.DoesNotContain("Роман", body);
        Assert.DoesNotContain(ProblemReport.LogFolderHint, body);
        Assert.Contains(body, full);
        Assert.DoesNotContain("Роман", full);
    }

    [Fact]
    public void Long_log_is_cut_from_the_oldest_lines()
    {
        var log = Enumerable.Range(1, 100).Select(i => $"строка {i:D3} " + new string('ж', 150)).ToList();
        var (url, full) = ProblemReport.Build(Info, log, Profile, "Роман");
        Assert.True(url.Length <= ProblemReport.MaxUrlLength, url.Length.ToString());
        var body = Uri.UnescapeDataString(url[(url.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        Assert.Contains("строка 100", body); // свежие строки остаются
        Assert.DoesNotContain("строка 061", body); // в адрес влезает лишь несколько последних …
        Assert.Contains("строка 061", full); // … а в буфере обмена — все последние 40
        Assert.DoesNotContain("строка 060", full);
        Assert.Contains(ProblemReport.LogFolderHint, body);
        Assert.DoesNotContain(ProblemReport.LogFolderHint, full);
        // Помещается почти вплотную: ещё одна строка уже не влезла бы.
        Assert.True(url.Length > ProblemReport.MaxUrlLength - 1500);
    }

    [Fact]
    public void No_log_no_details()
    {
        var (url, _) = ProblemReport.Build(Info, [], null, null);
        Assert.DoesNotContain("details", Uri.UnescapeDataString(url));
    }

    [Fact]
    public void Read_tail_while_file_is_open_for_writing()
    {
        var path = Path.Combine(_dir.Path, "2026-10-24.log");
        File.WriteAllLines(path, Enumerable.Range(1, 100).Select(i => "line " + i));
        File.WriteAllText(Path.Combine(_dir.Path, "2026-10-23.log"), "old");
        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        Assert.Equal(path, ProblemReport.LatestLog(_dir.Path));
        var tail = ProblemReport.ReadTail(path);
        Assert.Equal(40, tail.Count);
        Assert.Equal("line 61", tail[0]);
        Assert.Equal("line 100", tail[^1]);
        Assert.Empty(ProblemReport.ReadTail(Path.Combine(_dir.Path, "none.log")));
        Assert.Null(ProblemReport.LatestLog(Path.Combine(_dir.Path, "missing")));
    }
}
