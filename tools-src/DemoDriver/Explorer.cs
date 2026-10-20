using System.Diagnostics;
using System.Windows.Automation;
using static DemoDriver.Native;

namespace DemoDriver;

/// <summary>Окно проводника: открыть папку, поставить на место, «Крупные значки», найти значок файла (UI Automation).</summary>
internal static class Explorer
{
    /// <summary>Окно проводника с этой папкой (через Shell.Application.Windows()) или Zero.</summary>
    public static IntPtr FindWindow(string folder)
    {
        var want = Normalize(folder);
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic w = windows.Item(i);
                    if (w is null) continue;
                    string path = w.Document.Folder.Self.Path;
                    if (Normalize(path) == want) return new IntPtr((long)w.HWND);
                }
                catch
                {
                    // Окна браузера / «Главная» без пути — пропускаем.
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Shell.Application недоступен: " + ex.Message);
        }
        // Запасной путь: окно CabinetWClass с именем папки в заголовке.
        var name = Path.GetFileName(want);
        foreach (var hwnd in VisibleTopLevelWindows())
            if (ClassName(hwnd) == "CabinetWClass" && WindowText(hwnd).Contains(name, StringComparison.OrdinalIgnoreCase))
                return hwnd;
        return IntPtr.Zero;
    }

    /// <summary>Открыть папку в проводнике (или найти уже открытую), поставить окно в rect, крупные значки.</summary>
    public static IntPtr Open(string folder, RECT rect)
    {
        var hwnd = FindWindow(folder);
        if (hwnd == IntPtr.Zero)
        {
            Log.Write($"Открываю проводник: {folder}");
            var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            {
                UseShellExecute = false,
            };
            psi.ArgumentList.Add(folder);
            Process.Start(psi)?.Dispose();
            var sw = Stopwatch.StartNew();
            while (hwnd == IntPtr.Zero && sw.Elapsed < TimeSpan.FromSeconds(20))
            {
                Thread.Sleep(300);
                hwnd = FindWindow(folder);
            }
            if (hwnd == IntPtr.Zero)
            {
                Log.Error("окно проводника с папкой так и не появилось за 20 с");
                return IntPtr.Zero;
            }
            Log.Write($"Окно проводника появилось через {sw.Elapsed.TotalSeconds:F1} с");
            Thread.Sleep(1000); // дать проводнику дорисоваться
        }
        Log.Write("Проводник: " + Describe(hwnd));

        ShowWindow(hwnd, SW_RESTORE);
        Thread.Sleep(300);
        if (!SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, SWP_NOZORDER | SWP_SHOWWINDOW))
            Log.Write($"SetWindowPos не прошёл: ошибка {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        Thread.Sleep(300);
        Activate(hwnd);

        if (!SetLargeIcons(folder))
        {
            // Запасной путь — сочетание клавиш проводника «Крупные значки».
            Log.Write("Крупные значки: Ctrl+Shift+2");
            Keyboard.Chord(0x32, VK_CONTROL, VK_SHIFT);
        }
        Thread.Sleep(800);
        Log.Write("Проводник после настройки: " + Describe(hwnd));
        return hwnd;
    }

    /// <summary>Вывести окно на передний план; если Windows не даёт — щёлкнуть по пустому месту заголовка.</summary>
    public static void Activate(IntPtr hwnd)
    {
        SetForegroundWindow(hwnd);
        Thread.Sleep(300);
        if (GetForegroundWindow() == hwnd) return;
        Log.Write("SetForegroundWindow не сработал — щёлкаю по заголовку окна");
        GetWindowRect(hwnd, out var r);
        Mouse.Glide(r.Left + r.Width * 0.62, r.Top + 12, 400);
        Mouse.Click();
        Thread.Sleep(300);
        Log.Write("Активное окно: " + Describe(GetForegroundWindow()));
    }

    private static bool SetLargeIcons(string folder)
    {
        var want = Normalize(folder);
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                try
                {
                    dynamic w = windows.Item(i);
                    if (w is null) continue;
                    string path = w.Document.Folder.Self.Path;
                    if (Normalize(path) != want) continue;
                    w.Document.CurrentViewMode = 1; // FVM_ICON
                    w.Document.IconSize = 96;       // «Крупные значки»
                    Log.Write($"Вид папки: значки {w.Document.IconSize} px (режим {w.Document.CurrentViewMode})");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Write("Вид папки через Shell.Application: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write("Shell.Application: " + ex.Message);
        }
        return false;
    }

    /// <summary>
    /// Прямоугольник значка файла в окне проводника (физические пиксели экрана) — элемент списка UI Automation.
    /// Имя элемента — «Отпуск.heic» или «Отпуск» (если расширения скрыты).
    /// </summary>
    public static System.Windows.Rect? FindItem(IntPtr hwnd, string fileName, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                var items = ListItems(hwnd);
                var stem = Path.GetFileNameWithoutExtension(fileName);
                var match = items.FirstOrDefault(i => string.Equals(i.Name, fileName, StringComparison.OrdinalIgnoreCase));
                if (match.Name is null)
                {
                    var byStem = items.Where(i => string.Equals(i.Name, stem, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (byStem.Count == 1) match = byStem[0];
                }
                if (match.Name is not null && !match.Rect.IsEmpty && match.Rect.Width > 0)
                {
                    Log.Write($"Значок «{fileName}» найден (UIA «{match.Name}»): {Fmt(match.Rect)}");
                    return match.Rect;
                }
                if (sw.Elapsed > timeout)
                {
                    Log.Error($"значок «{fileName}» не найден за {timeout.TotalSeconds:F0} с. Элементы списка ({items.Count}): "
                        + string.Join("; ", items.Select(i => $"«{i.Name}» {Fmt(i.Rect)}")));
                    DumpTree(hwnd);
                    return null;
                }
                if (attempt == 1 || attempt % 10 == 0)
                    Log.Write($"Жду значок «{fileName}»… сейчас в списке: " + string.Join(", ", items.Select(i => i.Name)));
            }
            catch (Exception ex)
            {
                if (sw.Elapsed > timeout)
                {
                    Log.Error($"UI Automation: {ex}");
                    return null;
                }
                Log.Write("UI Automation: " + ex.Message);
            }
            Thread.Sleep(300);
        }
    }

    private static List<(string Name, System.Windows.Rect Rect)> ListItems(IntPtr hwnd)
    {
        var root = AutomationElement.FromHandle(hwnd);
        var found = root.FindAll(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        var list = new List<(string, System.Windows.Rect)>();
        foreach (AutomationElement item in found)
        {
            try { list.Add((item.Current.Name ?? "", item.Current.BoundingRectangle)); }
            catch { }
        }
        return list;
    }

    /// <summary>Дерево UI Automation окна (до 6 уровней) — в журнал, если значок не нашёлся.</summary>
    private static void DumpTree(IntPtr hwnd)
    {
        try
        {
            Log.Write("Дерево UI Automation окна проводника:");
            var walker = TreeWalker.ControlViewWalker;
            var lines = 0;
            void Walk(AutomationElement e, int depth)
            {
                if (depth > 6 || lines > 300) return;
                try
                {
                    Log.Write($"{new string(' ', depth * 2)}{e.Current.ControlType.ProgrammaticName} «{e.Current.Name}» "
                        + $"[{e.Current.ClassName}] {Fmt(e.Current.BoundingRectangle)}");
                }
                catch { }
                lines++;
                for (var c = walker.GetFirstChild(e); c is not null; c = walker.GetNextSibling(c)) Walk(c, depth + 1);
            }
            Walk(AutomationElement.FromHandle(hwnd), 0);
        }
        catch (Exception ex)
        {
            Log.Write("Дерево UIA: " + ex.Message);
        }
    }

    private static string Fmt(System.Windows.Rect r) =>
        r.IsEmpty ? "(пусто)" : $"({r.X:F0},{r.Y:F0} {r.Width:F0}x{r.Height:F0})";

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\', '/').ToUpperInvariant(); }
        catch { return path.TrimEnd('\\', '/').ToUpperInvariant(); }
    }
}
