using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Wheel;

namespace DropWheel.Tests;

/// <summary>Пароль на PDF (D-PDFPASS): каталог, колесо, аргументы qpdf, секреты в журнале и файлах «@…», логика окна.</summary>
public class PdfPasswordTests
{
    private const string In = "/docs/Мои фото/тест 1.pdf";
    private const string Out = "/docs/Мои фото/.dropwheel-tmp/.dropwheel-abc.pdf";
    private static readonly ConversionSettings S = ConversionSettings.Default;

    // ---------- каталог и колесо ----------

    [Fact]
    public void Protect_and_unlock_are_pdf_tools_for_one_file()
    {
        var single = OperationCatalog.ForFiles(["/docs/отчёт.pdf"]).Select(o => o.Id).ToList();
        Assert.Contains(PdfPassword.ProtectId, single);
        Assert.Contains(PdfPassword.UnlockId, single);
        var two = OperationCatalog.ForFiles(["a.pdf", "b.pdf"]).Select(o => o.Id).ToList();
        Assert.DoesNotContain(PdfPassword.ProtectId, two);
        Assert.DoesNotContain(PdfPassword.UnlockId, two);
        Assert.DoesNotContain(PdfPassword.ProtectId, OperationCatalog.ForFile("photo.jpg").Select(o => o.Id));
        foreach (var id in new[] { PdfPassword.ProtectId, PdfPassword.UnlockId })
        {
            var op = OperationCatalog.Find(id)!;
            Assert.Equal(OperationMode.Tool, op.Mode);
            Assert.Equal(EngineKind.Qpdf, op.Engine);
            Assert.Equal(1, op.MaxFiles);
            Assert.True(op.NeedsValues);
            Assert.Equal([PdfPassword.Key], op.SecretParameters);
            Assert.False(op.HiddenByDefault);
        }
    }

    [Fact]
    public void Pdf_tools_wheel_has_exactly_eight_and_shows_all_by_default()
    {
        var tools = WheelGroups.Operations(WheelGroups.Pdf).Where(o => o.Mode == OperationMode.Tool).Select(o => o.Id).ToList();
        Assert.Equal(["pdf-merge", "pdf-split", "pdf-optimize", "zip-pack", "pack-7z", "pdf-rotate", PdfPassword.ProtectId, PdfPassword.UnlockId], tools);
    }

    [Fact]
    public void Folders_skip_password_operations()
    {
        using var dir = new TempDir();
        dir.File("отчёт.pdf", "%PDF-1.4");
        var offer = FolderPlan.OfferFor([dir.Path]);
        Assert.Contains(offer.Operations, o => o.Id == "pdf-rotate");
        Assert.DoesNotContain(offer.Operations, o => PdfPassword.IsPasswordOperation(o.Id));
    }

    [Fact]
    public void Labels_and_suffixes()
    {
        var protect = OperationCatalog.Find(PdfPassword.ProtectId)!;
        var unlock = OperationCatalog.Find(PdfPassword.UnlockId)!;
        Assert.Equal("Защитить…", Loc.Label(protect, UiLanguage.Ru));
        Assert.Equal(" (защищён)", Loc.Suffix(protect, UiLanguage.Ru));
        Assert.Equal("Снять пароль…", Loc.Label(unlock, UiLanguage.Ru));
        Assert.Equal(" (без пароля)", Loc.Suffix(unlock, UiLanguage.Ru));
        Assert.Equal("Protect…", Loc.Label(protect, UiLanguage.En));
        Assert.Equal(" (protected)", Loc.Suffix(protect, UiLanguage.En));
        Assert.Equal("Remove password…", Loc.Label(unlock, UiLanguage.En));
        Assert.Equal(" (unlocked)", Loc.Suffix(unlock, UiLanguage.En));
    }

    // ---------- аргументы qpdf ----------

    [Fact]
    public void Protect_args_are_aes256_with_same_user_and_owner_password()
    {
        var args = ArgumentBuilder.Build(PdfPassword.Bind(true, "тайна 1"), In, Out, S);
        Assert.Equal(["--encrypt", "--user-password=тайна 1", "--owner-password=тайна 1", "--bits=256", "--", In, Out], args);
    }

    [Fact]
    public void Unlock_args_decrypt_with_password()
    {
        var args = ArgumentBuilder.Build(PdfPassword.Bind(false, "тайна 1"), In, Out, S);
        Assert.Equal(["--password=тайна 1", "--decrypt", In, Out], args);
    }

    [Theory]
    [InlineData("{in}")]
    [InlineData("a{out}b")]
    [InlineData("{password}")]
    [InlineData("-dash @at")]
    public void Password_with_braces_or_dashes_is_taken_literally(string password)
    {
        var args = ArgumentBuilder.Build(PdfPassword.Bind(false, password), In, Out, S);
        Assert.Equal("--password=" + password, args[0]);
    }

