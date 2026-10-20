using System.Windows.Media;
using DropWheel.Ui;

namespace DropWheel.Wheel;

/// <summary>Фон колеса: Translucent — под окном системное размытие; Solid — без него (почти непрозрачный диск с тенью).</summary>
public enum WheelBackdrop { Solid, Translucent }

/// <summary>Цвета колеса для светлой и тёмной темы (в духе Windows 11).</summary>
public sealed class WheelPalette
{
    private WheelPalette(bool light, AccentColors accent)
    {
        IsLight = light;
        var acc = light ? accent.Dark1 : accent.Light2;
        Accent = MakeBrush(acc);
        OnAccent = MakeBrush(AccentColors.TextOn(acc));
        AccentGlow = MakeBrush(Color.FromArgb(0x59, 0xFF, 0xFF, 0xFF));

        if (light)
        {
            DiscTint = MakeBrush(0xC8, 0xF3, 0xF3, 0xF3);
            DiscSolid = MakeBrush(0xF7, 0xF6, 0xF6, 0xF6);
            RimOuter = MakePen(0x29, 0x00, 0x00, 0x00);
            RimInner = MakePen(0xB3, 0xFF, 0xFF, 0xFF);
            Shadow = Color.FromArgb(0x38, 0, 0, 0);
            Tile = MakeBrush(0x8C, 0xFF, 0xFF, 0xFF);
            Track = MakeBrush(0x0F, 0x00, 0x00, 0x00);
            Center = MakeBrush(0xD9, 0xFF, 0xFF, 0xFF);
            CenterHover = MakeBrush(0xFF, 0xFF, 0xFF, 0xFF);
            CenterRim = MakePen(0x14, 0x00, 0x00, 0x00);
            Text = MakeBrush(0xE4, 0x00, 0x00, 0x00);
            TextSecondary = MakeBrush(0x9E, 0x00, 0x00, 0x00);
            TextTertiary = MakeBrush(0x72, 0x00, 0x00, 0x00);
            // Подкраска системного акрила (почти прозрачная: основной цвет даёт WPF-диск).
            AcrylicTint = 0x18F3F3F3;
        }
        else
        {
            DiscTint = MakeBrush(0xB8, 0x20, 0x20, 0x20);
            DiscSolid = MakeBrush(0xF2, 0x24, 0x24, 0x24);
            RimOuter = MakePen(0x73, 0x00, 0x00, 0x00);
            RimInner = MakePen(0x1F, 0xFF, 0xFF, 0xFF);
            Shadow = Color.FromArgb(0x66, 0, 0, 0);
            Tile = MakeBrush(0x0F, 0xFF, 0xFF, 0xFF);
            Track = MakeBrush(0x0F, 0xFF, 0xFF, 0xFF);
            Center = MakeBrush(0x17, 0xFF, 0xFF, 0xFF);
            CenterHover = MakeBrush(0x2B, 0xFF, 0xFF, 0xFF);
            CenterRim = MakePen(0x14, 0xFF, 0xFF, 0xFF);
            Text = MakeBrush(0xFF, 0xFF, 0xFF, 0xFF);
            TextSecondary = MakeBrush(0xC5, 0xFF, 0xFF, 0xFF);
            TextTertiary = MakeBrush(0x87, 0xFF, 0xFF, 0xFF);
            AcrylicTint = 0x18202020;
        }
    }

    public static WheelPalette Dark(AccentColors? accent = null) => new(false, accent ?? AccentColors.Default);
    public static WheelPalette Light(AccentColors? accent = null) => new(true, accent ?? AccentColors.Default);

    /// <summary>По текущей теме приложений Windows и её акценту.</summary>
    public static WheelPalette FromSystem() => new(SystemTheme.AppsLight, SystemTheme.Accent);

    public bool IsLight { get; }
    public Brush DiscTint { get; }
    public Brush DiscSolid { get; }
    public Pen RimOuter { get; }
    public Pen RimInner { get; }
    public Color Shadow { get; }
    public Brush Tile { get; }
    public Brush Track { get; }
    public Brush Accent { get; }
    public Brush OnAccent { get; }
    public Brush AccentGlow { get; }
    public Brush Center { get; }
    public Brush CenterHover { get; }
    public Pen CenterRim { get; }
    public Brush Text { get; }
    public Brush TextSecondary { get; }
    public Brush TextTertiary { get; }
    /// <summary>ARGB подкраски системного размытия.</summary>
    public uint AcrylicTint { get; }

    private static Brush MakeBrush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush MakeBrush(byte a, byte r, byte g, byte b) => MakeBrush(Color.FromArgb(a, r, g, b));

    private static Pen MakePen(byte a, byte r, byte g, byte b)
    {
        var p = new Pen(MakeBrush(a, r, g, b), 1);
        p.Freeze();
        return p;
    }
}
