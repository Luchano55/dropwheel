using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

/// <summary>«Копировать результат в буфер обмена» (D-COPYRESULT): что класть в буфер для результатов одной пачки.</summary>
public class ResultClipboardTests
{
    private static readonly Dictionary<string, long> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        [@"C:\Мои фото\тест 1 (оформлено).png"] = 200_000,
        [@"C:\Мои фото\тест 1.jpg"] = 3_000_000,
        [@"C:\Мои фото\тест 1.webp"] = 100_000,
        [@"C:\Мои фото\анимация.gif"] = 900_000,
        [@"C:\Мои фото\значок.ico"] = 50_000,
        [@"C:\Мои фото\огромная.png"] = ResultClipboard.MaxImageBytes + 1,
        [@"C:\Видео\ролик.mp4"] = 10_000_000,
        [@"C:\Документы\доклад.pdf"] = 500_000,
    };

    private static readonly HashSet<string> Folders = new(StringComparer.OrdinalIgnoreCase) { @"C:\Мои фото (JPG)", @"C:\Документы\доклад (страницы)" };

    private static ResultClipboardAction Decide(params string[] results) =>
        ResultClipboard.Decide(results, Files.ContainsKey, Folders.Contains, p => Files[p]);

    [Theory]
    [InlineData(@"C:\Мои фото\тест 1 (оформлено).png")]
    [InlineData(@"C:\Мои фото\тест 1.jpg")]
    [InlineData(@"C:\Мои фото\тест 1.webp")]
    public void One_picture_goes_as_a_picture(string path)
    {
        var action = Decide(path);
        Assert.Equal(ResultClipboardKind.Image, action.Kind);
        Assert.Equal([path], action.Paths);
    }

    [Theory]
    [InlineData(@"C:\Видео\ролик.mp4")]
    [InlineData(@"C:\Документы\доклад.pdf")]
    [InlineData(@"C:\Мои фото\анимация.gif")] // картинкой GIF потерял бы анимацию
    [InlineData(@"C:\Мои фото\значок.ico")]
    [InlineData(@"C:\Мои фото\огромная.png")]
    [InlineData(@"C:\Документы\доклад (страницы)")] // папка-результат (разбить PDF)
    public void Anything_else_goes_as_a_file(string path)
    {
        var action = Decide(path);
        Assert.Equal(ResultClipboardKind.Files, action.Kind);
        Assert.Equal([path], action.Paths);
    }

    [Fact]
    public void Several_results_go_as_files_even_if_they_are_pictures()
    {
        var action = Decide(@"C:\Мои фото\тест 1.jpg", @"C:\Мои фото\тест 1.webp", @"C:\Мои фото\тест 1.JPG");
        Assert.Equal(ResultClipboardKind.Files, action.Kind);
        Assert.Equal([@"C:\Мои фото\тест 1.jpg", @"C:\Мои фото\тест 1.webp"], action.Paths);
    }

    [Fact]
    public void Folder_batch_root_goes_as_a_folder_and_missing_results_are_skipped()
    {
        var action = Decide(@"C:\Мои фото (JPG)\", @"C:\нет\такого.png");
        Assert.Equal(ResultClipboardKind.Files, action.Kind);
        Assert.Equal([@"C:\Мои фото (JPG)"], action.Paths);

        Assert.Equal(ResultClipboardKind.None, Decide(@"C:\нет\такого.png").Kind);
        Assert.Equal(ResultClipboardKind.None, Decide().Kind);
        Assert.Equal(ResultClipboardKind.None, Decide("", "  ").Kind);
    }

    [Fact]
    public void Real_files_are_checked_on_disk()
    {
        using var dir = new TempDir();
        var png = Path.Combine(dir.Path, "тест 1 (оформлено).png");
        File.WriteAllBytes(png, [1, 2, 3]);
        Assert.Equal(ResultClipboardKind.Image, ResultClipboard.Decide([png]).Kind);
        Assert.Equal(ResultClipboardKind.Files, ResultClipboard.Decide([dir.Path]).Kind);
    }
}
