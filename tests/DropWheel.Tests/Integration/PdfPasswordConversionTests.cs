using System.Diagnostics;
using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;

namespace DropWheel.Tests.Integration;

/// <summary>
/// Пароль на PDF настоящим qpdf (D-PDFPASS): «Мои фото/тест 1.pdf» → «(защищён)» → «(без пароля)»;
/// пароль с кириллицей и пробелами; в журнале и подробностях ошибки пароля нет; файлы «@…» удалены.
/// </summary>
public class PdfPasswordConversionTests : IDisposable
{
    private const string Password = "Мой пароль 1 {in}";
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);

    public void Dispose() => _dir.Dispose();

    private string FixturePdf(string name = "тест 1.pdf")
    {
        var p = Path.Combine(_dir.Path, name);
        File.Copy(Tools.Fixture("sample-3pages.pdf"), p);
        return p;
    }

    /// <summary>Код выхода qpdf: --requires-password — 0, если без пароля PDF не открыть, 2 — PDF не зашифрован.</summary>
    private static int QpdfExit(params string[] args)
    {
        var psi = new ProcessStartInfo(Tools.Qpdf) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    private async Task<(string Output, List<string> Log)> Run(Operation op, string input)
    {
        var hash = Tools.Hash(input);
        var log = new List<string>();
        try
        {
            var output = await _converter.ConvertAsync([input], op, log: line => { lock (log) log.Add(line); });
            return (output, log);
        }
        finally
        {
            Assert.Equal(hash, Tools.Hash(input)); // исходник не тронут
            Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*")); // ни временного PDF, ни файлов «@…»
            Assert.DoesNotContain(log, l => l.Contains("пароль 1", StringComparison.Ordinal));
        }
    }

    [SkippableFact]
    public async Task Protect_then_unlock_round_trip()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var input = FixturePdf();
        Assert.Equal(2, QpdfExit("--requires-password", input));

        var (protectedPdf, log) = await Run(PdfPassword.Bind(true, Password), input);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (защищён).pdf"), protectedPdf);
        Assert.Equal(0, QpdfExit("--requires-password", protectedPdf)); // без пароля не открыть
        Assert.Equal(0, QpdfExit("--password=" + Password, "--check", protectedPdf)); // с паролем — открывается
        Assert.Equal(2, QpdfExit("--password=wrong", "--check", protectedPdf));
        var encryption = Tools.Run(Tools.Qpdf, "--password=" + Password, "--show-encryption", protectedPdf);
        Assert.Contains("AESv3", encryption); // AES-256
        // В журнале — командная строка с «@файлом» вместо пароля.
        Assert.Contains(log, l => l.StartsWith("> ") && l.Contains("--encrypt") && l.Contains("@") && l.Contains(".arg"));

        var (unlocked, log2) = await Run(PdfPassword.Bind(false, Password), protectedPdf);
        Assert.Equal(Path.Combine(_dir.Path, "тест 1 (защищён) (без пароля).pdf"), unlocked);
        Assert.Equal(2, QpdfExit("--requires-password", unlocked)); // больше не зашифрован
        Assert.Equal(3, Tools.PdfPages(unlocked));
        Assert.Contains(log2, l => l.Contains("--decrypt"));
    }

    [SkippableFact]
    public async Task Wrong_password_is_a_clear_error_without_the_password()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var input = FixturePdf();
        var (protectedPdf, _) = await Run(PdfPassword.Bind(true, Password), input);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => Run(PdfPassword.Bind(false, "неверный пароль 1"), protectedPdf));
        Assert.Equal(Loc.T.ErrWrongPassword, ex.Message);
        Assert.DoesNotContain("неверный пароль 1", ex.Details ?? "");
        Assert.DoesNotContain(Directory.GetFiles(_dir.Path), f => f.Contains("без пароля"));
    }

    [SkippableFact]
    public async Task Protecting_an_already_protected_pdf_says_so()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var (protectedPdf, _) = await Run(PdfPassword.Bind(true, Password), FixturePdf());
        var ex = await Assert.ThrowsAsync<ConversionException>(() => Run(PdfPassword.Bind(true, "другой пароль 1"), protectedPdf));
        Assert.Equal(Loc.T.ErrPdfAlreadyProtected, ex.Message);
    }

    [SkippableFact]
    public async Task Queue_keeps_the_password_out_of_job_error_and_log()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var (protectedPdf, _) = await Run(PdfPassword.Bind(true, Password), FixturePdf());
        var log = new List<string>();
        var queue = new JobQueue(_converter, log: line => { lock (log) log.Add(line); });
        var job = queue.EnqueueSelection([protectedPdf], PdfPassword.Bind(false, "неверный пароль 1")).Single();
        await job.Completion;
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(Loc.T.ErrWrongPassword, job.Error);
        Assert.DoesNotContain("неверный пароль 1", job.ErrorDetails ?? "");
        Assert.NotEmpty(log);
        Assert.DoesNotContain(log, l => l.Contains("неверный пароль 1"));
    }

    [SkippableFact]
    public async Task Cli_protects_and_unlocks_with_password_flag()
    {
        Skip.IfNot(Tools.HasQpdf, "нет qpdf");
        var input = FixturePdf();
        var err = new StringWriter();
        var code = await Program.RunAsync(["convert", input, "pdf-protect", "--password", Password, "-v"], new StringWriter(), err);
        Assert.Equal(0, code);
        Assert.DoesNotContain("пароль 1", err.ToString()); // подробный журнал CLI — без пароля
        var protectedPdf = Path.Combine(_dir.Path, "тест 1 (защищён).pdf");
        Assert.Equal(0, QpdfExit("--requires-password", protectedPdf));

        err = new StringWriter();
        code = await Program.RunAsync(["convert", protectedPdf, "pdf-unlock", "--password", "нет", "-v"], new StringWriter(), err);
        Assert.Equal(1, code);
        Assert.Contains(Loc.T.ErrWrongPassword, err.ToString());

        code = await Program.RunAsync(["convert", protectedPdf, "pdf-unlock", "--password", Password], new StringWriter(), new StringWriter());
        Assert.Equal(0, code);
        Assert.Equal(2, QpdfExit("--requires-password", Path.Combine(_dir.Path, "тест 1 (защищён) (без пароля).pdf")));
    }

    [Fact]
    public async Task Cli_rejects_missing_or_misplaced_password()
    {
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.pdf", "pdf-protect"], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.pdf", "pdf-unlock", "--password", "  "], new StringWriter(), new StringWriter()));
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.pdf", "pdf-rotate", "--password", "x"], new StringWriter(), new StringWriter()));
    }
}