    [Fact]
    public void Bind_rejects_empty_or_multiline_password()
    {
        Assert.Throws<ArgumentException>(() => PdfPassword.Bind(true, ""));
        Assert.Throws<ArgumentException>(() => PdfPassword.Bind(true, "   "));
        Assert.Throws<ArgumentException>(() => PdfPassword.Bind(true, "a\nb"));
        Assert.Throws<ArgumentException>(() => PdfPassword.Bind(false, "a\rb"));
        Assert.Equal(" с пробелами ", PdfPassword.Bind(false, " с пробелами ").Values![PdfPassword.Key]);
    }

    [Fact]
    public void Secret_values_are_listed_only_when_set()
    {
        Assert.Empty(OperationCatalog.Find(PdfPassword.ProtectId)!.SecretValues());
        Assert.Equal(["x1"], PdfPassword.Bind(true, "x1").SecretValues());
        Assert.Empty(VideoTrim.Bind(false, 1, 2).SecretValues()); // у обрезки секретов нет
    }

    // ---------- секреты: журнал и файлы «@…» ----------

    [Fact]
    public void Mask_hides_every_secret_occurrence()
    {
        Assert.Equal("> qpdf --password=•••••• @x ••••••", SecretArguments.Mask("> qpdf --password=тайна @x тайна", ["тайна"]));
        Assert.Equal("ab", SecretArguments.Mask("ab", []));
        Assert.Equal("", SecretArguments.Mask(null, ["x"]));
        // Длинный секрет — первым: от него не остаётся хвоста.
        Assert.Equal("[••••••]", SecretArguments.Mask("[abcd]", ["ab", "abcd"]));
        var lines = new List<string>();
        SecretArguments.MaskLog(lines.Add, ["тайна"])!("пароль тайна");
        Assert.Equal(["пароль ••••••"], lines);
    }

    [Fact]
    public void Arguments_with_secret_move_to_arg_files()
    {
        using var dir = new TempDir();
        var created = new List<string>();
        var args = SecretArguments.MoveToFiles(
            ["--encrypt", "--user-password=тайна 1", "--owner-password=тайна 1", "--bits=256", "--", In, Out], ["тайна 1"], dir.Path, created);

        Assert.Equal(2, created.Count);
        Assert.Equal(["--encrypt", "@" + created[0], "@" + created[1], "--bits=256", "--", In, Out], args);
        Assert.DoesNotContain(args, a => a.Contains("тайна"));
        Assert.Equal("--user-password=тайна 1\n", File.ReadAllText(created[0]));
        Assert.Equal(new System.Text.UTF8Encoding(false).GetBytes("--owner-password=тайна 1\n"), File.ReadAllBytes(created[1])); // без BOM
        Assert.All(created, f => Assert.StartsWith(".dropwheel-", Path.GetFileName(f)));

        SecretArguments.Delete(created);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    [Fact]
    public void Arguments_without_secrets_stay_as_they_are()
    {
        var created = new List<string>();
        IReadOnlyList<string> args = ["--decrypt", In, Out];
        Assert.Same(args, SecretArguments.MoveToFiles(args, [], "/nowhere", created));
        Assert.Equal(args, SecretArguments.MoveToFiles(args, ["тайна"], "/nowhere", created));
        Assert.Empty(created);
    }

    [Fact]
    public void Only_qpdf_reads_arg_files()
    {
        Assert.True(SecretArguments.SupportsArgFiles(EngineKind.Qpdf));
        Assert.False(SecretArguments.SupportsArgFiles(EngineKind.FFmpeg));
    }

    [Fact]
    public async Task Converter_refuses_unbound_password_operation_before_running_anything()
    {
        using var dir = new TempDir();
        var pdf = dir.File("тест 1.pdf", "%PDF-1.4");
        var converter = new Converter(new ToolLocator(), S);
        var ex = await Assert.ThrowsAsync<ConversionException>(() =>
            converter.ConvertAsync(pdf, OperationCatalog.Find(PdfPassword.UnlockId)!));
        Assert.Contains(PdfPassword.Key, ex.Message);
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path, ".dropwheel-*"));
    }

    // ---------- окно: когда доступна кнопка ----------

    [Theory]
    [InlineData("тайна", "тайна", true)]
    [InlineData("тайна", "тайн", false)]
    [InlineData("тайна", "", false)]
    [InlineData("", "", false)]
    [InlineData("  ", "  ", false)]
    [InlineData("Тайна", "тайна", false)] // регистр важен
    [InlineData(" с пробелом", " с пробелом", true)]
    public void Protect_button_needs_matching_nonempty_passwords(string password, string repeat, bool can) =>
        Assert.Equal(can, PdfPassword.CanProtect(password, repeat));

    [Fact]
    public void Mismatch_shows_only_after_typing_the_repeat()
    {
        Assert.False(PdfPassword.ShowMismatch("тайна", ""));
        Assert.True(PdfPassword.ShowMismatch("тайна", "т"));
        Assert.False(PdfPassword.ShowMismatch("тайна", "тайна"));
    }

    [Fact]
    public void Unlock_needs_any_valid_password()
    {
        Assert.True(PdfPassword.IsValid("1"));
        Assert.False(PdfPassword.IsValid(null));
        Assert.False(PdfPassword.IsValid("a\nb"));
    }
}
