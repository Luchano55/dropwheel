using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Shell;

namespace DropWheel.Tests.Shell;

public class ClipboardPlanTests
{
    private static readonly HashSet<string> Existing = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Мои фото\тест 1.heic", @"C:\Мои фото\видео.mp4", @"C:\Мои фото\доклад.pdf",
    };

    private static bool Exists(string path) => Existing.Contains(path);

    [Fact]
    public void Files_open_wheel_for_those_files()
    {
        string[] files = [@"C:\Мои фото\тест 1.heic", @"C:\Мои фото\видео.mp4", @"C:\Мои фото\доклад.pdf"];
        var action = ClipboardPlan.Decide(files, hasImage: false, Exists);
        Assert.Equal(ClipboardActionKind.Files, action.Kind);
        Assert.Equal(files, action.Files);
    }

    [Fact]
    public void Files_win_over_image()
    {
        var action = ClipboardPlan.Decide([@"C:\Мои фото\тест 1.heic"], hasImage: true, Exists);
        Assert.Equal(ClipboardActionKind.Files, action.Kind);
    }

    [Fact]
    public void Missing_files_folders_and_duplicates_are_dropped()
    {
        var action = ClipboardPlan.Decide(
            [@"C:\Мои фото", @"C:\нет такого.jpg", @"C:\Мои фото\видео.mp4", @"c:\мои фото\ВИДЕО.mp4", ""], hasImage: false, Exists);
        Assert.Equal([@"C:\Мои фото\видео.mp4"], action.Files);
    }

    [Fact]
    public void Image_without_files_is_saved()
    {
        Assert.Equal(ClipboardActionKind.Image, ClipboardPlan.Decide(null, hasImage: true, Exists).Kind);
        // Файлов, которые есть на диске, нет, но есть картинка — картинка.
        Assert.Equal(ClipboardActionKind.Image, ClipboardPlan.Decide([@"C:\нет.jpg"], hasImage: true, Exists).Kind);
    }

    [Fact]
    public void Text_or_empty_gives_nothing()
    {
        Assert.Equal(ClipboardActionKind.Nothing, ClipboardPlan.Decide(null, hasImage: false, Exists).Kind);
        Assert.Equal(ClipboardActionKind.Nothing, ClipboardPlan.Decide([], hasImage: false, Exists).Kind);
        Assert.Empty(ClipboardPlan.Decide([@"C:\нет.jpg"], hasImage: false, Exists).Files);
    }

    [Fact]
    public void Image_name_has_date_and_time_without_colons()
    {
        var name = ClipboardPlan.ImageFileName(new DateTime(2026, 10, 22, 10, 31, 5));
        Assert.Equal("Буфер 2026-10-22 10-31-05.png", name);
        Assert.DoesNotContain(':', name);
    }

    [Fact]
    public void Busy_name_gets_number()
    {
        var now = new DateTime(2026, 10, 22, 10, 31, 5);
        var dir = Path.Combine("x", "Мои фото");
        var first = Path.Combine(dir, "Буфер 2026-10-22 10-31-05.png");
        Assert.Equal(first, ClipboardPlan.ImagePath(dir, now, _ => false));
        Assert.Equal(Path.Combine(dir, "Буфер 2026-10-22 10-31-05 (1).png"), ClipboardPlan.ImagePath(dir, now, p => p == first));
    }

    [Fact]
    public void Image_folder_is_shared_output_folder_or_pictures()
    {
        Assert.Equal("/data/Результаты", ClipboardPlan.ImageFolder(ConversionSettings.Default with { OutputFolder = "/data/Результаты" }));
        var fallback = ClipboardPlan.ImageFolder(ConversionSettings.Default);
        Assert.Equal(ClipboardPlan.DefaultImageFolder, fallback);
        Assert.Equal("DropWheel", Path.GetFileName(fallback));
    }

    [Fact]
    public void Own_clipboard_folder_wins_over_shared_output_folder()
    {
        var shared = ConversionSettings.Default with { OutputFolder = "/data/Результаты" };
        Assert.Equal("/data/Буфер", ClipboardPlan.ImageFolder("/data/Буфер", shared));
        Assert.Equal("/data/Результаты", ClipboardPlan.ImageFolder(null, shared));
        Assert.Equal(ClipboardPlan.DefaultImageFolder, ClipboardPlan.ImageFolder("", ConversionSettings.Default));
    }

    [Theory]
    [InlineData(true, true, "/p/Буфер 1.jpg", false)]   // оставлять — не трогаем
    [InlineData(false, true, "/p/Буфер 1.jpg", true)]   // не оставлять, готово — удалить
    [InlineData(false, false, "/p/Буфер 1.jpg", false)] // ошибка или отмена — картинка остаётся
    [InlineData(false, true, null, false)]              // результата нет
    [InlineData(false, true, "/p/Буфер 1.png", false)]  // результат — сам PNG
    public void Image_is_deleted_only_after_success_when_not_kept(bool keep, bool done, string? output, bool expected)
    {
        Assert.Equal(expected, ClipboardPlan.ShouldDeleteImage(keep, done, "/p/Буфер 1.png", output));
    }
}

[Collection(LanguageCollection.Name)]
public class ClipboardPlanEnglishTests
{
    [Fact]
    public void English_image_name()
    {
        using var _ = Loc.Use(UiLanguage.En);
        Assert.Equal("Clipboard 2026-10-22 10-31-05.png", ClipboardPlan.ImageFileName(new DateTime(2026, 10, 22, 10, 31, 5)));
    }
}
