using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using DropWheel.Conversions;
using DropWheel.Ui;
using DropWheel.Localization;

namespace DropWheel.Wheel;

/// <summary>
/// Круговое меню в духе Tangerine (macOS), по-виндовому: матовый диск, отдельные скруглённые «лепестки»
/// с зазорами, в центре круг с «пилюлей». Пилюля показывает размер файлов, имя формата под курсором
/// или «Отмена». Конвертация — крупные названия форматов ПРОПИСНЫМИ без значков; инструменты — значок над подписью.
/// Рисуется целиком в OnRender; центр колеса — центр элемента.
/// Наведение, смена режима и «приём» файла анимируются (если анимации Windows включены и элемент
/// показан на экране); иначе состояние сразу конечное — так снимаются скриншоты.
/// </summary>
public sealed partial class WheelControl : FrameworkElement
{
    /// <summary>Радиус центра («Отмена») для попадания курсора, DIP.</summary>
    public const double InnerRadius = 62;
    /// <summary>Внешний радиус кольца лепестков для попадания курсора, DIP.</summary>
    public const double OuterRadius = DiscRadius;
    /// <summary>Радиус диска (фон колеса), DIP: диаметр 288.</summary>
    public const double DiscRadius = 144;
    /// <summary>Насколько тень выходит за диск, DIP.</summary>
    public const double ShadowSpread = 12;

    private const double CenterRadius = 60;      // ~42 % диаметра
    private const double PetalGap = 5;
    private const double PetalInner = CenterRadius + PetalGap;
    private const double PetalOuter = DiscRadius - PetalGap;
    private const double PetalCorner = 10;
    private const double Lift = 2.5;
    private const double ContentRadius = (PetalInner + PetalOuter) / 2;
    private const double PillHeight = 28;
    private const double PillMinWidth = 72;
    private const double CaptionOffset = 30;
    private const double DotsOffset = 29;

    private const double HoverInMs = 90;
    private const double HoverOutMs = 120;
    private const double SwitchMs = 190;
    private const double PulseMs = 150;
    private const double SwitchAngle = 14;

