using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Threading;
using DropWheel.Jobs;
using DropWheel.Logging;
using DropWheel.Updates;
using DropWheel.Localization;

namespace DropWheel.App;

/// <summary>
/// Самообновление из релиза GitHub (dev-build): раз в сутки и по кнопке в трее читаем version.json,
/// если сборка новее — качаем архив программы (и утилит, если их набор сменился), сверяем SHA-256,
/// ждём, пока нет активных задач, раскладываем файлы (UpdateApplier) и перезапускаемся.
/// Это единственное обращение программы в сеть; конвертация всегда офлайн.
/// </summary>
internal sealed class Updater : IDisposable
{
    private const string ReleaseUrl = "https://github.com/Luchano55/dropwheel/releases/download/dev-build/";
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly DispatcherTimer _timer;
    private readonly JobQueue _queue;
    private readonly FileLog _log;
    private readonly Action<UpdateManifest> _onAvailable;
    private readonly Action _beforeRestart;
    private int _busy;

    public Updater(Dispatcher ui, JobQueue queue, FileLog log, Action<UpdateManifest> onAvailable, Action beforeRestart)
    {
        _queue = queue;
        _log = log;
        _onAvailable = onAvailable;
        _beforeRestart = beforeRestart;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DropWheel-updater");
        _timer = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            await CheckAsync(manual: false);
        };
    }

    /// <summary>Найденное обновление (чтобы трей мог предложить его ещё раз).</summary>
    public UpdateManifest? Available { get; private set; }

    public static string AppDir => AppContext.BaseDirectory;

    public void Start() => _timer.Start();

    /// <summary>Выключили «Проверять обновления» — больше не проверять по расписанию.</summary>
    public void Stop() => _timer.Stop();

    /// <returns>true — есть обновление.</returns>
    public async Task<bool> CheckAsync(bool manual)
    {
        try
        {
            var json = await _http.GetStringAsync(ReleaseUrl + "version.json");
            var manifest = UpdateManifest.Parse(json);
            if (manifest is null)
            {
                _log.Write("Обновления: version.json не разобран");
                return false;
            }
            var plan = UpdatePlan.For(manifest, AppVersion.CurrentBuild, ReadLocalToolsVersion());
            _log.Write($"Обновления: у нас {AppVersion.CurrentBuild}, в релизе {manifest.Build}, план {plan}");
            if (!plan.Any) return false;
            Available = manifest;
            _onAvailable(manifest);
            return true;
        }
        catch (Exception ex)
        {
            // Нет интернета — это нормально для офлайн-программы; молчим, если проверка не ручная.
            _log.Write("Обновления: проверка не удалась: " + ex.Message);
            if (manual) throw;
            return false;
        }
    }

    /// <summary>Скачать, проверить, дождаться простоя, применить, перезапуститься.</summary>
    public async Task InstallAsync(UpdateManifest manifest, IProgress<string>? status = null)
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            var plan = UpdatePlan.For(manifest, AppVersion.CurrentBuild, ReadLocalToolsVersion());
            var work = Path.Combine(Path.GetTempPath(), "DropWheel-update-" + manifest.Build);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            var staging = Directory.CreateDirectory(Path.Combine(work, "staging")).FullName;

            status?.Report(Loc.T.UpdateDownloadingApp);
            await DownloadAndExtractAsync(manifest.AppZip, manifest.AppSha256, work, staging);
            if (plan.Tools)
            {
                status?.Report(Loc.T.UpdateDownloadingTools);
                await DownloadAndExtractAsync(manifest.ToolsZip, manifest.ToolsSha256, work, staging);
            }

            // Пока идёт конвертация, ffmpeg/magick заняты — подождём.
            status?.Report(Loc.T.UpdateWaitingJobs);
            while (_queue.Jobs.Any(j => j.State is JobState.Queued or JobState.Running))
                await Task.Delay(1000);

            var changed = UpdateApplier.Apply(staging, AppDir);
            _log.Write($"Обновление до сборки {manifest.Build}: заменено файлов {changed.Count}");
            Directory.Delete(work, recursive: true);

            Restart(manifest.Build);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private async Task DownloadAndExtractAsync(string zipName, string sha256, string work, string staging)
    {
        var zipPath = Path.Combine(work, zipName);
        await using (var source = await _http.GetStreamAsync(ReleaseUrl + zipName))
        await using (var file = File.Create(zipPath))
            await source.CopyToAsync(file);

        await using (var check = File.OpenRead(zipPath))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(check));
            if (!actual.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Loc.F(Loc.T.UpdateChecksumMismatch, zipName));
        }

        // В архивах сборки всё лежит в папке DropWheel/ — её содержимое и есть папка программы.
        var unpack = Path.Combine(work, Path.GetFileNameWithoutExtension(zipName));
        ZipFile.ExtractToDirectory(zipPath, unpack, overwriteFiles: true);
        var root = Directory.Exists(Path.Combine(unpack, "DropWheel")) ? Path.Combine(unpack, "DropWheel") : unpack;
        foreach (var src in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var dst = Path.Combine(staging, Path.GetRelativePath(root, src));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Move(src, dst, overwrite: true);
        }
    }

    private void Restart(int build)
    {
        _beforeRestart(); // освободить Mutex и канал, чтобы новый экземпляр стал первым
        var exe = Path.Combine(AppDir, "DropWheel.exe");
        Process.Start(new ProcessStartInfo(exe, $"--after-update {build}") { UseShellExecute = false, WorkingDirectory = AppDir });
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown());
    }

    private static string? ReadLocalToolsVersion()
    {
        var path = Path.Combine(AppDir, "tools", "tools-version.txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public void Dispose()
    {
        _timer.Stop();
        _http.Dispose();
    }
}
