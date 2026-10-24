using System.Globalization;

namespace DropWheel.Conversions;

/// <summary>
/// Фон «Оформить…» (D-BEAUTIFY): ключ (в CLI и --style), холст для ImageMagick (<c>gradient:#a-#b</c>, <c>xc:#fff</c>,
/// <c>xc:none</c>) и цвета для кружка-образца в окне (From = To — сплошной; Transparent — шахматка).
/// </summary>
public sealed record BeautifyBackground(string Key, string Canvas, string From, string To)
{
    public bool IsGradient => Canvas.StartsWith("gradient:", StringComparison.Ordinal);
    public bool IsTransparent => Canvas == "xc:none";
}

/// <summary>
/// Оформление картинки: фон (ключ из <see cref="ImageBeautify.Backgrounds"/>), отступ в % от короткой стороны,
/// радиус скругления углов в пикселях исходника, тень. Запись для CLI — «bg=sunset;pad=8;radius=12;shadow=1».
/// </summary>
public sealed record BeautifyStyle(string Background, int Padding, int Radius, bool Shadow)
{
    public static BeautifyStyle Default { get; } = new("sunset", 8, 12, true);

    /// <summary>«bg=sunset;pad=8;radius=12;shadow=1».</summary>
    public string Format() => string.Create(CultureInfo.InvariantCulture,
        $"bg={Background};pad={Padding};radius={Radius};shadow={(Shadow ? 1 : 0)}");

    public override string ToString() => Format();

    /// <summary>
    /// «bg=sky;pad=10;radius=0;shadow=0» — пары через «;» или «,», чего нет — по умолчанию (<see cref="Default"/>).
    /// Ключи: bg (background), pad (padding), radius, shadow (1/0, on/off, yes/no, true/false).
    /// Неизвестный ключ, фон или число вне диапазона — false.
    /// </summary>
    public static bool TryParse(string? text, out BeautifyStyle style)
    {
        style = Default;
        if (text is null) return false;
        var s = Default;
        foreach (var part in text.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) return false;
            var key = part[..eq].Trim().ToLowerInvariant();
            var value = part[(eq + 1)..].Trim();
            switch (key)
            {
                case "bg" or "background":
                    if (ImageBeautify.FindBackground(value) is not { } bg) return false;
                    s = s with { Background = bg.Key };
                    break;
                case "pad" or "padding":
                    if (!Int(value.TrimEnd('%'), ImageBeautify.MinPadding, ImageBeautify.MaxPadding, out var pad)) return false;
                    s = s with { Padding = pad };
                    break;
                case "radius":
                    if (!Int(value, ImageBeautify.MinRadius, ImageBeautify.MaxRadius, out var radius)) return false;
                    s = s with { Radius = radius };
                    break;
                case "shadow":
                    switch (value.ToLowerInvariant())
                    {
                        case "1" or "on" or "yes" or "true": s = s with { Shadow = true }; break;
                        case "0" or "off" or "no" or "false": s = s with { Shadow = false }; break;
                        default: return false;
                    }
                    break;
                default:
                    return false;
            }
        }
        style = s;
        return true;
    }

    private static bool Int(string text, int min, int max, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
}

/// <summary>Размеры одного оформления в пикселях: отступ, радиус, размытие и сдвиг тени, размер результата.</summary>
public readonly record struct BeautifyLayout(int Pad, int Radius, int Blur, int Offset, int Width, int Height);

/// <summary>
/// «Оформить…» (D-BEAUTIFY): картинка со скруглёнными углами и тенью на фоне с отступами, одним вызовом ImageMagick.
/// Шаблон — в каталоге; здесь — значения его параметров по стилю и размеру картинки (<see cref="Values"/>).
/// Предпросмотр окна — тот же шаблон на уменьшенной копии (<see cref="ForPreview"/>).
/// </summary>
public static class ImageBeautify
{
    public const string Id = "img-beautify";
    public const string MaskKey = "mask", PadKey = "pad", ShadowKey = "shadow", CanvasKey = "canvas", BackgroundKey = "bg";
    public static readonly IReadOnlyList<string> ParameterNames = [MaskKey, PadKey, ShadowKey, CanvasKey, BackgroundKey];

    public const int MinPadding = 0, MaxPadding = 25;
    public const int MinRadius = 0, MaxRadius = 100;
    /// <summary>Непрозрачность тени, %.</summary>
    public const int ShadowOpacity = 40;
    /// <summary>Сторона копии для предпросмотра (меньшие картинки не увеличиваются).</summary>
    public const int PreviewSide = 1000;

    /// <summary>
    /// Шаблон аргументов (одна команда): повернуть по EXIF, sRGB (CMYK/серые — к общему виду с фоном);
    /// скруглить углы — DstIn с белым скруглённым прямоугольником на прозрачном (прозрачность исходника сохраняется);
    /// сдвинуть на отступ; тень — из копии (-shadow сам сдвигает её на размытие), под картинку;
    /// холст нужного размера — первым слоем, -layers flatten рисует всё на нём (что за краем — обрезается); 8 бит.
    /// </summary>
    public const string Args =
        "-auto-orient -colorspace sRGB +repage -alpha set " +
        "( +clone -alpha transparent -fill white -draw {mask} ) -compose DstIn -composite " +
        "-repage +{pad}+{pad} " +
        "( +clone -background black -shadow {shadow} ) +swap " +
        "( -size {canvas} -define gradient:angle=135 {bg} ) -insert 0 " +
        "-background none -compose Over -layers flatten -depth 8";