    private static string NothingText => Loc.T.WheelNothing;
    private static string CancelText => Loc.T.Cancel;

    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<WheelItem>), typeof(WheelControl),
        new FrameworkPropertyMetadata(Array.Empty<WheelItem>(), FrameworkPropertyMetadataOptions.AffectsRender,
            (d, e) => ((WheelControl)d).OnItemsChanged((IReadOnlyList<WheelItem>)e.OldValue, (IReadOnlyList<WheelItem>)e.NewValue)));

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(OperationMode), typeof(WheelControl),
        new FrameworkPropertyMetadata(OperationMode.Convert, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverIndexProperty = DependencyProperty.Register(
        nameof(HoverIndex), typeof(int?), typeof(WheelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnAnimatedChanged));

    public static readonly DependencyProperty IsCenterHoveredProperty = DependencyProperty.Register(
        nameof(IsCenterHovered), typeof(bool), typeof(WheelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnAnimatedChanged));

    public static readonly DependencyProperty IsDotsHoveredProperty = DependencyProperty.Register(
        nameof(IsDotsHovered), typeof(bool), typeof(WheelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnAnimatedChanged));

    public static readonly DependencyProperty IsPendingProperty = DependencyProperty.Register(
        nameof(IsPending), typeof(bool), typeof(WheelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowModeDotsProperty = DependencyProperty.Register(
        nameof(ShowModeDots), typeof(bool), typeof(WheelControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IdleTextProperty = DependencyProperty.Register(
        nameof(IdleText), typeof(string), typeof(WheelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EmptyHintProperty = DependencyProperty.Register(
        nameof(EmptyHint), typeof(string), typeof(WheelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(WheelPalette), typeof(WheelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop), typeof(WheelBackdrop), typeof(WheelControl),
        new FrameworkPropertyMetadata(WheelBackdrop.Solid, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DrawShadowProperty = DependencyProperty.Register(
        nameof(DrawShadow), typeof(bool), typeof(WheelControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly FrameTicker _ticker;
    private readonly Dictionary<int, Geometry[]> _petals = [];

    // Анимация: 0..1 по каждому лепестку (наведение), центру и точкам режима.
    private double[] _hover = [];
    private double _center;
    private double _dots;
    // Смена набора (режим, первые сектора после DragEnter): старый набор уходит, новый приходит.
    private IReadOnlyList<WheelItem> _oldItems = [];
    private double[] _oldHover = [];
    private OperationMode _oldMode;
    private double _switch = 1;
    // «Приём» файла: короткая вспышка лепестка.
    private int _pulseIndex = -1;
    private double _pulse = 1;

    public WheelControl()
    {
        _ticker = new FrameTicker(Tick);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) SnapAnimations();
        };
    }

    /// <summary>Сектора (не больше 8). Пусто — «Нечего предложить».</summary>
    public IReadOnlyList<WheelItem> Items
    {
        get => (IReadOnlyList<WheelItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value ?? Array.Empty<WheelItem>());
    }

    /// <summary>Режим: «Конвертация» — названия форматов прописными, «Инструменты» — значок и подпись.</summary>
    public OperationMode Mode
    {
        get => (OperationMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>Подсвеченный сектор; null или −1 — нет.</summary>
    public int? HoverIndex
    {
        get => (int?)GetValue(HoverIndexProperty);
        set => SetValue(HoverIndexProperty, value is < 0 ? null : value);
    }

    /// <summary>Курсор над центром — в пилюле «Отмена».</summary>
    public bool IsCenterHovered
    {
        get => (bool)GetValue(IsCenterHoveredProperty);
        set => SetValue(IsCenterHoveredProperty, value);
    }

    /// <summary>Курсор над точками режима (их можно нажать в режиме клика).</summary>
    public bool IsDotsHovered
    {
        get => (bool)GetValue(IsDotsHoveredProperty);
        set => SetValue(IsDotsHoveredProperty, value);
    }

    /// <summary>
    /// Сектора ещё не известны (колесо показано, DragEnter ещё не пришёл):
    /// при пустых Items рисуется пустое кольцо и центр, без «Нечего предложить».
    /// </summary>
    public bool IsPending
    {
        get => (bool)GetValue(IsPendingProperty);
        set => SetValue(IsPendingProperty, value);
    }

    /// <summary>Есть оба режима — под пилюлей две точки (текущий режим ярче).</summary>
    public bool ShowModeDots
    {
        get => (bool)GetValue(ShowModeDotsProperty);
        set => SetValue(ShowModeDotsProperty, value);
    }

    /// <summary>Текст пилюли, когда ни на что не наведено — общий размер файлов («1,1 МБ»). null — «Отмена».</summary>
    public string? IdleText
    {
        get => (string?)GetValue(IdleTextProperty);
        set => SetValue(IdleTextProperty, value);
    }

    /// <summary>Мелкая подсказка под «Нечего предложить».</summary>
    public string? EmptyHint
    {
        get => (string?)GetValue(EmptyHintProperty);
        set => SetValue(EmptyHintProperty, value);
    }

    /// <summary>Цвета; null — по теме Windows.</summary>
    public WheelPalette? Palette
    {
        get => (WheelPalette?)GetValue(PaletteProperty);
        set => SetValue(PaletteProperty, value);
    }

    public WheelBackdrop Backdrop
    {
        get => (WheelBackdrop)GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    /// <summary>Рисовать тень вокруг диска. Окно с размытием выключает её: там тень рисует отдельное окно под колесом.</summary>
    public bool DrawShadow
    {
        get => (bool)GetValue(DrawShadowProperty);
        set => SetValue(DrawShadowProperty, value);
    }

    /// <summary>Сектор под точкой (в координатах элемента); null — центр, вне кольца или пусто.</summary>
    public int? HitTest(Point p)
    {
        var (dx, dy) = Offset(p);
        return WheelGeometry.HitTest(dx, dy, Items.Count, InnerRadius, OuterRadius);
    }

    /// <summary>Точка в центральном круге.</summary>
    public bool IsCenter(Point p)
    {
        var (dx, dy) = Offset(p);
        return WheelGeometry.IsCenter(dx, dy, InnerRadius);
    }

    /// <summary>Точка на точках режима (только если они показаны).</summary>
    public bool IsOnModeDots(Point p)
    {
        if (!ShowModeDots || Items.Count == 0) return false;
        var (dx, dy) = Offset(p);
        return Math.Abs(dx) <= 22 && Math.Abs(dy - DotsOffset) <= 9;
    }

    /// <summary>Точка на диске колеса (вне диска — прозрачный угол окна).</summary>
    public bool IsOnDisc(Point p)
    {
        var (dx, dy) = Offset(p);
        return dx * dx + dy * dy <= DiscRadius * DiscRadius;
    }

    /// <summary>Вспышка лепестка: файл принят.</summary>
    public void Pulse(int index)
    {
        if (index < 0 || index >= Items.Count) return;
        _pulseIndex = index;
        _pulse = 0;
        Kick();
    }

    /// <summary>Все анимации — сразу в конечное состояние.</summary>
    public void SnapAnimations()
    {
        _ticker.Stop();
        var hover = HoverTarget;
        if (_hover.Length != Items.Count) _hover = new double[Items.Count];
        for (var i = 0; i < _hover.Length; i++) _hover[i] = i == hover ? 1 : 0;
        _center = IsCenterHovered ? 1 : 0;
        _dots = IsDotsHovered ? 1 : 0;
        _oldItems = [];
        _oldHover = [];
        _switch = 1;
        _pulse = 1;
        _pulseIndex = -1;
        InvalidateVisual();
    }

    /// <summary>Мягкая тень вокруг диска радиуса discRadius с центром c (общая с окном тени).</summary>
    public static void DrawDiscShadow(DrawingContext dc, Point c, double discRadius, Color shadow)
    {
        Color A(double k) => Color.FromArgb((byte)Math.Round(shadow.A * k), shadow.R, shadow.G, shadow.B);
        var r = discRadius + ShadowSpread;
        var edge = (discRadius - 2) / r;
        var brush = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(A(0), 0),
                new GradientStop(A(0), edge),
                new GradientStop(A(1), edge + 0.012),
                new GradientStop(A(0.35), edge + (1 - edge) * 0.45),
                new GradientStop(A(0), 1),
            },
        };
        brush.Freeze();
        dc.DrawEllipse(brush, null, new Point(c.X, c.Y + 3), r, r);
    }

    protected override Size MeasureOverride(Size availableSize) => new(DiscRadius * 2, DiscRadius * 2);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateVisual();

    // ---- анимация ----

    private int? HoverTarget => HoverIndex is { } h && h < Items.Count ? h : null;

    /// <summary>Элемент на экране — анимировать имеет смысл.</summary>
    private bool IsLive => IsVisible && PresentationSource.FromVisual(this) is not null;

    private static void OnAnimatedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WheelControl)d).Kick();

    private void OnItemsChanged(IReadOnlyList<WheelItem> oldItems, IReadOnlyList<WheelItem> newItems)
    {
        if (Motion.Enabled && IsLive && !ReferenceEquals(oldItems, newItems) && (oldItems.Count > 0 || newItems.Count > 0))
        {
            _oldItems = oldItems;
            _oldHover = _hover.Length == oldItems.Count ? _hover : new double[oldItems.Count];
            // WheelWindow меняет Mode после Items: режим старого набора — ещё текущий.
            _oldMode = Mode;
            _switch = 0;
        }
        else
        {
            _oldItems = [];
            _oldHover = [];
            _switch = 1;
        }
        _hover = new double[newItems.Count];
        _pulseIndex = -1;
        _pulse = 1;
        Kick();
    }

    private void Kick()
    {
        if (_hover.Length != Items.Count) _hover = new double[Items.Count];
        if (!Motion.Enabled || !IsLive)
        {
            SnapAnimations();
            return;
        }
        _ticker.Start();
    }

    /// <summary>Наведение проявляется за 90 мс, гаснет за 120 мс.</summary>
    private static double Step(double value, double target, double dt) =>
        Motion.Approach(value, target, dt, target > value ? HoverInMs : HoverOutMs);

    private bool Tick(double dt)
    {
        var busy = false;
        var hover = HoverTarget;
        if (_hover.Length != Items.Count) _hover = new double[Items.Count];
        for (var i = 0; i < _hover.Length; i++)
        {
            var target = i == hover ? 1.0 : 0.0;
            _hover[i] = Step(_hover[i], target, dt);
            busy |= _hover[i] != target;
        }
        var centerTarget = IsCenterHovered ? 1.0 : 0.0;
        _center = Step(_center, centerTarget, dt);
        busy |= _center != centerTarget;
        var dotsTarget = IsDotsHovered ? 1.0 : 0.0;
        _dots = Step(_dots, dotsTarget, dt);
        busy |= _dots != dotsTarget;
        if (_switch < 1)
        {
            _switch = Math.Min(1, _switch + dt / SwitchMs);
            if (_switch >= 1)
            {
                _oldItems = [];
                _oldHover = [];
            }
            else busy = true;
        }
        if (_pulse < 1)
        {
            _pulse = Math.Min(1, _pulse + dt / PulseMs);
            busy |= _pulse < 1;
        }
        InvalidateVisual();
        return busy;
    }

    // ---- рисование ----

    protected override void OnRender(DrawingContext dc)
    {
        var p = Palette ?? WheelPalette.FromSystem();
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        DrawDisc(dc, p, c);

        var items = Items;
        if (items.Count == 0 && _oldItems.Count == 0)
        {
            if (IsPending)
            {
                DrawTrack(dc, p, c);
                DrawCenter(dc, p, c, [], [], dpi);
            }
            else
            {
                DrawNothing(dc, p, c, dpi);
            }
            return;
        }

        var t = Motion.EaseOutCubic(_switch);
        if (_switch < 1 && _oldItems.Count > 0)
            DrawPetals(dc, p, c, _oldItems, _oldMode, _oldHover, 1 - t, SwitchAngle * t, pulse: false, dpi);

        if (items.Count > 0)
        {
            var fresh = _switch < 1;
            DrawPetals(dc, p, c, items, Mode, _hover, fresh ? t : 1, fresh ? -SwitchAngle * (1 - t) : 0, pulse: true, dpi);
            DrawCenter(dc, p, c, items, _hover, dpi);
        }
        else
        {
            dc.PushOpacity(t);
            DrawNothing(dc, p, c, dpi);
            dc.Pop();
        }
    }

    private void DrawDisc(DrawingContext dc, WheelPalette p, Point c)
    {
        if (DrawShadow) DrawDiscShadow(dc, c, DiscRadius, p.Shadow);
        dc.DrawEllipse(Backdrop == WheelBackdrop.Solid ? p.DiscSolid : p.DiscTint, null, c, DiscRadius, DiscRadius);
        dc.DrawEllipse(null, p.RimOuter, c, DiscRadius - 0.5, DiscRadius - 0.5);
        dc.DrawEllipse(null, p.RimHighlight, c, DiscRadius - 1.5, DiscRadius - 1.5);
    }

    /// <summary>Пока сектора неизвестны — пустое кольцо на месте лепестков.</summary>
    private static void DrawTrack(DrawingContext dc, WheelPalette p, Point c)
    {
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new EllipseGeometry(c, PetalOuter, PetalOuter));
        ring.Children.Add(new EllipseGeometry(c, PetalInner, PetalInner));
        ring.Freeze();
        dc.DrawGeometry(p.Track, null, ring);
    }

    private void DrawPetals(DrawingContext dc, WheelPalette p, Point c, IReadOnlyList<WheelItem> items, OperationMode mode,
        double[] hover, double opacity, double rotation, bool pulse, double dpi)
    {
        if (opacity <= 0.001) return;
        var pops = 0;
        if (opacity < 1) { dc.PushOpacity(opacity); pops++; }
        if (rotation != 0) { dc.PushTransform(new RotateTransform(rotation, c.X, c.Y)); pops++; }

        var count = items.Count;
        var petals = PetalGeometries(count);
        for (var i = 0; i < count; i++)
        {
            var h = i < hover.Length ? Motion.EaseOutCubic(hover[i]) : 0;
            var mid = WheelGeometry.MidAngle(i, count);
            // Лепесток под курсором чуть «приподнимается» наружу (у единственного лепестка-кольца — нет).
            var (lx, ly) = count == 1 ? (0.0, 0.0) : WheelGeometry.PointAt(mid, Lift * h);
            var (cx, cy) = count == 1 ? (0.0, -ContentRadius) : WheelGeometry.PointAt(mid, ContentRadius);
            var k = pulse && i == _pulseIndex && _pulse < 1 ? Math.Sin(Math.PI * _pulse) : 0;

            dc.PushTransform(new TranslateTransform(c.X + lx, c.Y + ly));
            if (k > 0) dc.PushTransform(new ScaleTransform(1 + 0.05 * k, 1 + 0.05 * k, cx, cy));

            var g = petals[i];
            dc.DrawGeometry(p.Petal, null, g);
            if (h > 0.001)
            {
                dc.PushOpacity(h);
                dc.DrawGeometry(p.Accent, null, g);
                dc.Pop();
            }
            if (k > 0)
            {
                dc.PushOpacity(k);
                dc.DrawGeometry(p.Pulse, null, g);
                dc.Pop();
            }

            var width = LabelWidth(mid, count);
            if (mode == OperationMode.Convert) DrawFormatLabel(dc, p, new Point(cx, cy), items[i], h, width, dpi);
            else DrawToolLabel(dc, p, new Point(cx, cy), items[i], h, width, dpi);

            if (k > 0) dc.Pop();
            dc.Pop();
        }

        for (var i = 0; i < pops; i++) dc.Pop();
    }

    /// <summary>Рисует содержимое дважды с перекрёстным проявлением: обычные цвета → «текст на акценте».</summary>
    private static void CrossFade(DrawingContext dc, double h, Action<bool> draw)
    {
        if (h < 0.999)
        {
            if (h > 0.001) dc.PushOpacity(1 - h);
            draw(false);
            if (h > 0.001) dc.Pop();
        }
        if (h > 0.001)
        {
            if (h < 0.999) dc.PushOpacity(h);
            draw(true);
            if (h < 0.999) dc.Pop();
        }
    }

    /// <summary>Конвертация: «PNG» крупно прописными, уточнение из скобок — мелко под ним («страницы»).</summary>
    private static void DrawFormatLabel(DrawingContext dc, WheelPalette p, Point at, WheelItem item, double h,
        double width, double dpi)
    {
        var (main, sub) = SplitLabel(item.Label);
        var mainFace = UiFonts.Face(UiFonts.Display, FontWeights.SemiBold);
        var mainSize = FitSize(main, mainFace, 15, width, 10.5, dpi);
        var subFace = UiFonts.Face(UiFonts.Text, FontWeights.Normal);
        CrossFade(dc, h, accent =>
        {
            var m = Text(main, mainFace, mainSize, accent ? p.OnAccent : p.PetalText, width, 2, dpi);
            var s = sub is null ? null : Text(sub, subFace, 11, accent ? p.OnAccent : p.PetalSubtext, width, 1, dpi);
            var total = m.Height + (s?.Height ?? 0);
            var top = at.Y - total / 2;
            dc.DrawText(m, new Point(at.X - width / 2, top));
            if (s is not null) dc.DrawText(s, new Point(at.X - width / 2, top + m.Height));
        });
    }

    /// <summary>Инструменты: значок над подписью, подпись как есть («Без метаданных»).</summary>
    private static void DrawToolLabel(DrawingContext dc, WheelPalette p, Point at, WheelItem item, double h,
        double width, double dpi)
    {
        var glyph = OperationGlyphs.For(item.Id);
        var glyphFace = UiFonts.Face(UiFonts.Icons, FontWeights.Normal);
        var labelFace = UiFonts.Face(UiFonts.Text, FontWeights.SemiBold);
        var labelSize = FitSize(item.Label, labelFace, 12, width, 10, dpi);
        CrossFade(dc, h, accent =>
        {
            var g = Text(glyph, glyphFace, 18, accent ? p.OnAccent : p.PetalIcon, 40, 1, dpi);
            var l = Text(item.Label, labelFace, labelSize, accent ? p.OnAccent : p.PetalText, width, 2, dpi);
            var total = g.Height + 4 + l.Height;
            var top = at.Y - total / 2;
            dc.DrawText(g, new Point(at.X - 20, top));
            dc.DrawText(l, new Point(at.X - width / 2, top + g.Height + 4));
        });
    }

    /// <summary>
    /// Центр: круг с «пилюлей». В пилюле — размер файлов, имя лепестка под курсором (жирно) или «Отмена»;
    /// тексты плавно сменяют друг друга, ширина пилюли подстраивается. Над ней — режим, под ней — точки режима.
    /// </summary>
    private void DrawCenter(DrawingContext dc, WheelPalette p, Point c, IReadOnlyList<WheelItem> items, double[] hover, double dpi)
    {
        dc.DrawEllipse(p.Center, p.CenterRim, c, CenterRadius, CenterRadius);

        var regular = UiFonts.Face(UiFonts.Text, FontWeights.Normal);
        var bold = UiFonts.Face(UiFonts.Text, FontWeights.SemiBold);
        if (items.Count > 0)
        {
            var caption = Mode == OperationMode.Convert ? Loc.T.ModeConvert : Loc.T.ModeTools;
            var ft = Text(caption, regular, 10, p.TextTertiary, 2 * CenterRadius - 20, 1, dpi);
            dc.DrawText(ft, new Point(c.X - ft.MaxTextWidth / 2, c.Y - CaptionOffset - ft.Height / 2));
        }

        // Кандидаты в пилюлю с весами: лепестки под курсором, «Отмена» над центром, иначе — размер файлов.
        var center = Motion.EaseOutCubic(_center);
        var entries = new List<(string Text, Typeface Face, double Weight)>();
        var used = center;
        for (var i = 0; i < items.Count && i < hover.Length; i++)
        {
            var h = Motion.EaseOutCubic(hover[i]);
            if (h <= 0.001) continue;
            entries.Add((PillName(items[i], Mode), bold, h));
            used = Math.Max(used, h);
        }
        if (center > 0.001) entries.Add((CancelText, bold, center));
        if (used < 0.999) entries.Add((IdleText is { Length: > 0 } idle ? idle : CancelText, regular, 1 - used));

        var maxWidth = 2 * Math.Sqrt(CenterRadius * CenterRadius - PillHeight * PillHeight / 4) - 10;
        var texts = new List<(FormattedText Text, double Weight)>();
        double pillWidth = 0, weightSum = 0;
        foreach (var (text, face, weight) in entries)
        {
            var size = FitSize(text, face, 13, maxWidth - 20, 10, dpi);
            texts.Add((Text(text, face, size, p.Text, maxWidth - 16, 1, dpi), weight));
            pillWidth += Math.Min(maxWidth, Math.Max(PillMinWidth, Measure(text, face, size, dpi) + 24)) * weight;
            weightSum += weight;
        }
        pillWidth = weightSum > 0 ? pillWidth / weightSum : PillMinWidth;

        var pill = new Rect(c.X - pillWidth / 2, c.Y - PillHeight / 2, pillWidth, PillHeight);
        dc.DrawRoundedRectangle(p.Pill, p.PillRim, pill, PillHeight / 2, PillHeight / 2);
        if (center > 0.001)
        {
            dc.PushOpacity(center);
            dc.DrawRoundedRectangle(p.PillHover, null, pill, PillHeight / 2, PillHeight / 2);
            dc.Pop();
        }
        foreach (var (ft, weight) in texts)
        {
            dc.PushOpacity(Math.Min(1, weight));
            dc.DrawText(ft, new Point(c.X - ft.MaxTextWidth / 2, c.Y - ft.Height / 2));
            dc.Pop();
        }

        if (ShowModeDots && items.Count > 0)
        {
            var dots = Motion.EaseOutCubic(_dots);
            var r = 2.5 + 0.75 * dots;
            var active = Mode == OperationMode.Convert ? 0 : 1;
            for (var i = 0; i < 2; i++)
            {
                var brush = i == active ? p.Text : p.TextTertiary;
                dc.DrawEllipse(brush, null, new Point(c.X + (i == 0 ? -5.5 : 5.5) * (1 + 0.2 * dots), c.Y + DotsOffset), r, r);
            }
        }
    }

    private void DrawNothing(DrawingContext dc, WheelPalette p, Point c, double dpi)
    {
        var glyph = Text(Glyphs.Info, UiFonts.Face(UiFonts.Icons, FontWeights.Normal), 26, p.TextSecondary, 40, 1, dpi);
        var title = Text(NothingText, UiFonts.Face(UiFonts.Display, FontWeights.SemiBold), 16, p.Text, 200, 2, dpi);
        var hint = EmptyHint is { Length: > 0 } h
            ? Text(h, UiFonts.Face(UiFonts.Text, FontWeights.Normal), 12, p.TextSecondary, 190, 2, dpi)
            : null;
        var total = glyph.Height + 8 + title.Height + (hint is null ? 0 : 4 + hint.Height);
        var top = c.Y - total / 2;
        dc.DrawText(glyph, new Point(c.X - 20, top));
        top += glyph.Height + 8;
        dc.DrawText(title, new Point(c.X - 100, top));
        if (hint is not null) dc.DrawText(hint, new Point(c.X - 95, top + title.Height + 4));
    }

    // ---- подписи ----

    [GeneratedRegex(@"^(?<main>.+?)\s*\((?<sub>.+)\)$")]
    private static partial Regex LabelWithNote();

    /// <summary>«PNG (страницы)» → («PNG», «страницы»); «WebP» → («WEBP», null).</summary>
    public static (string Main, string? Sub) SplitLabel(string label)
    {
        var m = LabelWithNote().Match(label);
        return m.Success
            ? (m.Groups["main"].Value.ToUpperInvariant(), m.Groups["sub"].Value)
            : (label.ToUpperInvariant(), null);
    }

    /// <summary>Имя в пилюле: формат прописными (уточнение — через точку), инструмент — как есть.</summary>
    private static string PillName(WheelItem item, OperationMode mode)
    {
        if (mode != OperationMode.Convert) return item.Label;
        var (main, sub) = SplitLabel(item.Label);
        return sub is null ? main : $"{main} · {sub}";
    }

    /// <summary>Ширина подписи лепестка: у верхних/нижних — по дуге, у боковых — по толщине кольца.</summary>
    private static double LabelWidth(double midDeg, int count)
    {
        if (count == 1) return 160;
        var arc = 2 * Math.PI * ContentRadius / count - PetalGap - 16;
        var radial = PetalOuter - PetalInner - 12;
        var s = Math.Abs(Math.Sin(midDeg * Math.PI / 180));
        return Math.Clamp(arc * (1 - s) + radial * s, 56, 150);
    }

    // ---- геометрия ----

    /// <summary>Лепестки с центром в (0, 0): кольцевые секторы с зазорами и скруглёнными углами.</summary>
    private Geometry[] PetalGeometries(int count)
    {
        if (_petals.TryGetValue(count, out var cached)) return cached;
        var result = new Geometry[count];
        for (var i = 0; i < count; i++)
            result[i] = TileGeometry.Build(i, count, PetalInner, PetalOuter, PetalGap, PetalCorner);
        _petals[count] = result;
        return result;
    }

    // ---- текст ----

    private (double Dx, double Dy) Offset(Point p) => (p.X - ActualWidth / 2, p.Y - ActualHeight / 2);

    private static double Measure(string text, Typeface face, double size, double dpi) =>
        new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size,
            Brushes.Black, null, TextFormattingMode.Ideal, dpi).WidthIncludingTrailingWhitespace;

    /// <summary>
    /// Размер шрифта, при котором самое длинное слово помещается в ширину (не меньше min):
    /// «Нормализовать» лучше чуть мельче, чем с многоточием.
    /// </summary>
    private static double FitSize(string text, Typeface face, double size, double width, double min, double dpi)
    {
        var widest = 0.0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            widest = Math.Max(widest, Measure(word, face, size, dpi));
        return widest <= width - 2 ? size : Math.Max(min, size * (width - 2) / widest);
    }

    /// <summary>Текст по центру ширины maxWidth; переносится по словам, лишнее — многоточие.</summary>
    private static FormattedText Text(string text, Typeface face, double size, Brush brush, double maxWidth,
        int maxLines, double pixelsPerDip)
    {
        // Мелкий текст — с привязкой к пикселям (чётче на 100%), крупный — «идеальный».
        var mode = size <= 13.5 ? TextFormattingMode.Display : TextFormattingMode.Ideal;
        return new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, size, brush, null, mode, pixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = maxLines,
            TextAlignment = TextAlignment.Center,
            Trimming = TextTrimming.CharacterEllipsis,
        };
    }
}
