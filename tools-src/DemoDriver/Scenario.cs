using System.Diagnostics;
using DropWheel.Conversions;
using static DemoDriver.Native;

namespace DemoDriver;

internal sealed record ScenarioOptions(
    string Folder,
    string AppExe,
    string ToolsDir,
    string AppLogDir,
    string ShotsDir,
    string TimelinePath,
    string? Ffmpeg,
    string RawOutput,
    string RecordLog,
    string PhotoFile,
    string PhotoOp,
    string VideoFile,
    string VideoOp,
    double RingRadiusDip);

/// <summary>
/// Сценарий ролика (он же сквозной тест):
/// 1) фото: тащим из проводника → Пробел → колесо → ведём по секторам → бросаем на «PNG» → ждём PNG рядом;
/// 2) видео: тащим → Пробел → ещё Пробел (Инструменты) → бросаем на «Сжать» → второй запуск DropWheel.exe
///    открывает окно задач с прогрессом → ждём «… (сжато).mp4»;
/// 3) выделяем оба результата в проводнике.
/// Каждое действие — в журнал и в timeline.json (по нему монтаж ставит подписи), в ключевые моменты — скриншоты.
/// </summary>
internal sealed class Scenario(ScenarioOptions o)
{
    /// <summary>Конвертер пишет во временный «.dropwheel-&lt;guid&gt;.ext» и переименовывает по готовности (D3).</summary>
    private const string TempPrefix = ".dropwheel";

    private readonly Timeline _tl = new();
    private readonly List<string> _failures = [];
    private int _shot;
    private bool _buttonDown;
    private IntPtr _explorer;

    /// <returns>0 — всё получилось; 2 — результат не появился (сквозной тест не прошёл); 3 — не готово окружение.</returns>
    public int Run()
    {
        var (sw, sh) = (GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));
        _tl.ScreenWidth = sw / 2 * 2;
        _tl.ScreenHeight = sh / 2 * 2;
        var work = Desktop.WorkArea;
        Log.Write($"Сценарий: папка {o.Folder}, экран {sw}x{sh}, рабочая область {work}");
        Log.Write($"Фото «{o.PhotoFile}» → {o.PhotoOp}; видео «{o.VideoFile}» → {o.VideoOp}");

        _explorer = Explorer.FindWindow(o.Folder);
        if (_explorer == IntPtr.Zero)
        {
            Log.Error("нет окна проводника с папкой — сначала open-explorer");
            return 3;
        }
        if (DropWheelPids().Count == 0)
        {
            Log.Error("DropWheel.exe не запущен");
            return 3;
        }
        Desktop.EnsureCursorVisible();
        Explorer.Activate(_explorer);
        GetWindowRect(_explorer, out var er);

        Recorder? recorder = null;
        if (o.Ffmpeg is { Length: > 0 } ffmpeg)
        {
            recorder = Recorder.Start(ffmpeg, o.RawOutput, o.RecordLog, fps: 30);
            if (recorder is null) _tl.Errors.Add("запись экрана не запустилась");
        }
        else
        {
            Log.Write("ffmpeg не указан — сценарий без записи");
        }

