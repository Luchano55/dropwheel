using System.Runtime.InteropServices;

namespace DropWheel.Ui;

/// <summary>
/// Системные эффекты окон: размытие под окном, скругление углов, подложка Windows 11, форма окна.
/// Всё необязательное: каждая функция проверяет версию Windows, ловит ошибки и возвращает false —
/// тогда окно просто рисуется без эффекта (сплошной полупрозрачный фон).
/// Переменная окружения DROPWHEEL_EFFECTS=0 выключает размытие и подложки целиком.
/// </summary>
internal static class NativeEffects
{
    private const int WcaAccentPolicy = 19;
    private const int AccentDisabled = 0;
    private const int AccentEnableAcrylicBlurBehind = 4;

    private const int DwmwaUseImmersiveDarkModeOld = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;

    public const int CornerDoNotRound = 1;
    public const int CornerRound = 2;
    public const int CornerRoundSmall = 3;

    public const int BackdropNone = 1;
    /// <summary>Mica — подложка обычных окон Windows 11 (окно настроек).</summary>
    public const int BackdropMica = 2;
    public const int BackdropTransient = 3;

    public static int Build => Environment.OSVersion.Version.Build;

    /// <summary>Windows 11 (скруглённые углы окон).</summary>
    public static bool IsWindows11 => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    /// <summary>Подложки Mica/Acrylic через DWMWA_SYSTEMBACKDROP_TYPE (Windows 11 22H2+).</summary>
    public static bool SupportsSystemBackdrop => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);

    /// <summary>Акриловое размытие под окном (Windows 10 1803+).</summary>
    public static bool SupportsAcrylic => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17134);

    /// <summary>Эффекты не выключены переменной окружения DROPWHEEL_EFFECTS=0.</summary>
    public static bool EffectsAllowed => Environment.GetEnvironmentVariable("DROPWHEEL_EFFECTS") is not "0";

    /// <summary>
    /// Акриловое размытие под окном (недокументированный SetWindowCompositionAttribute,
    /// работает в Windows 10 1803+ и 11). tint — ARGB подкраски; альфа &gt; 0, иначе Windows рисует «дырку».
    /// </summary>
    public static bool SetAcrylic(IntPtr hwnd, bool enable, uint tintArgb = 0x10202020)
    {
        if (hwnd == IntPtr.Zero || (enable && (!SupportsAcrylic || !EffectsAllowed))) return false;
        try
        {
            // В ACCENT_POLICY цвет — AABBGGRR.
            var a = (tintArgb >> 24) & 0xFF;
            var r = (tintArgb >> 16) & 0xFF;
            var g = (tintArgb >> 8) & 0xFF;
            var b = tintArgb & 0xFF;
            var policy = new AccentPolicy
            {
                AccentState = enable ? AccentEnableAcrylicBlurBehind : AccentDisabled,
                AccentFlags = 0,
                GradientColor = (Math.Max(a, 1) << 24) | (b << 16) | (g << 8) | r,
            };
            var size = Marshal.SizeOf<AccentPolicy>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(policy, ptr, false);
                var data = new CompositionAttributeData { Attribute = WcaAccentPolicy, Data = ptr, SizeOfData = size };
                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Форма окна — круг с центром (cx, cy) и радиусом r в физических пикселях окна; r ≤ 0 — снять форму.</summary>
    public static bool SetCircleRegion(IntPtr hwnd, double cx, double cy, double r)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            if (r <= 0) return SetWindowRgn(hwnd, IntPtr.Zero, true) != 0;
            var rgn = CreateEllipticRgn((int)Math.Round(cx - r), (int)Math.Round(cy - r),
                (int)Math.Round(cx + r) + 1, (int)Math.Round(cy + r) + 1);
            if (rgn == IntPtr.Zero) return false;
            // Успешный SetWindowRgn забирает регион себе — удалять его нельзя.
            if (SetWindowRgn(hwnd, rgn, true) != 0) return true;
            DeleteObject(rgn);
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Тёмная рамка/подложка окна (Windows 10 1809+ и 11).</summary>
    public static void SetDarkMode(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        var attr = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985) ? DwmwaUseImmersiveDarkMode : DwmwaUseImmersiveDarkModeOld;
        SetDwmInt(hwnd, attr, dark ? 1 : 0);
    }

    /// <summary>Скругление углов окна (Windows 11).</summary>
    public static bool SetCorners(IntPtr hwnd, int preference) =>
        IsWindows11 && SetDwmInt(hwnd, DwmwaWindowCornerPreference, preference);

    /// <summary>Цвет рамки окна (Windows 11); argb.</summary>
    public static bool SetBorderColor(IntPtr hwnd, uint argb)
    {
        if (!IsWindows11) return false;
        var colorref = (int)(((argb & 0xFF) << 16) | (argb & 0xFF00) | ((argb >> 16) & 0xFF));
        return SetDwmInt(hwnd, DwmwaBorderColor, colorref);
    }

    /// <summary>
    /// Системная подложка Windows 11 22H2+ (акрил всплывающих окон). Для неё клиентская область
    /// должна быть «стеклом»: DwmExtendFrameIntoClientArea(−1) и прозрачный фон WPF — это делает вызывающий,
    /// и только если функция вернула true.
    /// </summary>
    public static bool SetSystemBackdrop(IntPtr hwnd, int type)
    {
        if (!SupportsSystemBackdrop || (type != BackdropNone && !EffectsAllowed)) return false;
        return SetDwmInt(hwnd, DwmwaSystemBackdropType, type);
    }

    /// <summary>Вся клиентская область — «стекло» DWM (под ней видна системная подложка).</summary>
    public static bool ExtendFrameIntoClientArea(IntPtr hwnd, bool sheet)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var m = sheet ? new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 } : new Margins();
            return DwmExtendFrameIntoClientArea(hwnd, ref m) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool SetDwmInt(IntPtr hwnd, int attribute, int value)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            return DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int)) == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionAttributeData data);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateEllipticRgn(int left, int top, int right, int bottom);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
}