    /// <summary>Фоны: три мягких градиента, белый, светло-серый, тёмный, прозрачный.</summary>
    public static readonly IReadOnlyList<BeautifyBackground> Backgrounds =
    [
        new("sunset", "gradient:#ff9a8b-#a18cd1", "#ff9a8b", "#a18cd1"),
        new("sky", "gradient:#a1c4fd-#c2e9fb", "#a1c4fd", "#c2e9fb"),
        new("mint", "gradient:#d4fc79-#96e6a1", "#d4fc79", "#96e6a1"),
        new("white", "xc:#ffffff", "#ffffff", "#ffffff"),
        new("grey", "xc:#eceef1", "#eceef1", "#eceef1"),
        new("dark", "xc:#1f2328", "#1f2328", "#1f2328"),
        new("none", "xc:none", "#00000000", "#00000000"),
    ];

    /// <summary>Фон по ключу (без учёта регистра; «transparent» — то же, что «none», «gray» — «grey»); null — нет такого.</summary>
    public static BeautifyBackground? FindBackground(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var k = key.Trim().ToLowerInvariant() switch
        {
            "transparent" => "none",
            "gray" => "grey",
            var other => other,
        };
        return Backgrounds.FirstOrDefault(b => b.Key == k);
    }

    /// <summary>Подпись фона на языке интерфейса («Закат» / «Sunset»).</summary>
    public static string Label(BeautifyBackground background) => background.Key switch
    {
        "sunset" => Localization.Loc.T.BgSunset,
        "sky" => Localization.Loc.T.BgSky,
        "mint" => Localization.Loc.T.BgMint,
        "white" => Localization.Loc.T.BgWhite,
        "grey" => Localization.Loc.T.BgGrey,
        "dark" => Localization.Loc.T.BgDark,
        _ => Localization.Loc.T.BgNone,
    };

    /// <summary>
    /// Размеры для картинки w × h: отступ — % от короткой стороны; радиус — не больше половины короткой стороны;
    /// тень — размытие 2,5 % короткой стороны (2…100 px), сдвиг вниз — 0,6 размытия. Результат — w + 2·отступ × h + 2·отступ.
    /// </summary>
    public static BeautifyLayout Measure(BeautifyStyle style, int w, int h)
    {
        if (w < 1 || h < 1) throw new ArgumentException($"Неверный размер картинки {w}×{h}");
        var shortSide = Math.Min(w, h);
        var pad = (int)Math.Round(shortSide * Math.Clamp(style.Padding, MinPadding, MaxPadding) / 100.0, MidpointRounding.AwayFromZero);
        var radius = Math.Clamp(style.Radius, 0, shortSide / 2);
        var blur = Math.Clamp((int)Math.Round(shortSide * 0.025, MidpointRounding.AwayFromZero), 2, 100);
        var offset = (int)Math.Round(blur * 0.6, MidpointRounding.AwayFromZero);
        return new BeautifyLayout(pad, radius, blur, offset, w + 2 * pad, h + 2 * pad);
    }

    /// <summary>Значения {mask} {pad} {shadow} {canvas} {bg} для картинки w × h (уже повёрнутой по EXIF).</summary>
    public static IReadOnlyDictionary<string, string> Values(BeautifyStyle style, int w, int h)
    {
        var l = Measure(style, w, h);
        var bg = FindBackground(style.Background) ?? throw new ArgumentException($"Нет фона «{style.Background}»");
        var inv = CultureInfo.InvariantCulture;
        return new Dictionary<string, string>
        {
            // roundrectangle с радиусом 0 ImageMagick 6 не рисует вовсе (картинка пропала бы) — тогда обычный прямоугольник.
            [MaskKey] = l.Radius > 0
                ? string.Create(inv, $"roundrectangle 0,0 {w - 1},{h - 1} {l.Radius},{l.Radius}")
                : string.Create(inv, $"rectangle 0,0 {w - 1},{h - 1}"),
            [PadKey] = l.Pad.ToString(inv),
            // Без тени — та же команда с прозрачной тенью: шаблон один, условий в каталоге нет.
            [ShadowKey] = style.Shadow ? string.Create(inv, $"{ShadowOpacity}x{l.Blur}+0+{l.Offset}") : "0x1+0+0",
            [CanvasKey] = string.Create(inv, $"{l.Width}x{l.Height}"),
            [BackgroundKey] = bg.Canvas,
        };
    }

    /// <summary>Операция «Оформить» для картинки w × h со стилем style.</summary>
    public static Operation Bind(BeautifyStyle style, int w, int h) =>
        OperationCatalog.Find(Id)!.WithValues(Values(style, w, h));

    /// <summary>
    /// Стиль для уменьшенной копии: радиус — в масштабе копии (отступ и тень и так считаются от её размера),
    /// чтобы предпросмотр выглядел как результат.
    /// </summary>
    public static BeautifyStyle ForPreview(BeautifyStyle style, int fullW, int fullH, int previewW, int previewH)
    {
        if (fullW < 1 || fullH < 1 || previewW < 1 || previewH < 1) return style;
        var scale = Math.Min(1.0, Math.Min(previewW / (double)fullW, previewH / (double)fullH));
        return style with { Radius = (int)Math.Round(style.Radius * scale, MidpointRounding.AwayFromZero) };
    }
}