        try
        {
            // Курсор — на пустое место в проводнике, секунда «тишины» в начале ролика.
            Mouse.Glide(er.Left + er.Width * 0.72, er.Top + er.Height * 0.78, 500);
            Thread.Sleep(1200);
            _tl.Mark("start");
            Shot("start");

            // ---- 1. Фото → PNG ----
            var before = FolderSnapshot();
            string? photoResult = null;
            if (DragToWheel(o.PhotoFile, o.PhotoOp, toggles: 0, "photo",
                    (work.Left + work.Width * 0.75, work.Top + work.Height * 0.40)))
            {
                CheckAppLog(o.PhotoOp);
                photoResult = WaitNewFile(before, o.PhotoFile, o.PhotoOp, TimeSpan.FromSeconds(60));
                if (photoResult is not null)
                {
                    _tl.Mark("photo-done");
                    _tl.Results["photo"] = photoResult;
                    Thread.Sleep(1700);
                    Shot("photo-done");
                }
            }

            // ---- 2. Видео → Инструменты → «Сжать» ----
            before = FolderSnapshot();
            string? videoResult = null;
            if (DragToWheel(o.VideoFile, o.VideoOp, toggles: 1, "video",
                    (work.Left + work.Width * 0.73, work.Top + work.Height * 0.44)))
            {
                // Второй запуск без файлов → первый экземпляр показывает окно задач.
                Thread.Sleep(300);
                _tl.Mark("jobs-launch");
                StartSecondInstance();
                if (WaitWindow(IsJobsWindow, TimeSpan.FromSeconds(10)) is var jobs && jobs != IntPtr.Zero)
                {
                    _tl.Mark("jobs-shown");
                    Log.Write("Окно задач: " + Describe(jobs));
                    Thread.Sleep(400);
                    Shot("jobs");
                }
                else
                {
                    _tl.Errors.Add("окно задач не появилось");
                    Log.Error("окно задач не появилось за 10 с");
                    Shot("no-jobs-window");
                }
                CheckAppLog(o.VideoOp);
                videoResult = WaitNewFile(before, o.VideoFile, o.VideoOp, TimeSpan.FromSeconds(180));
                if (videoResult is not null)
                {
                    _tl.Mark("video-done");
                    _tl.Results["video"] = videoResult;
                    Thread.Sleep(900);
                    Shot("video-done");
                }
            }

            // ---- 3. Итог: выделить оба результата ----
            _tl.Mark("outro");
            SelectResults(photoResult, videoResult);
            Mouse.Glide(work.Left + work.Width * 0.80, work.Top + work.Height * 0.20, 700);
            Thread.Sleep(2000);
            _tl.Mark("end");
            Shot("end");
        }
        catch (Exception ex)
        {
            Fail("сценарий упал: " + ex);
            Shot("crash");
        }
        finally
        {
            if (_buttonDown)
            {
                Mouse.LeftUp();
                _buttonDown = false;
            }
            if (recorder is not null)
            {
                recorder.Stop();
                _tl.T0UnixMs = recorder.T0UnixMs;
                _tl.RecordedSeconds = recorder.RecordedSeconds;
                recorder.Dispose();
                Log.Write($"Запись: {o.RawOutput}, {_tl.RecordedSeconds:F1} с, начало {_tl.T0UnixMs}");
            }
            Desktop.FlushScreenshots();
            _tl.Save(o.TimelinePath);
        }

