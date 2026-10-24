using System.Diagnostics;
using System.Globalization;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Localization;

namespace DropWheel.Engines;

/// <summary>
/// Установленный бесплатный LibreOffice (soffice --headless --convert-to) — для документов, когда Microsoft Office
/// нет или он не работает. Не встраивается: находим установленный. Решение D-LIBREOFFICE.
/// Каждый запуск — со своим временным профилем и папкой результата (удаляются сразу), с таймаутом.
/// </summary>
/// <param name="sofficePath">Явный путь к soffice; null — искать (LibreOfficeLocator).</param>
/// <param name="timeout">Сколько ждать одну конвертацию; null — 3 минуты.</param>
/// <param name="csvCulture">Региональные настройки для CSV (разделитель); null — текущие настройки Windows.</param>
/// <param name="tempDirectory">Где создавать временный профиль и папку результата; null — %TEMP%.</param>
public sealed class LibreOfficeEngine(string? sofficePath = null, Action<string>? log = null, TimeSpan? timeout = null,
    CultureInfo? csvCulture = null, string? tempDirectory = null) : IInternalEngine
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);

    private readonly Lazy<string?> _soffice = new(() => sofficePath ?? LibreOfficeLocator.Find());
    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public EngineKind Kind => EngineKind.LibreOffice;

    /// <summary>Путь к найденному soffice (null — LibreOffice не установлен). Ищется один раз.</summary>
    public string? SofficePath => _soffice.Value;

    public bool IsAvailable => SofficePath is { } path && File.Exists(path);

    public bool CanRun(Operation op) => LibreOfficeFormats.Supports(op) && IsAvailable;

    public bool CanRun(Operation op, string extension) => LibreOfficeFormats.Supports(op, extension) && IsAvailable;

    public async Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct)
    {
        var input = inputs[0];
        if (!IsAvailable) throw new ConversionException(Loc.F(Loc.T.ErrCantStart, "LibreOffice"), "soffice не найден");
        // Документ с паролем LibreOffice без окон не откроет — скажем сразу и понятно.
        if (OfficeFormats.IsPasswordProtected(input)) throw new ConversionException(Loc.T.ErrDocPassword, input);

        var csv = string.Equals(op.Id, LibreOfficeFormats.ExcelToCsvId, StringComparison.OrdinalIgnoreCase);
        var filter = LibreOfficeFormats.Filter(op, csvCulture ?? CultureInfo.CurrentCulture,
            csv ? Documents.SpreadsheetCsv.FirstVisibleSheet(input) : null);
        // Временная папка: профиль LibreOffice (несколько МБ) и результат. Имя без пробелов и кириллицы,
        // но %TEMP% может быть с кириллицей (C:\Users\Роман\…) — профиль передаётся как %-кодированный URL.
        var root = Path.Combine(tempDirectory ?? Path.GetTempPath(), $"dropwheel-lo-{Guid.NewGuid():N}");
        var profile = Path.Combine(root, "profile");
        var outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        try
        {
            await RunSofficeAsync(LibreOfficeFormats.Arguments(input, outDir, profile, filter), ct);

            var expected = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + "." + LibreOfficeFormats.OutputExtension(filter));
            var made = File.Exists(expected) ? expected : Directory.EnumerateFiles(outDir).FirstOrDefault();
            if (made is null || new FileInfo(made).Length == 0)
                throw new ConversionException(Loc.F(Loc.T.ErrAppCantConvert, "LibreOffice"), $"{op.Id}: {input} — LibreOffice не записал результат");
            if (csv)
                AddUtf8Bom(made); // как CSV UTF-8 из Excel: без BOM Excel показал бы кириллицу кракозябрами
            File.Move(made, output, overwrite: true);
            progress?.Report(1.0);
        }
        finally
        {
            DeleteQuietly(root);
        }
    }

    private async Task RunSofficeAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var exe = SofficePath!;
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // Linux (тесты): с языком «C/POSIX» LibreOffice не понимает путь профиля с кириллицей и зависает.
        if (!OperatingSystem.IsWindows()) psi.Environment["LC_ALL"] = "C.UTF-8";
        log?.Invoke($"> {exe} {string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))}");

        using var process = new Process { StartInfo = psi };
        var text = new StringBuilder();
        void Collect(object? _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (text) if (text.Length < 8000) text.AppendLine(e.Data);
        }
        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        try
        {
            process.Start();
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            throw new ConversionException(Loc.F(Loc.T.ErrCantStart, Path.GetFileName(exe)), ex.ToString(), ex);
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timer = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            // Завис (окно, которого не видно, битый файл) или отмена — убиваем вместе с soffice.bin.
            try { process.Kill(entireProcessTree: true); } catch { /* уже завершился */ }
            await process.WaitForExitAsync(CancellationToken.None);
            if (ct.IsCancellationRequested) throw;
            throw new ConversionException(
                Loc.F(Loc.T.ErrAppTimeout, "LibreOffice", _timeout.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture)),
                $"soffice не завершился за {_timeout}");
        }

        string output;
        lock (text) output = text.ToString().Trim();
        if (output.Length > 0) log?.Invoke(output);
        if (process.ExitCode != 0)
            throw new ConversionException(Loc.F(Loc.T.ErrAppCantConvert, "LibreOffice"),
                $"soffice завершился с кодом {process.ExitCode}\n{output}");
    }

    private static void AddUtf8Bom(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) return;
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        file.Write([0xEF, 0xBB, 0xBF]);
        file.Write(bytes);
    }

    /// <summary>Временная папка. Только что убитый soffice в Windows может ещё держать файлы — несколько попыток.</summary>
    private static void DeleteQuietly(string dir)
    {
        for (var attempt = 0; attempt < 5 && Directory.Exists(dir); attempt++)
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(200); }
        }
    }
}

/// <summary>
/// Где установлен LibreOffice. Порядок: переменная DROPWHEEL_SOFFICE → реестр Windows
/// (HKLM\SOFTWARE\LibreOffice\UNO\InstallPath, и 32-битный в WOW6432Node) → C:\Program Files\LibreOffice\program
/// → PATH (Linux). В папке Windows берём soffice.com (консольный — ждёт конца конвертации), иначе soffice.exe.
/// </summary>
public static class LibreOfficeLocator
{
    public static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("DROPWHEEL_SOFFICE");
        if (!string.IsNullOrWhiteSpace(env)) return File.Exists(env) ? env : null;

        foreach (var dir in CandidateDirs())
            if (InProgramDir(dir) is { } found) return found;
        return null;
    }

    /// <summary>soffice в папке program: в Windows — soffice.com, иначе soffice.exe; в Linux — soffice.</summary>
    public static string? InProgramDir(string dir)
    {
        string[] names = OperatingSystem.IsWindows() ? ["soffice.com", "soffice.exe"] : ["soffice", "libreoffice"];
        foreach (var name in names)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static IEnumerable<string> CandidateDirs()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var key in new[] { @"SOFTWARE\LibreOffice\UNO\InstallPath", @"SOFTWARE\WOW6432Node\LibreOffice\UNO\InstallPath" })
            {
                string? value = null;
                try
                {
                    using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                    value = k?.GetValue(null) as string;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                }
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                var pf = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(pf)) yield return Path.Combine(pf, "LibreOffice", "program");
            }
            yield break;
        }
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (!string.IsNullOrWhiteSpace(dir)) yield return dir;
    }
}
