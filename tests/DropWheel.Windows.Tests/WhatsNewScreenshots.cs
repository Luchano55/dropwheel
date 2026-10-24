using DropWheel.App;
using DropWheel.Localization;
using DropWheel.Ui;
using DropWheel.Updates;

namespace DropWheel.Windows.Tests;

/// <summary>Окно «Что нового» (D-WHATSNEW) → PNG: светлая и тёмная тема, английский. Текст — свой, не из CHANGELOG.</summary>
public class WhatsNewScreenshots
{
    internal const string Sample = """
        ## 1.2.0

        **Новое в этой версии**
        - После обновления — окно «Что нового» с изменениями новой версии; открыть ещё раз — пункт «Что нового» в меню значка.
        - Обрезка картинки помнит последнюю пропорцию.

        **Исправлено**
        - Файлы из папки `Мои фото` с пробелами в имени больше не теряются в очереди.

        **English**
        - After an update, a “What's new” window lists the changes; open it again from the tray menu → What's new.
        - Picture cropping remembers the last aspect ratio.
        - Fixed: files from folders with spaces in the name no longer get lost in the queue.

        ## 1.1.1

        **Исправлено**
        - Документы из почты открываются без ошибки.

        **English**
        - Fixed: documents from e-mail open without an error.
        """;

    internal static void Save(bool light, string name, string? previous)
    {
        var notes = ChangelogNotes.Select(Sample, "1.2.0", previous, Loc.Current);
        var view = new WhatsNewView(light ? SettingsPalette.Light() : SettingsPalette.Dark(), "1.2.0", notes);
        TrimScreenshots.SaveWindow(view, WhatsNewView.DefaultWidth, WhatsNewView.TitleFor("1.2.0"), view.Palette, light, name);
    }

    [Theory]
    [InlineData(true, "whatsnew-light", null)]
    [InlineData(false, "whatsnew-dark", "1.1.0")] // обновились через версию — два раздела
    public void WhatsNew_window(bool light, string name, string? previous) =>
        Sta.Run(() => Save(light, name, previous));
}

/// <summary>Окно «Что нового» по-английски (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class WhatsNewEnglishScreenshots
{
    [Fact]
    public void WhatsNew_window_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            WhatsNewScreenshots.Save(light: true, "whatsnew-light-en", null);
        });
    }
}
