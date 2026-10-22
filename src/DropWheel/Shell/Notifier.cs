using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using DropWheel.Logging;
using DropWheel.Settings;
using DropWheel.Localization;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DropWheel.Shell;

/// <summary>
/// Системные уведомления Windows (раздел 8 ТЗ).
/// «Готово»: для фото и видео — превью результата, кнопки «Открыть» и «Показать в папке».
/// Несколько задач закончились почти одновременно — одно уведомление «Готово: 5 файлов» вместо пачки
/// (ждём 1 с тишины, но не дольше 3 с). Ошибка — короткая причина и «Подробнее» (журнал).
/// Вызывать Done/Failed из потока интерфейса.
/// </summary>
internal sealed class Notifier
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(3);

    private const string ActionKey = "action";
    private const string RevealAction = "reveal";
    private const string OpenAction = "open";
    private const string LogAction = "log";
    private const string GroupAction = "group";

    private static readonly string[] PreviewExtensions =
        ["jpg", "jpeg", "png", "webp", "avif", "heic", "heif", "bmp", "gif", "tif", "tiff", "mp4", "m4v", "mov", "mkv", "webm", "avi"];

    /// <summary>Пути групповых уведомлений: в аргументы кнопки длинный список не помещается.</summary>
    private static readonly ConcurrentDictionary<string, string[]> Groups = new();

    private readonly FileLog _log;
    private readonly DispatcherTimer _timer;
    private readonly List<string> _done = [];
    private readonly List<(string Input, string Reason)> _failed = [];
    private DateTime _firstPending;

    public Notifier(FileLog log)
    {
        _log = log;
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = Quiet };
        _timer.Tick += (_, _) => Flush();
        // Клик по уведомлению/кнопке. Приходит в фоновом потоке.
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            try
            {
                Handle(ToastArguments.Parse(e.Argument));
            }
            catch (Exception ex)
            {
                _log.Write("Не удалось выполнить действие уведомления: " + ex.Message);
            }
        };
    }

    public void Done(string outputPath)
    {
        _done.Add(outputPath);
        Schedule();
    }

    public void Failed(string inputPath, string reason)
    {
        _failed.Add((inputPath, reason));
        Schedule();
    }

    /// <summary>Открыть файл или папку программой по умолчанию.</summary>
    public static void Open(string? path)
    {
        if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path))) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Нет программы для этого типа — покажем в папке.
            RevealInExplorer(path);
        }
    }

    public static void RevealInExplorer(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            // explorer разбирает командную строку сам, поэтому строкой, а не ArgumentList.
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Показать в папке сразу несколько файлов (выделены все, если они в одной папке).</summary>
    public static void RevealMany(IReadOnlyList<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
        if (existing.Count == 0) return;
        var folder = Path.GetDirectoryName(existing[0]);
        var sameFolder = existing.All(p => string.Equals(Path.GetDirectoryName(p), folder, StringComparison.OrdinalIgnoreCase));
        if (existing.Count > 1 && sameFolder && folder is not null && SelectInFolder(folder, existing)) return;
        RevealInExplorer(existing[0]);
    }

    /// <summary>Открыть журнал за сегодня (в нём подробности ошибки); нет файла — папку журналов.</summary>
    public static void OpenLog(FileLog log)
    {
        try
        {
            if (File.Exists(log.CurrentFile)) Process.Start(new ProcessStartInfo(log.CurrentFile) { UseShellExecute = true });
            else
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogsDir}\"") { UseShellExecute = true });
            }
        }
        catch (Exception)
        {
        }
    }

    // ---- группировка ----

    private void Schedule()
    {
        var now = DateTime.UtcNow;
        if (!_timer.IsEnabled) _firstPending = now;
        _timer.Stop();
        // Ждём секунду тишины, но не дольше MaxWait от первого события.
        var left = _firstPending + MaxWait - now;
        _timer.Interval = left < Quiet ? (left > TimeSpan.Zero ? left : TimeSpan.FromMilliseconds(1)) : Quiet;
        _timer.Start();
    }

    private void Flush()
    {
        _timer.Stop();
        var done = _done.ToList();
        var failed = _failed.ToList();
        _done.Clear();
        _failed.Clear();

        if (done.Count == 1) _ = ShowDoneAsync(done[0]);
        else if (done.Count > 1) ShowDoneGroup(done);

        if (failed.Count == 1) ShowFailed(failed[0].Input, failed[0].Reason);
        else if (failed.Count > 1) ShowFailedGroup(failed);
    }

    // ---- уведомления ----

    private async Task ShowDoneAsync(string path)
    {
        string? preview = null;
        if (HasPreview(path))
        {
            try
            {
                var task = ShellThumbnails.SaveToastPreviewAsync(path);
                if (await Task.WhenAny(task, Task.Delay(PreviewTimeout)) == task) preview = await task;
            }
            catch (Exception ex)
            {
                _log.Write("Превью для уведомления не получилось: " + ex.Message);
            }
        }

        Show(() =>
        {
            var b = new ToastContentBuilder()
                .AddArgument(ActionKey, RevealAction)
                .AddArgument("path", path)
                .AddText(Loc.F(Loc.T.NotifyDone, Path.GetFileName(path.TrimEnd('\\', '/'))))
                .AddText(FolderLine([path]));
            if (preview is not null) b.AddHeroImage(new Uri(preview));
            return b
                .AddButton(new ToastButton().SetContent(Loc.T.ActionOpen)
                    .AddArgument(ActionKey, OpenAction).AddArgument("path", path))
                .AddButton(new ToastButton().SetContent(Loc.T.ActionReveal)
                    .AddArgument(ActionKey, RevealAction).AddArgument("path", path));
        });
    }

    private void ShowDoneGroup(IReadOnlyList<string> paths)
    {
        var id = Guid.NewGuid().ToString("N");
        Groups[id] = paths.ToArray();
        var names = paths.Select(p => Path.GetFileName(p.TrimEnd('\\', '/'))).ToList();
        var list = names.Count <= 2
            ? string.Join(", ", names)
            : Loc.F(Loc.T.NotifyList, names[0], names[1], names.Count - 2);
        Show(() => new ToastContentBuilder()
            .AddArgument(ActionKey, GroupAction)
            .AddArgument("id", id)
            .AddArgument("path", paths[0])
            .AddText(Loc.F(Loc.T.NotifyDone, Loc.Count(paths.Count, Loc.T.Files)))
            .AddText(list)
            .AddText(FolderLine(paths))
            .AddButton(new ToastButton().SetContent(Loc.T.ActionReveal)
                .AddArgument(ActionKey, GroupAction).AddArgument("id", id).AddArgument("path", paths[0])));
    }

    private void ShowFailed(string inputPath, string reason) => Show(() => new ToastContentBuilder()
        .AddArgument(ActionKey, LogAction)
        .AddText(Loc.F(Loc.T.NotifyFailed, Path.GetFileName(inputPath)))
        .AddText(reason)
        .AddButton(new ToastButton().SetContent(Loc.T.ActionDetails).AddArgument(ActionKey, LogAction)));

    private void ShowFailedGroup(IReadOnlyList<(string Input, string Reason)> failed) => Show(() => new ToastContentBuilder()
        .AddArgument(ActionKey, LogAction)
        .AddText(Loc.F(Loc.T.NotifyFailed, Loc.Count(failed.Count, Loc.T.Files)))
        .AddText($"{Path.GetFileName(failed[0].Input)}: {failed[0].Reason}")
        .AddText(Loc.T.NotifyDetailsInLog)
        .AddButton(new ToastButton().SetContent(Loc.T.ActionDetails).AddArgument(ActionKey, LogAction)));

    private void Show(Func<ToastContentBuilder> build)
    {
        try
        {
            build().Show();
        }
        catch (Exception ex)
        {
            _log.Write("Не удалось показать уведомление: " + ex.Message);
        }
    }

    private void Handle(ToastArguments args)
    {
        if (!args.TryGetValue(ActionKey, out var action)) return;
        args.TryGetValue("path", out var path);
        switch (action)
        {
            case OpenAction:
                Open(path);
                break;
            case RevealAction:
                RevealInExplorer(path);
                break;
            case GroupAction:
                if (args.TryGetValue("id", out var id) && Groups.TryGetValue(id, out var paths)) RevealMany(paths);
                else RevealInExplorer(path);
                break;
            case LogAction:
                OpenLog(_log);
                break;
        }
    }

    /// <summary>«В папке «Мои фото»» или «В разных папках».</summary>
    private static string FolderLine(IReadOnlyList<string> paths)
    {
        var folders = paths.Select(p => Path.GetDirectoryName(p.TrimEnd('\\', '/'))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (folders.Count != 1 || folders[0] is not { } folder) return Loc.T.NotifyDifferentFolders;
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        return string.IsNullOrEmpty(name) ? Loc.F(Loc.T.NotifyOnDrive, folder) : Loc.F(Loc.T.NotifyInFolder, name);
    }

    private static bool HasPreview(string path) =>
        File.Exists(path) && PreviewExtensions.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant());

    // ---- выделить несколько файлов в проводнике ----

    private static bool SelectInFolder(string folder, IReadOnlyList<string> files)
    {
        var folderPidl = IntPtr.Zero;
        var items = new List<IntPtr>();
        try
        {
            folderPidl = ILCreateFromPathW(folder);
            if (folderPidl == IntPtr.Zero) return false;
            foreach (var f in files)
            {
                var pidl = ILCreateFromPathW(f);
                if (pidl != IntPtr.Zero) items.Add(pidl);
            }
            return items.Count > 0 && SHOpenFolderAndSelectItems(folderPidl, (uint)items.Count, items.ToArray(), 0) == 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            foreach (var p in items) ILFree(p);
            if (folderPidl != IntPtr.Zero) ILFree(folderPidl);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string path);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);
}
