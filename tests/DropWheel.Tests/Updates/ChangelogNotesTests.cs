using DropWheel.Localization;
using DropWheel.Updates;

namespace DropWheel.Tests.Updates;

public class ChangelogNotesTests
{
    private const string Sample = """
        # Что нового в DropWheel / Changelog

        Раздел каждой версии начинается со строки `## <версия>`.

        ## 1.2.0

        **Новое в этой версии**
        - Окно «Что нового» после обновления: `ChangelogNotes`,
          продолжение строки пункта.
        - Ссылка [на релизы](https://example.org/releases).

        **English**
        - A “What's new” window after an update,
          continued line.

        ## 1.1.1

        **Исправлено**
        - Мои фото/тест 1.heic больше не падает.

        **English**
        - Fixed a crash.

        ## 1.1.0

        Без английской части.
        - пункт

        ## 1.0.0

        **English**
        - The first release.
        """;

    [Fact]
    public void Parse_skips_intro_and_finds_sections()
    {
        var sections = ChangelogNotes.Parse(Sample);
        Assert.Equal(["1.2.0", "1.1.1", "1.1.0", "1.0.0"], sections.Select(s => s.Version));
        Assert.DoesNotContain(sections, s => s.Body.Contains("Раздел каждой версии"));
    }

    [Fact]
    public void Russian_part_is_plain_text_with_bullets()
    {
        var note = Assert.Single(ChangelogNotes.Select(Sample, "1.2.0", null, UiLanguage.Ru));
        Assert.Equal("1.2.0", note.Version);
        Assert.Equal(
            "Новое в этой версии\n" +
            "• Окно «Что нового» после обновления: ChangelogNotes, продолжение строки пункта.\n" +
            "• Ссылка на релизы.", note.Text);
        Assert.DoesNotContain("English", note.Text);
    }

    [Fact]
    public void English_part_is_chosen_by_language()
    {
        var note = Assert.Single(ChangelogNotes.Select(Sample, "1.2.0", null, UiLanguage.En));
        Assert.Equal("• A “What's new” window after an update, continued line.", note.Text);
    }

    [Fact]
    public void Dev_build_uses_its_release_section()
    {
        var note = Assert.Single(ChangelogNotes.Select(Sample, "1.2.0-dev.57", "1.2.0-dev.56", UiLanguage.Ru));
        Assert.Equal("1.2.0", note.Version);
        Assert.Equal(new Version(1, 2, 0), ChangelogNotes.BaseVersion("1.2.0-dev.57"));
        Assert.Equal(new Version(1, 2, 0), ChangelogNotes.BaseVersion("v1.2.0+abc"));
        Assert.Null(ChangelogNotes.BaseVersion("разработчика"));
    }

    [Fact]
    public void All_versions_newer_than_previous_up_to_current()
    {
        var notes = ChangelogNotes.Select(Sample, "1.2.0", "1.1.0", UiLanguage.Ru);
        Assert.Equal(["1.2.0", "1.1.1"], notes.Select(n => n.Version));
        Assert.Equal("Исправлено\n• Мои фото/тест 1.heic больше не падает.", notes[1].Text);

        // Текущая — 1.1.1: раздел 1.2.0 (ещё не вышедший) не показывается.
        Assert.Equal(["1.1.1", "1.1.0"], ChangelogNotes.Select(Sample, "1.1.1", "1.0.0", UiLanguage.Ru).Select(n => n.Version));
    }

    [Fact]
    public void Missing_parts_and_sections()
    {
        // Нет английской части — русская; нет русской — пусто, и раздел пропускается.
        Assert.Equal("Без английской части.\n• пункт", Assert.Single(ChangelogNotes.Select(Sample, "1.1.0", null, UiLanguage.En)).Text);
        Assert.Empty(ChangelogNotes.Select(Sample, "1.0.0", null, UiLanguage.Ru));
        Assert.Empty(ChangelogNotes.Select(Sample, "9.9.9", null, UiLanguage.Ru));
        Assert.Empty(ChangelogNotes.Select("", "1.2.0", "1.1.0", UiLanguage.Ru));
        // Сборка разработчика — верхний раздел.
        Assert.Equal("1.2.0", Assert.Single(ChangelogNotes.Select(Sample, null, null, UiLanguage.Ru)).Version);
    }

    [Theory]
    [InlineData(null, "1.2.0", false, false)]   // первый запуск — молча
    [InlineData(null, "1.2.0", true, true)]     // первое обновление на версию с «Что нового»
    [InlineData("1.1.1", "1.2.0", false, true)]
    [InlineData("1.2.0", "1.2.0", true, false)]
    [InlineData("1.2.0-dev.56", "1.2.0-dev.57", false, true)]
    [InlineData("1.1.1", null, true, false)]    // сборка разработчика
    public void When_to_show_at_startup(string? lastSeen, string? current, bool afterUpdate, bool show) =>
        Assert.Equal(show, WhatsNew.ShouldShow(lastSeen, current, afterUpdate));

    [Fact]
    public void Real_changelog_has_both_languages_for_released_versions()
    {
        var text = File.ReadAllText(RepoRoot("docs", "CHANGELOG.md"));
        foreach (var v in new[] { "1.1.1", "1.1.0", "1.0.0" })
        {
            var ru = Assert.Single(ChangelogNotes.Select(text, v, null, UiLanguage.Ru)).Text;
            var en = Assert.Single(ChangelogNotes.Select(text, v, null, UiLanguage.En)).Text;
            Assert.Matches("[А-Яа-яЁё]", ru);
            Assert.DoesNotContain("**", ru + en);
            Assert.DoesNotContain("`", ru + en);
            Assert.StartsWith("•", en);
        }
    }

    private static string RepoRoot(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DropWheel.sln"))) return Path.Combine([dir.FullName, .. parts]);
        throw new DirectoryNotFoundException("Не найден корень репозитория (DropWheel.sln)");
    }
}
