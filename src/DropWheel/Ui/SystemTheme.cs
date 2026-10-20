using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace DropWheel.Ui;

/// <summary>Цвета акцента Windows: основной и оттенки (как в «Параметры → Персонализация → Цвета»).</summary>
public sealed record AccentColors(Color Base, Color Light1, Color Light2, Color Dark1)
{
    /// <summary>Синий Windows по умолчанию — для тестов и на случай, если система не ответила.</summary>
    public static readonly AccentColors Default = FromBase(Color.FromRgb(0x00, 0x78, 0xD4));

    /// <summary>Оттенки, вычисленные из основного цвета (если UISettings недоступен).</summary>
    public static AccentColors FromBase(Color c) =>
        new(c, Mix(c, Colors.White, 0.25), Mix(c, Colors.White, 0.5), Mix(c, Colors.Black, 0.22));

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t),
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    /// <summary>Чёрный или белый текст поверх цвета — что читается лучше.</summary>
    public static Color TextOn(Color c)
    {
        static double Lin(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        var l = 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        return l > 0.4 ? Color.FromRgb(0, 0, 0) : Colors.White;
    }
}

/// <summary>
/// Системные настройки внешнего вида: светлая/тёмная тема, акцент, прозрачность, анимации.
/// Читаются один раз и перечитываются при смене настроек Windows (WM_SETTINGCHANGE),
/// после чего вызывается <see cref="Changed"/> в потоке интерфейса. Никогда не бросает.
/// </summary>
public static class SystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly object Gate = new();
    private static bool _loaded;
    private static bool _appsLight = true;
    private static bool _shellLight;
    private static bool _transparency = true;
    private static bool _animations = true;
    private static AccentColors _accent = AccentColors.Default;
    private static bool _subscribed;

    /// <summary>Тема приложений (колесо): «Режим приложения по умолчанию».</summary>
    public static bool AppsLight { get { Ensure(); return _appsLight; } }

    /// <summary>Тема Windows (панель задач и её всплывающие окна) — для окна задач у трея и меню трея.</summary>
    public static bool ShellLight { get { Ensure(); return _shellLight; } }

    /// <summary>«Эффекты прозрачности» включены и не включена высокая контрастность.</summary>
    public static bool TransparencyEnabled { get { Ensure(); return _transparency && !SystemParameters.HighContrast; } }

    /// <summary>«Эффекты анимации» Windows включены.</summary>
    public static bool AnimationsEnabled { get { Ensure(); return _animations; } }

    public static AccentColors Accent { get { Ensure(); return _accent; } }

    /// <summary>Тема/акцент/прозрачность поменялись. Вызывается в потоке, где был первый вызов Subscribe.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Следить за сменой настроек Windows. Вызывать из потока интерфейса (там же придёт Changed).
    /// </summary>
    public static void Subscribe()
    {
        if (_subscribed) return;
        _subscribed = true;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        try
        {
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color
                    or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility
                    or UserPreferenceCategory.Window)
                    dispatcher.BeginInvoke(() =>
                    {
                        if (Refresh()) Changed?.Invoke();
                    });
            };
        }
        catch (Exception)
        {
            // Без подписки тема перечитается при следующем запуске.
        }
    }

    /// <summary>Перечитать настройки. true — что-то изменилось.</summary>
    public static bool Refresh()
    {
        var appsLight = ReadDword("AppsUseLightTheme", 1) != 0;
        var shellLight = ReadDword("SystemUsesLightTheme", 0) != 0;
        var transparency = ReadDword("EnableTransparency", 1) != 0;
        var animations = ReadAnimations();
        var accent = ReadAccent();
        lock (Gate)
        {
            var changed = !_loaded || appsLight != _appsLight || shellLight != _shellLight
                          || transparency != _transparency || animations != _animations || accent != _accent;
            _appsLight = appsLight;
            _shellLight = shellLight;
            _transparency = transparency;
            _animations = animations;
            _accent = accent;
            _loaded = true;
            return changed;
        }
    }

    private static void Ensure()
    {
        if (!_loaded) Refresh();
    }

    private static int ReadDword(string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue(name) is int v ? v : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static bool ReadAnimations()
    {
        var enabled = true;
        try { enabled = SystemParameters.ClientAreaAnimation; }
        catch (Exception) { }
        try
        {
            // Windows 10 1809+: «Показывать анимацию в Windows».
            if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) enabled = false;
        }
        catch (Exception) { }
        return enabled;
    }

    private static AccentColors ReadAccent()
    {
        try
        {
            var ui = new Windows.UI.ViewManagement.UISettings();
            Color Get(Windows.UI.ViewManagement.UIColorType t)
            {
                var c = ui.GetColorValue(t);
                return Color.FromRgb(c.R, c.G, c.B);
            }
            return new AccentColors(
                Get(Windows.UI.ViewManagement.UIColorType.Accent),
                Get(Windows.UI.ViewManagement.UIColorType.AccentLight1),
                Get(Windows.UI.ViewManagement.UIColorType.AccentLight2),
                Get(Windows.UI.ViewManagement.UIColorType.AccentDark1));
        }
        catch (Exception)
        {
            return AccentColors.Default;
        }
    }
}