        Log.Write("Итог сценария:");
        foreach (var (key, value) in _tl.Results) Log.Write($"  {key}: {value}");
        foreach (var e in _tl.Errors) Log.Write($"  проблема: {e}");
        if (_failures.Count == 0)
        {
            Log.Write("СКВОЗНОЙ ТЕСТ ПРОШЁЛ: оба файла появились рядом с исходниками");
            return 0;
        }
        foreach (var f in _failures) Log.Annotate("error", "Демо: " + f);
        Log.Write("СКВОЗНОЙ ТЕСТ НЕ ПРОШЁЛ");
        return 2;
    }

    /// <summary>
    /// Перетащить файл из проводника в точку wheelAt, вызвать колесо Пробелом, toggles раз сменить режим,
    /// провести курсор по соседним секторам и бросить на сектор операции opId.
    /// </summary>
    private bool DragToWheel(string fileName, string opId, int toggles, string tag, (double X, double Y) wheelAt)
    {
        var file = Path.Combine(o.Folder, fileName);
        var items = WheelLayout.Items(file, toggles, o.ToolsDir).ToList();
        var index = items.FindIndex(i => i.Id == opId);
        Log.Write($"[{tag}] сектора колеса (после {toggles} смен режима): "
            + string.Join(", ", items.Select((it, k) => $"{k}:{it.Label} [{it.Id}]")));
        if (index < 0)
        {
            Fail($"[{tag}] операции {opId} нет среди секторов колеса для «{fileName}»");
            return false;
        }

        Explorer.Activate(_explorer);
        var rect = Explorer.FindItem(_explorer, fileName, TimeSpan.FromSeconds(20));
        if (rect is not { } r)
        {
            Fail($"[{tag}] значок «{fileName}» не найден в проводнике");
            Shot($"{tag}-no-item");
            return false;
        }
        // Верхняя часть элемента — сам значок (подпись ниже).
        var grab = (X: r.X + r.Width / 2, Y: r.Y + r.Height * 0.36);

        Mouse.Glide(grab.X, grab.Y);
        Thread.Sleep(350);
        _tl.Mark($"{tag}-press");
        Mouse.LeftDown();
        _buttonDown = true;
        Thread.Sleep(160);
        // Сорвать с места: проводник начинает перетаскивание после сдвига на несколько пикселей.
        Mouse.Glide(grab.X + 14, grab.Y + 8, 160, bend: 0);
        Thread.Sleep(60);
        Mouse.Glide(wheelAt.X, wheelAt.Y, 1000);
        Thread.Sleep(300);
        Shot($"{tag}-dragging", background: true);

        _tl.Mark($"{tag}-space");
        Keyboard.Tap(VK_SPACE);
        var wheel = WaitWindow(IsWheelWindow, TimeSpan.FromSeconds(3));
        if (wheel == IntPtr.Zero)
        {
            Log.Write($"[{tag}] колесо не появилось за 3 с — жму Пробел ещё раз");
            Shot($"{tag}-no-wheel");
            Keyboard.Tap(VK_SPACE);
            wheel = WaitWindow(IsWheelWindow, TimeSpan.FromSeconds(3));
        }
        if (wheel == IntPtr.Zero)
        {
            Fail($"[{tag}] колесо не появилось после Пробела (хуки? DROPWHEEL_DEMO? перетаскивание не началось?)");
            CancelDrag();
            return false;
        }
        _tl.Mark($"{tag}-wheel");
        Log.Write($"[{tag}] колесо: {Describe(wheel)}");

        // Шевельнуть мышь: колесо строит сектора по DragEnter, а он приходит после движения.
        var (px, py) = Mouse.Position;
        Mouse.Glide(px + 4, py + 3, 120, bend: 0);
        Mouse.Glide(px, py, 120, bend: 0);
        Thread.Sleep(550);
        Shot($"{tag}-wheel", background: true);

        for (var k = 0; k < toggles; k++)
        {
            Thread.Sleep(350);
            _tl.Mark($"{tag}-toggle");
            Keyboard.Tap(VK_SPACE);
            Thread.Sleep(750);
            Shot($"{tag}-mode-{k + 1}", background: true);
        }

        GetWindowRect(wheel, out var wr);
        var cx = wr.Left + wr.Width / 2.0;
        var cy = wr.Top + wr.Height / 2.0;
        var dpi = GetDpiForWindow(wheel);
        var radius = o.RingRadiusDip * (dpi > 0 ? dpi / 96.0 : 1);
        Log.Write($"[{tag}] центр колеса ({cx:F0},{cy:F0}), DPI {dpi}, радиус кольца {radius:F0} px, "
            + $"цель — сектор {index} из {items.Count} «{items[index].Label}»");

        // Маршрут: два соседних сектора, потом нужный — видно, как подсвечиваются сектора.
        var route = new List<int>();
        if (items.Count >= 3) route.Add((index + 2) % items.Count);
        if (items.Count >= 2) route.Add((index + 1) % items.Count);
        route.Add(index);
        double? angle = null;
        foreach (var s in route)
        {
            var a = WheelLayout.SectorAngle(s, items.Count);
            if (angle is not { } from)
            {
                var (dx, dy) = WheelLayout.SectorPoint(s, items.Count, radius);
                Mouse.Glide(cx + dx, cy + dy, 450, bend: 0.05);
            }
            else
            {
                var delta = ((a - from) % 360 + 540) % 360 - 180;
                Mouse.Arc(cx, cy, radius, from, from + delta, 420);
            }
            angle = a;
            _tl.Mark($"{tag}-hover-{items[s].Id}");
            Thread.Sleep(s == index ? 750 : 400);
        }
        Shot($"{tag}-target", background: true);

        _tl.Mark($"{tag}-drop");
        Mouse.LeftUp();
        _buttonDown = false;
        var sw = Stopwatch.StartNew();
        while (IsWindowVisible(wheel) && sw.Elapsed < TimeSpan.FromSeconds(2)) Thread.Sleep(25);
        Log.Write(IsWindowVisible(wheel)
            ? $"[{tag}] колесо всё ещё видно через 2 с после броска"
            : $"[{tag}] колесо спряталось через {sw.ElapsedMilliseconds} мс после броска");
        return true;
    }

    /// <summary>Отмена перетаскивания: Esc (проводник прекращает перетаскивание), кнопку — отпустить.</summary>
    private void CancelDrag()
    {
        Keyboard.Tap(VK_ESCAPE);
        Thread.Sleep(200);
        if (_buttonDown)
        {
            Mouse.LeftUp();
            _buttonDown = false;
        }
        Thread.Sleep(300);
    }

    // ---------- окна DropWheel ----------

    private static HashSet<uint> DropWheelPids() =>
        Process.GetProcessesByName("DropWheel").Select(p => (uint)p.Id).ToHashSet();

    private static IntPtr WaitWindow(Func<IntPtr, HashSet<uint>, bool> match, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        var pids = DropWheelPids();
        var refreshed = sw.Elapsed;
        while (sw.Elapsed < timeout)
        {
            foreach (var hwnd in VisibleTopLevelWindows())
                if (pids.Contains(ProcessOf(hwnd)) && match(hwnd, pids))
                {
                    Log.Write($"Окно найдено через {sw.ElapsedMilliseconds} мс");
                    return hwnd;
                }
            Thread.Sleep(25);
            if (sw.Elapsed - refreshed > TimeSpan.FromMilliseconds(500))
            {
                pids = DropWheelPids();
                refreshed = sw.Elapsed;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Колесо — квадратное окно DropWheel без рамки (заголовок «DropWheel»).</summary>
    private static bool IsWheelWindow(IntPtr hwnd, HashSet<uint> pids)
    {
        if (!GetWindowRect(hwnd, out var r) || r.Width < 100 || r.Width > 2000) return false;
        var title = WindowText(hwnd);
        return Math.Abs(r.Width - r.Height) <= 4 && (title == "DropWheel" || title.Length == 0);
    }

    /// <summary>Окно задач — «DropWheel — задачи».</summary>
    private static bool IsJobsWindow(IntPtr hwnd, HashSet<uint> pids)
    {
        var title = WindowText(hwnd);
        return title.Contains("задач", StringComparison.OrdinalIgnoreCase)
            || (title.StartsWith("DropWheel", StringComparison.Ordinal) && title != "DropWheel");
    }

    private void StartSecondInstance()
    {
        try
        {
            Log.Write($"Второй запуск {o.AppExe} без файлов (должно открыться окно задач)");
            Process.Start(new ProcessStartInfo(o.AppExe) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex)
        {
            _tl.Errors.Add("второй запуск DropWheel.exe: " + ex.Message);
            Log.Error("второй запуск DropWheel.exe: " + ex.Message);
        }
    }

    // ---------- результаты ----------

    private HashSet<string> FolderSnapshot() =>
        Directory.EnumerateFileSystemEntries(o.Folder).Select(Path.GetFileName).OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ждём новый файл с расширением результата операции (временные .dropwheel-… не считаются).</summary>
    private string? WaitNewFile(HashSet<string> before, string sourceName, string opId, TimeSpan timeout)
    {
        var op = OperationCatalog.Find(opId);
        var ext = "." + (op?.Output.Resolve(Path.GetExtension(sourceName)) ?? "");
        var expected = Path.GetFileNameWithoutExtension(sourceName) + (op?.Suffix ?? "") + ext;
        Log.Write($"Жду результат «{expected}» (любой новый файл *{ext}, кроме временных {TempPrefix}…) до {timeout.TotalSeconds:F0} с");
        var sw = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        while (sw.Elapsed < timeout)
        {
            var fresh = FolderSnapshot().Where(n => !before.Contains(n)).ToList();
            var done = fresh
                .Where(n => !n.StartsWith(TempPrefix, StringComparison.OrdinalIgnoreCase)
                            && !n.Contains(".dropwheel-tmp", StringComparison.OrdinalIgnoreCase)
                            && n.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(n => string.Equals(n, expected, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            if (done is not null)
            {
                var path = Path.Combine(o.Folder, done);
                var size = new FileInfo(path).Length;
                Log.Write($"Результат появился через {sw.Elapsed.TotalSeconds:F1} с: «{done}», {size:N0} байт"
                    + (string.Equals(done, expected, StringComparison.OrdinalIgnoreCase) ? "" : $" (ожидалось имя «{expected}»)"));
                if (size == 0) Fail($"результат «{done}» пустой");
                return path;
            }
            if (sw.Elapsed - lastReport > TimeSpan.FromSeconds(5))
            {
                lastReport = sw.Elapsed;
                Log.Write($"  …ждём {sw.Elapsed.TotalSeconds:F0} с, новое в папке: "
                    + (fresh.Count == 0 ? "ничего" : string.Join(", ", fresh)));
            }
            Thread.Sleep(100);
        }
        Fail($"за {timeout.TotalSeconds:F0} с не появился результат «{expected}» для «{sourceName}»");
        Shot("no-result-" + opId);
        return null;
    }

    /// <summary>Журнал DropWheel: колесо записывает «Колесо: &lt;операция&gt;» при броске. Только для диагностики.</summary>
    private void CheckAppLog(string opId)
    {
        var sw = Stopwatch.StartNew();
        string? last = null;
        while (sw.Elapsed < TimeSpan.FromSeconds(3))
        {
            try
            {
                var file = Directory.Exists(o.AppLogDir)
                    ? new DirectoryInfo(o.AppLogDir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()
                    : null;
                if (file is not null)
                {
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream);
                    var lines = reader.ReadToEnd().Split('\n');
                    last = lines.LastOrDefault(l => l.Contains("Колесо:", StringComparison.Ordinal))?.Trim();
                    if (last is not null && last.Contains(opId, StringComparison.Ordinal))
                    {
                        Log.Write($"Журнал DropWheel подтверждает бросок: {last}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Журнал DropWheel не читается: " + ex.Message);
            }
            Thread.Sleep(200);
        }
        Log.Write($"Журнал DropWheel не подтвердил бросок на {opId}; последняя запись колеса: {last ?? "(нет)"}");
    }

    /// <summary>Выделить результаты в проводнике: щелчок по первому, Ctrl+щелчок по остальным.</summary>
    private void SelectResults(params string?[] files)
    {
        var first = true;
        foreach (var path in files)
        {
            if (path is null) continue;
            var rect = Explorer.FindItem(_explorer, Path.GetFileName(path), TimeSpan.FromSeconds(8));
            if (rect is not { } r) continue;
            Mouse.Glide(r.X + r.Width / 2, r.Y + r.Height * 0.4);
            Thread.Sleep(250);
            if (first)
            {
                Mouse.Click();
            }
            else
            {
                Keyboard.Down(VK_CONTROL);
                Thread.Sleep(50);
                Mouse.Click();
                Thread.Sleep(50);
                Keyboard.Up(VK_CONTROL);
            }
            first = false;
            Thread.Sleep(450);
        }
    }

    // ---------- помощники ----------

    private void Fail(string message)
    {
        _failures.Add(message);
        _tl.Fail(message);
    }

    private void Shot(string name, bool background = false) =>
        Desktop.Screenshot(Path.Combine(o.ShotsDir, $"demo-{++_shot:00}-{name}.png"), background);
}
