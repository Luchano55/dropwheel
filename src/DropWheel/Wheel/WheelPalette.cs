using System.Windows.Media;
using DropWheel.Ui;

namespace DropWheel.Wheel;

/// <summary>Фон колеса: Translucent — под окном системное размытие («матовое стекло»); Solid — без него (плотный диск).</summary>
public enum WheelBackdrop { Solid, Translucent }

/// <summary>
/// Цвета колеса: матовое стекло, полупрозрачные белые «лепестки», акцент Windows при наведении.
/// Светлая тема — лепестки белые ~65 %, подписи тёмно-серые тёплые; тёмная — лепестки белые ~14 %, подписи белые.
/// </summary>
public sealed class WheelPalette
{
    private WheelPalette(bool light, AccentColors accent)
    {
        IsLight = light;
        // Насыщенный акцент в обеих темах: подпись на нём — белая (или чёрная, если акцент светлый).
        Accent = MakeBrush(accent.Base);
        OnAccent = MakeBrush(AccentColors.TextOn(accent.Base));
        Pulse = MakeBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));

        if (light)
        {
            DiscTint = MakeBrush(0x59, 0xF7, 0xF7, 0xF7);
            DiscSolid = MakeBrush(0xF0, 0xF3, 0xF1, 0xEF);
            RimHighlight = MakePen(0xC0, 0xFF, 0xFF, 0xFF);
            RimOuter = MakePen(0x1A, 0x00, 0x00, 0x00);
            Shadow = Color.FromArgb(0x47, 0x00, 0x00, 0x00);
            Petal = MakeBrush(0xA6, 0xFF, 0xFF, 0xFF);
            PetalText = MakeBrush(0xFF, 0x3A, 0x2E, 0x28);
            PetalIcon = MakeBrush(0xD9, 0x3A, 0x2E, 0x28);
            PetalSubtext = MakeBrush(0xA6, 0x3A, 0x2E, 0x28);
            Track = MakeBrush(0x59, 0xFF, 0xFF, 0xFF);
            Center = MakeBrush(0x4D, 0xFF, 0xFF, 0xFF);
            CenterRim = MakePen(0x80, 0xFF, 0xFF, 0xFF);
            Pill = MakeBrush(0xE0, 0xFF, 0xFF, 0xFF);
            PillHover = MakeBrush(0xFF, 0xFF, 0xFF, 0xFF);
            PillRim = MakePen(0x14, 0x3A, 0x2E, 0x28);
            Text = MakeBrush(0xFF, 0x3A, 0x2E, 0x28);
            TextSecondary = MakeBrush(0xA6, 0x3A, 0x2E, 0x28);
            TextTertiary = MakeBrush(0x80, 0x3A, 0x2E, 0x28);
            AcrylicTint = 0x30F3F3F3;
        }
        else
        {
            DiscTint = MakeBrush(0x8C, 0x1E, 0x1E, 0x1E);
            DiscSolid = MakeBrush(0xF2, 0x26, 0x26, 0x26);
            RimHighlight = MakePen(0x38, 0xFF, 0xFF, 0xFF);
            RimOuter = MakePen(0x66, 0x00, 0x00, 0x00);
            Shadow = Color.FromArgb(0x7A, 0x00, 0x00, 0x00);
            Petal = MakeBrush(0x24, 0xFF, 0xFF, 0xFF);
            PetalText = MakeBrush(0xFF, 0xFF, 0xFF, 0xFF);
            PetalIcon = MakeBrush(0xE6, 0xFF, 0xFF, 0xFF);
            PetalSubtext = MakeBrush(0xB3, 0xFF, 0xFF, 0xFF);
            Track = MakeBrush(0x17, 0xFF, 0xFF, 0xFF);
            Center = MakeBrush(0x12, 0xFF, 0xFF, 0xFF);
            CenterRim = MakePen(0x1F, 0xFF, 0xFF, 0xFF);
            Pill = MakeBrush(0x2E, 0xFF, 0xFF, 0xFF);
            PillHover = MakeBrush(0x47, 0xFF, 0xFF, 0xFF);
            PillRim = MakePen(0x1F, 0xFF, 0xFF, 0xFF);
            Text = MakeBrush(0xFF, 0xFF, 0xFF, 0xFF);
            TextSecondary = MakeBrush(0xC5, 0xFF, 0xFF, 0xFF);
            TextTertiary = MakeBrush(0x8A, 0xFF, 0xFF, 0xFF);
            AcrylicTint = 0x40202020;
        }
    }

    public static WheelPalette Dark(AccentColors? accent = null) => new(false, accent ?? AccentColors.Default);
    public static WheelPalette Light(AccentColors? accent = null) => new(true, accent ?? AccentColors.Default);

    /// <summary>По текущей теме приложений Windows и её акценту.</summary>
    public static WheelPalette FromSystem() => new(SystemTheme.AppsLight, SystemTheme.Accent);

    public bool IsLight { get; }
    /// <summary>Подкраска диска поверх системного размытия.</summary>
    public Brush DiscTint { get; }
    /// <summary>Диск без размытия (эффекты прозрачности выключены).</summary>
    public Brush DiscSolid { get; }
    /// <summary>Тонкий светлый блик по краю диска.</summary>
    public Pen RimHighlight { get; }
    /// <summary>Тонкая тёмная кромка снаружи (видна на светлых обоях).</summary>
    public Pen RimOuter { get; }
    public Color Shadow { get; }
    public Brush Petal { get; }
    public Brush PetalText { get; }
    public Brush PetalIcon { get; }
    public Brush PetalSubtext { get; }
    /// <summary>Пустое кольцо, пока сектора неизвестны.</summary>
    public Brush Track { get; }
    public Brush Accent { get; }
    public Brush OnAccent { get; }
    /// <summary>Вспышка принятого лепестка.</summary>
    public Brush Pulse { get; }
    public Brush Center { get; }
    public Pen CenterRim { get; }
    public Brush Pill { get; }
    public Brush PillHover { get; }
    public Pen PillRim { get; }
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
