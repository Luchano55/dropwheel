using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;
using static DemoDriver.Native;

namespace DemoDriver;

/// <summary>Экран и рабочий стол: диагностика, разрешение, обои, значки, курсор, скриншоты.</summary>
internal static class Desktop
{
    public static (int Width, int Height) ScreenSize => (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));

    public static RECT WorkArea
    {
        get
        {
            var r = new RECT();
            if (!SystemParametersInfoW(SPI_GETWORKAREA, 0, ref r, 0))
            {
                var (w, h) = ScreenSize;
                r = new RECT { Right = w, Bottom = h };
            }
            return r;
        }
    }

    // ---------- диагностика ----------

    /// <summary>Всё, что поможет понять по журналу CI, почему не получилось: сеанс, рабочий стол, экран, курсор, окна.</summary>
    public static void PrintInfo()
    {
        Log.Write($"ОС: {RuntimeInformation.OSDescription} ({Environment.OSVersion.Version}), .NET {Environment.Version}");
        Log.Write($"Сеанс процесса: {Process.GetCurrentProcess().SessionId}, активный консольный сеанс: {WTSGetActiveConsoleSessionId()}");
        Log.Write($"Пользователь: {Environment.UserDomainName}\\{Environment.UserName}, администратор: {IsAdmin()}, UAC (EnableLUA): {ReadEnableLua()}");
        Log.Write($"Рабочий стол ввода: {InputDesktopName() ?? "не открывается (сеанс 0 или экран заблокирован?)"}");
        var (w, h) = ScreenSize;
        Log.Write($"Экран: {w}x{h}, виртуальный: {GetSystemMetrics(SM_XVIRTUALSCREEN)},{GetSystemMetrics(SM_YVIRTUALSCREEN)} "
            + $"{GetSystemMetrics(SM_CXVIRTUALSCREEN)}x{GetSystemMetrics(SM_CYVIRTUALSCREEN)}, мониторов: {GetSystemMetrics(SM_CMONITORS)}, "
            + $"рабочая область: {WorkArea}, DPI: {SafeSystemDpi()}");
        Log.Write($"Мышь подключена: {GetSystemMetrics(SM_MOUSEPRESENT) != 0}, курсор: {CursorState()}, "
            + $"порог перетаскивания: {GetSystemMetrics(SM_CXDRAG)}x{GetSystemMetrics(SM_CYDRAG)} px");
        Log.Write($"Активное окно: {Describe(GetForegroundWindow())}");
        Log.Write("Режимы экрана: " + string.Join(", ", DisplayModes().Select(m => $"{m.Width}x{m.Height}")));
        foreach (var name in new[] { "explorer", "DropWheel" })
            foreach (var p in Process.GetProcessesByName(name))
                Log.Write($"Процесс {name}: pid {p.Id}, сеанс {p.SessionId}");
        Log.Write("Видимые окна:");
        foreach (var hwnd in VisibleTopLevelWindows())
        {
            GetWindowRect(hwnd, out var r);
            if (r.Width <= 1 || r.Height <= 1) continue;
            Log.Write("  " + Describe(hwnd));
        }
    }

    private static string SafeSystemDpi()
    {
        try { return GetDpiForSystem().ToString(); }
        catch { return "?"; }
    }

    private static bool IsAdmin()
    {
        try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
        catch { return false; }
    }

    private static string ReadEnableLua()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            return key?.GetValue("EnableLUA")?.ToString() ?? "нет";
        }
        catch (Exception ex) { return "? " + ex.Message; }
    }

    /// <summary>Имя рабочего стола, на который сейчас идёт ввод: Default — всё хорошо, Winlogon — экран заблокирован.</summary>
    public static string? InputDesktopName()
    {
        var desktop = OpenInputDesktop(0, false, DESKTOP_READOBJECTS);
        if (desktop == IntPtr.Zero) return null;
        try
        {
            var buffer = new byte[512];
            return GetUserObjectInformationW(desktop, UOI_NAME, buffer, (uint)buffer.Length, out var needed)
                ? Encoding.Unicode.GetString(buffer, 0, (int)Math.Max(0, needed - 2)).TrimEnd('\0')
                : null;
        }
        finally
        {
            CloseDesktop(desktop);
        }
    }

    // ---------- разрешение ----------

    public static List<(int Width, int Height)> DisplayModes()
    {
        var list = new List<(int, int)>();
        var dm = NewDevMode();
        for (var i = 0; EnumDisplaySettingsW(null, i, ref dm); i++)
        {
            if (dm.dmBitsPerPel >= 24 && !list.Contains((dm.dmPelsWidth, dm.dmPelsHeight)))
                list.Add((dm.dmPelsWidth, dm.dmPelsHeight));
            if (i > 2000) break;
        }
        return list;
    }

    /// <summary>Пробует разрешения по очереди; true — экран теперь в одном из них.</summary>
    public static bool SetResolution(IReadOnlyList<(int Width, int Height)> candidates)
    {
        var modes = DisplayModes();
        Log.Write($"Сейчас экран {ScreenSize.Width}x{ScreenSize.Height}; доступно режимов: {modes.Count}");
        foreach (var (w, h) in candidates)
        {
            if (ScreenSize == (w, h))
            {
                Log.Write($"Разрешение уже {w}x{h}");
                return true;
            }
            if (modes.Count > 0 && !modes.Contains((w, h)))
                Log.Write($"{w}x{h} нет в списке режимов — всё равно пробуем");

            var dm = NewDevMode();
            EnumDisplaySettingsW(null, ENUM_CURRENT_SETTINGS, ref dm);
            dm.dmPelsWidth = w;
            dm.dmPelsHeight = h;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT;
            var result = ChangeDisplaySettingsExW(null, ref dm, IntPtr.Zero, 0, IntPtr.Zero);
            Log.Write($"ChangeDisplaySettingsEx {w}x{h}: код {result}");
            if (result != DISP_CHANGE_SUCCESSFUL)
            {
                result = ChangeDisplaySettingsExW(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                Log.Write($"ChangeDisplaySettingsEx {w}x{h} (CDS_UPDATEREGISTRY): код {result}");
            }
            Thread.Sleep(1500);
            if (ScreenSize == (w, h))
            {
                Log.Write($"Разрешение установлено: {w}x{h}, рабочая область {WorkArea}");
                return true;
            }
        }
        Log.Write($"Разрешение не сменилось, остаётся {ScreenSize.Width}x{ScreenSize.Height}");
        return false;
    }

    private static DEVMODE NewDevMode() => new()
    {
        dmDeviceName = "",
        dmFormName = "",
        dmSize = (short)Marshal.SizeOf<DEVMODE>(),
    };

    // ---------- рабочий стол ----------

    /// <summary>Чистый рабочий стол для ролика: свернуть всё, градиентные обои, без значков, расширения файлов видны.</summary>
    public static void Prepare(string wallpaperPath)
    {
        MinimizeAll();
        SetWallpaper(wallpaperPath);
        HideDesktopIcons();
        ShowFileExtensions();
        EnsureCursorVisible();
    }

    public static void MinimizeAll()
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!)!;
            shell.MinimizeAll();
            Thread.Sleep(500);
            Log.Write("Все окна свёрнуты");
        }
        catch (Exception ex)
        {
            Log.Write("Свернуть окна не удалось: " + ex.Message);
        }
    }

    /// <summary>Рисует обои под размер экрана (тёмный сине-фиолетовый градиент с мягким свечением) и ставит их.</summary>
    public static void SetWallpaper(string path)
    {
        var (w, h) = ScreenSize;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var bmp = new Bitmap(Math.Max(w, 64), Math.Max(h, 64), PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(bmp))
            {
                var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
                using (var bg = new LinearGradientBrush(rect, Color.FromArgb(0x14, 0x1E, 0x3C), Color.FromArgb(0x3A, 0x1C, 0x5C), 35f))
                    g.FillRectangle(bg, rect);
                using var glowPath = new GraphicsPath();
                glowPath.AddEllipse(-bmp.Width * 0.1f, bmp.Height * 0.35f, bmp.Width * 0.9f, bmp.Height * 0.9f);
                using (var glow = new PathGradientBrush(glowPath)
                       {
                           CenterColor = Color.FromArgb(110, 0x2F, 0x80, 0xED),
                           SurroundColors = [Color.FromArgb(0, 0x2F, 0x80, 0xED)],
                       })
                    g.FillPath(glow, glowPath);
                bmp.Save(path, ImageFormat.Bmp);
            }

            using (var key = Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop"))
            {
                key.SetValue("WallpaperStyle", "2");
                key.SetValue("TileWallpaper", "0");
            }
            var ok = SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            Log.Write(ok ? $"Обои: {path} ({w}x{h})" : $"Обои не поставились: ошибка {Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            Log.Write("Обои не получились: " + ex.Message);
        }
        // Запасной вариант — однотонный фон того же цвета (если обои запрещены политикой).
        try { SetSysColors(1, [COLOR_DESKTOP], [0x3C1E14]); } catch { }
    }

    public static void HideDesktopIcons()
    {
        var listView = FindDesktopListView(out var defView);
        if (listView == IntPtr.Zero)
        {
            Log.Write("Значки рабочего стола: окно не найдено — пропускаем");
            return;
        }
        if (!IsWindowVisible(listView))
        {
            Log.Write("Значки рабочего стола уже скрыты");
            return;
        }
        // 0x7402 — команда «Вид → Отображать значки рабочего стола» (переключатель).
        SendMessageW(defView, WM_COMMAND, new IntPtr(0x7402), IntPtr.Zero);
        Thread.Sleep(300);
        Log.Write($"Значки рабочего стола скрыты: {!IsWindowVisible(listView)}");
    }

    private static IntPtr FindDesktopListView(out IntPtr defView)
    {
        var progman = FindWindowW("Progman", null);
        defView = progman == IntPtr.Zero ? IntPtr.Zero : FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView == IntPtr.Zero)
        {
            // После смены обоев значки живут в одном из окон WorkerW.
            var found = IntPtr.Zero;
            EnumWindows((hwnd, _) =>
            {
                if (ClassName(hwnd) != "WorkerW") return true;
                var view = FindWindowExW(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view == IntPtr.Zero) return true;
                found = view;
                return false;
            }, IntPtr.Zero);
            defView = found;
        }
        return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowExW(defView, IntPtr.Zero, "SysListView32", null);
    }

    /// <summary>Показывать расширения файлов: «Отпуск.heic» → «Отпуск.png» в ролике нагляднее.</summary>
    public static void ShowFileExtensions()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                key.SetValue("HideFileExt", 0, RegistryValueKind.DWord);
            SendMessageTimeoutW(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "ShellState", SMTO_ABORTIFHUNG, 2000, out _);
            Log.Write("Расширения файлов в проводнике: показывать");
        }
        catch (Exception ex)
        {
            Log.Write("Настройка расширений не удалась: " + ex.Message);
        }
    }

    // ---------- курсор ----------

    public static string CursorState()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci)) return $"неизвестно (ошибка {Marshal.GetLastWin32Error()})";
        var state = ci.flags switch
        {
            CURSOR_SHOWING => "виден",
            CURSOR_SUPPRESSED => "подавлен (сенсорный ввод)",
            0 => "скрыт",
            _ => $"флаги {ci.flags}",
        };
        return $"{state} в ({ci.ptScreenPos.X},{ci.ptScreenPos.Y})";
    }

    private static bool CursorShowing()
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        return GetCursorInfo(ref ci) && (ci.flags & CURSOR_SHOWING) != 0;
    }

    /// <summary>
    /// В виртуальной машине без мыши Windows прячет курсор, и в ролике его не будет.
    /// Известный обход — включить «Управление указателем с клавиатуры» (Mouse Keys): курсор показывается всегда.
    /// </summary>
    public static void EnsureCursorVisible()
    {
        Wiggle();
        if (CursorShowing())
        {
            Log.Write("Курсор виден: " + CursorState());
            return;
        }
        Log.Write("Курсор не виден (" + CursorState() + ") — включаем Mouse Keys");
        try
        {
            var mk = new MOUSEKEYS { cbSize = (uint)Marshal.SizeOf<MOUSEKEYS>() };
            SystemParametersInfoW(SPI_GETMOUSEKEYS, mk.cbSize, ref mk, 0);
            mk.cbSize = (uint)Marshal.SizeOf<MOUSEKEYS>();
            mk.dwFlags |= MKF_MOUSEKEYSON | MKF_AVAILABLE;
            if (mk.iMaxSpeed == 0) mk.iMaxSpeed = 80;
            if (mk.iTimeToMaxSpeed == 0) mk.iTimeToMaxSpeed = 3000;
            if (mk.iCtrlSpeed == 0) mk.iCtrlSpeed = 80;
            var ok = SystemParametersInfoW(SPI_SETMOUSEKEYS, mk.cbSize, ref mk, SPIF_SENDCHANGE);
            Log.Write($"Mouse Keys: {(ok ? "включено" : $"ошибка {Marshal.GetLastWin32Error()}")}");
        }
        catch (Exception ex)
        {
            Log.Write("Mouse Keys: " + ex.Message);
        }
        Wiggle();
        Log.Write("Курсор теперь: " + CursorState());
    }

    private static void Wiggle()
    {
        var (x, y) = Mouse.Position;
        Mouse.MoveTo(x + 3, y + 2);
        Thread.Sleep(50);
        Mouse.MoveTo(x, y);
        Thread.Sleep(100);
    }

    // ---------- скриншоты ----------

    private static readonly List<Task> PendingSaves = [];

    /// <summary>
    /// Снимок всего экрана в PNG вместе с курсором. CAPTUREBLT — чтобы попали прозрачные окна (колесо, картинка перетаскивания).
    /// background — сохранить PNG в фоне (снимок делается сразу, сценарий не ждёт кодирования).
    /// Ошибка не роняет сценарий — только запись в журнал.
    /// </summary>
    public static void Screenshot(string path, bool background = false)
    {
        Bitmap bmp;
        try
        {
            var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
            var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
            var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            bmp = new Bitmap(vw, vh, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            // BitBlt с CAPTUREBLT вручную: без него не попадают прозрачные (layered) окна — само колесо.
            // Graphics.CopyFromScreen такую комбинацию флагов не принимает (проверка enum).
            var dest = g.GetHdc();
            var screen = Native.GetDC(IntPtr.Zero);
            try { Native.BitBlt(dest, 0, 0, vw, vh, screen, vx, vy, Native.SRCCOPY | Native.CAPTUREBLT); }
            finally
            {
                Native.ReleaseDC(IntPtr.Zero, screen);
                g.ReleaseHdc(dest);
            }
            DrawCursor(g, vx, vy);
        }
        catch (Exception ex)
        {
            Log.Write($"Скриншот {Path.GetFileName(path)} не получился: {ex.Message}");
            return;
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                bmp.Save(path, ImageFormat.Png);
                Log.Write($"Скриншот: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                Log.Write($"Скриншот {Path.GetFileName(path)} не сохранился: {ex.Message}");
            }
            finally
            {
                bmp.Dispose();
            }
        }

        if (!background)
        {
            Save();
            return;
        }
        lock (PendingSaves) PendingSaves.Add(Task.Run(Save));
    }

    /// <summary>Дождаться фоновых сохранений скриншотов.</summary>
    public static void FlushScreenshots()
    {
        Task[] tasks;
        lock (PendingSaves)
        {
            tasks = [.. PendingSaves];
            PendingSaves.Clear();
        }
        Task.WaitAll(tasks, TimeSpan.FromSeconds(30));
    }

    private static void DrawCursor(Graphics g, int vx, int vy)
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || ci.hCursor == IntPtr.Zero) return;
        int hotX = 0, hotY = 0;
        if (GetIconInfo(ci.hCursor, out var info))
        {
            hotX = info.xHotspot;
            hotY = info.yHotspot;
            if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
            if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
        }
        var hdc = g.GetHdc();
        try
        {
            DrawIconEx(hdc, ci.ptScreenPos.X - vx - hotX, ci.ptScreenPos.Y - vy - hotY, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }
}
