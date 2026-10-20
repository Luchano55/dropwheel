using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DropWheel.Conversions;
using DropWheel.Ui;

namespace DropWheel.Wheel;

/// <summary>
/// Круговое меню: кольцо «плиток»-секторов (значок + подпись), «Отмена» в центре, точки режима.
/// Рисуется целиком в OnRender; центр колеса — центр элемента.
/// Наведение, смена режима и «приём» файла анимируются (если анимации Windows включены
/// и элемент показан на экране); иначе состояние сразу конечное — так снимаются скриншоты.
/// </summary>
public sealed class WheelControl : FrameworkElement
{
    /// <summary>Радиус центрального круга («Отмена») для попадания курсора, DIP.</summary>
    public const double InnerRadius = 55;
    /// <summary>Внешний радиус кольца секторов для попадания курсора, DIP.</summary>
    public const double OuterRadius = 130;
    /// <summary>Радиус диска (фон колеса), DIP. Окно делает свою форму по нему.</summary>
    public const double DiscRadius = 136;

    private const double CenterRadius = 50;
    private const double TileInner = 58;
    private const double TileOuter = 127;
    private const double TileGap = 4;
    private const double TileCorner = 7;
    private const double Lift = 4;
    private const double ContentRadius = (TileInner + TileOuter) / 2;
    private const double DotsOffset = 33;
    private const double ShadowSpread = 10;

    private const double HoverMs = 120;
    private const double SwitchMs = 190;
    private const double PulseMs = 150;
    private const double SwitchAngle = 14;

    private const string NothingText = "Нечего предложить";
    private const string CancelText = "Отмена";

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

    public static readonly DependencyProperty EmptyHintProperty = DependencyProperty.Register(
        nameof(EmptyHint), typeof(string), typeof(WheelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PaletteProperty = DependencyProperty.Register(
        nameof(Palette), typeof(WheelPalette), typeof(WheelControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop), typeof(WheelBackdrop), typeof(WheelControl),
        new FrameworkPropertyMetadata(WheelBackdrop.Solid, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly FrameTicker _ticker;
    private readonly Dictionary<int, Geometry[]> _tiles = [];

    // Анимация: 0..1 по каждому сектору (наведение), центру и точкам режима.
    private double[] _hover = [];
    private double _center;
    private double _dots;
    // Смена набора секторов (режим, первые сектора после DragEnter): старый набор уходит, новый приходит.
    private IReadOnlyList<WheelItem> _oldItems = [];
    private double[] _oldHover = [];
    private double _switch = 1;
    // «Приём» файла: короткая вспышка сектора.
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

    /// <summary>Режим — для подписи «Конвертация» / «Инструменты» и точек.</summary>
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

    /// <summary>Курсор над центром — «Отмена» ярче.</summary>
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
    /// при пустых Items рисуется только «Отмена», без «Нечего предложить».
    /// </summary>
    public bool IsPending
    {
        get => (bool)GetValue(IsPendingProperty);
        set => SetValue(IsPendingProperty, value);
    }

    /// <summary>Есть оба режима — внизу центра две точки (текущий режим ярче).</summary>
    public bool ShowModeDots
    {
        get => (bool)GetValue(ShowModeDotsProperty);
        set => SetValue(ShowModeDotsProperty, value);
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
        return Math.Abs(dx) <= 22 && Math.Abs(dy - DotsOffset) <= 11;
    }

    /// <summary>Точка на диске колеса (вне диска — прозрачный угол окна).</summary>
    public bool IsOnDisc(Point p)
    {
        var (dx, dy) = Offset(p);
        return dx * dx + dy * dy <= DiscRadius * DiscRadius;
    }

    /// <summary>Вспышка сектора: файл принят.</summary>
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

    private bool Tick(double dt)
    {
        var busy = false;
        var hover = HoverTarget;
        if (_hover.Length != Items.Count) _hover = new double[Items.Count];
        for (var i = 0; i < _hover.Length; i++)
        {
            var target = i == hover ? 1.0 : 0.0;
            _hover[i] = Motion.Approach(_hover[i], target, dt, HoverMs);
            busy |= _hover[i] != target;
        }
        _center = Motion.Approach(_center, IsCenterHovered ? 1 : 0, dt, HoverMs);
        busy |= _center != (IsCenterHovered ? 1 : 0);
        _dots = Motion.Approach(_dots, IsDotsHovered ? 1 : 0, dt, HoverMs);
        busy |= _dots != (IsDotsHovered ? 1 : 0);
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
            DrawTiles(dc, p, c, _oldItems, _oldHover, 1 - t, SwitchAngle * t, pulse: false, dpi);

        if (items.Count > 0)
        {
            var fresh = _switch < 1;
            DrawTiles(dc, p, c, items, _hover, fresh ? t : 1, fresh ? -SwitchAngle * (1 - t) : 0, pulse: true, dpi);
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
        if (Backdrop == WheelBackdrop.Solid)
        {
            // Мягкая тень: кольцо градиента за краем диска, чуть вниз.
            var r = DiscRadius + ShadowSpread;
            var edge = (DiscRadius - 2) / r;
            var shadow = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Colors.Transparent, 0),
                    new GradientStop(Colors.Transparent, edge),
                    new GradientStop(p.Shadow, edge + 0.01),
                    new GradientStop(Color.FromArgb(0, p.Shadow.R, p.Shadow.G, p.Shadow.B), 1),
                },
            };
            shadow.Freeze();
            dc.DrawEllipse(shadow, null, new Point(c.X, c.Y + 3), r, r);
        }
        dc.DrawEllipse(Backdrop == WheelBackdrop.Solid ? p.DiscSolid : p.DiscTint, null, c, DiscRadius, DiscRadius);
        dc.DrawEllipse(null, p.RimOuter, c, DiscRadius - 0.5, DiscRadius - 0.5);
        dc.DrawEllipse(null, p.RimInner, c, DiscRadius - 1.5, DiscRadius - 1.5);
    }

    /// <summary>Пока сектора неизвестны — пустая «дорожка» кольца.</summary>
    private static void DrawTrack(DrawingContext dc, WheelPalette p, Point c)
    {
        var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
        ring.Children.Add(new EllipseGeometry(c, TileOuter, TileOuter));
        ring.Children.Add(new EllipseGeometry(c, TileInner, TileInner));
        ring.Freeze();
        dc.DrawGeometry(p.Track, null, ring);
    }

    private void DrawTiles(DrawingContext dc, WheelPalette p, Point c, IReadOnlyList<WheelItem> items, double[] hover,
        double opacity, double rotation, bool pulse, double dpi)
    {
        if (opacity <= 0.001) return;
        var pops = 0;
        if (opacity < 1) { dc.PushOpacity(opacity); pops++; }
        if (rotation != 0) { dc.PushTransform(new RotateTransform(rotation, c.X, c.Y)); pops++; }

        var count = items.Count;
        var geometries = TileGeometries(count);
        for (var i = 0; i < count; i++)
        {
            var h = i < hover.Length ? Motion.EaseOutCubic(hover[i]) : 0;
            var mid = WheelGeometry.MidAngle(i, count);
            // Сектор под курсором чуть «приподнимается» наружу (у единственного сектора-кольца — нет).
            var (lx, ly) = count == 1 ? (0.0, 0.0) : WheelGeometry.PointAt(mid, Lift * h);
            var (cx, cy) = count == 1 ? (0.0, -ContentRadius) : WheelGeometry.PointAt(mid, ContentRadius);
            var k = pulse && i == _pulseIndex && _pulse < 1 ? Math.Sin(Math.PI * _pulse) : 0;

            dc.PushTransform(new TranslateTransform(c.X + lx, c.Y + ly));
            if (k > 0) dc.PushTransform(new ScaleTransform(1 + 0.06 * k, 1 + 0.06 * k, cx, cy));

            var g = geometries[i];
            dc.DrawGeometry(p.Tile, null, g);
            if (h > 0.001)
            {
                dc.PushOpacity(h);
                dc.DrawGeometry(p.Accent, null, g);
                dc.Pop();
            }
            if (k > 0)
            {
                dc.PushOpacity(k);
                dc.DrawGeometry(p.AccentGlow, null, g);
                dc.Pop();
            }

            DrawTileContent(dc, p, new Point(cx, cy), items[i], h, LabelWidth(mid, count), dpi);

            if (k > 0) dc.Pop();
            dc.Pop();
        }

        for (var i = 0; i < pops; i++) dc.Pop();
    }

    /// <summary>Значок над подписью; при наведении цвета плавно переходят в «текст на акценте».</summary>
    private static void DrawTileContent(DrawingContext dc, WheelPalette p, Point at, WheelItem item, double h,
        double width, double dpi)
    {
        var glyph = OperationGlyphs.For(item.Id);
        var glyphFace = UiFonts.Face(UiFonts.Icons, FontWeights.Normal);
        var labelFace = UiFonts.Face(UiFonts.Text, FontWeights.Normal);

        var labelSize = FitSize(item.Label, labelFace, 12.5, width, 10.5, dpi);
        void Draw(Brush glyphBrush, Brush labelBrush)
        {
            var g = Text(glyph, glyphFace, 20, glyphBrush, 40, 1, dpi);
            var l = Text(item.Label, labelFace, labelSize, labelBrush, width, 2, dpi);
            var total = g.Height + 3 + l.Height;
            var top = at.Y - total / 2;
            dc.DrawText(g, new Point(at.X - 20, top));
            dc.DrawText(l, new Point(at.X - width / 2, top + g.Height + 3));
        }

        if (h < 0.999)
        {
            if (h > 0.001) dc.PushOpacity(1 - h);
            Draw(p.TextSecondary, p.Text);
            if (h > 0.001) dc.Pop();
        }
        if (h > 0.001)
        {
            if (h < 0.999) dc.PushOpacity(h);
            Draw(p.OnAccent, p.OnAccent);
            if (h < 0.999) dc.Pop();
        }
    }

    /// <summary>Центр: подпись режима, «Отмена» или крупная подпись сектора под курсором, точки режима.</summary>
    private void DrawCenter(DrawingContext dc, WheelPalette p, Point c, IReadOnlyList<WheelItem> items, double[] hover, double dpi)
    {
        var center = Motion.EaseOutCubic(_center);
        dc.DrawEllipse(p.Center, p.CenterRim, c, CenterRadius, CenterRadius);
        if (center > 0.001)
        {
            dc.PushOpacity(center);
            dc.DrawEllipse(p.CenterHover, null, c, CenterRadius, CenterRadius);
            dc.Pop();
        }

        var text = UiFonts.Face(UiFonts.Text, FontWeights.Normal);
        if (items.Count > 0)
        {
            var caption = Mode == OperationMode.Convert ? "Конвертация" : "Инструменты";
            var ft = Text(caption, text, 10.5, p.TextTertiary, 2 * CenterRadius - 14, 1, dpi);
            dc.DrawText(ft, new Point(c.X - ft.MaxTextWidth / 2, c.Y - 31 - ft.Height / 2));
        }

        // «Отмена» и подписи секторов плавно сменяют друг друга по мере наведения.
        var shown = 0.0;
        for (var i = 0; i < items.Count && i < hover.Length; i++)
        {
            var h = Motion.EaseOutCubic(hover[i]);
            if (h <= 0.001) continue;
            shown = Math.Max(shown, h);
            dc.PushOpacity(h);
            var face = UiFonts.Face(UiFonts.Display, FontWeights.SemiBold);
            var width = 2 * CenterRadius - 12;
            var big = Text(items[i].Label, face, FitSize(items[i].Label, face, 17, width, 12, dpi), p.Text, width, 2, dpi);
            dc.DrawText(big, new Point(c.X - big.MaxTextWidth / 2, c.Y - big.Height / 2));
            dc.Pop();
        }

        if (shown < 0.999)
        {
            dc.PushOpacity(1 - shown);
            var cancelBrush = center > 0.5 ? p.Text : p.TextSecondary;
            var glyph = Text(Glyphs.Cancel, UiFonts.Face(UiFonts.Icons, FontWeights.Normal), 12, cancelBrush, 30, 1, dpi);
            var label = Text(CancelText, text, 13, cancelBrush, 2 * CenterRadius - 14, 1, dpi);
            var total = glyph.Height + 2 + label.Height;
            var top = c.Y - total / 2;
            dc.DrawText(glyph, new Point(c.X - 15, top));
            dc.DrawText(label, new Point(c.X - label.MaxTextWidth / 2, top + glyph.Height + 2));
            dc.Pop();
        }

        if (ShowModeDots && items.Count > 0)
        {
            var dots = Motion.EaseOutCubic(_dots);
            var r = 2.75 + 0.75 * dots;
            var active = Mode == OperationMode.Convert ? 0 : 1;
            for (var i = 0; i < 2; i++)
            {
                var brush = i == active ? p.Text : p.TextTertiary;
                dc.DrawEllipse(brush, null, new Point(c.X + (i == 0 ? -6 : 6) * (1 + 0.2 * dots), c.Y + DotsOffset), r, r);
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

    // ---- геометрия плиток ----

    /// <summary>Ширина подписи сектора: у верхних/нижних — по дуге, у боковых — по толщине кольца.</summary>
    private static double LabelWidth(double midDeg, int count)
    {
        if (count == 1) return 150;
        var arc = 2 * Math.PI * ContentRadius / count - TileGap - 12;
        var radial = TileOuter - TileInner - 10;
        var s = Math.Abs(Math.Sin(midDeg * Math.PI / 180));
        return Math.Clamp(arc * (1 - s) + radial * s, 58, 140);
    }

    /// <summary>Плитки секторов с центром в (0, 0): кольцевые секторы с зазором и скруглёнными углами.</summary>
    private Geometry[] TileGeometries(int count)
    {
        if (_tiles.TryGetValue(count, out var cached)) return cached;
        var result = new Geometry[count];
        for (var i = 0; i < count; i++) result[i] = BuildTile(i, count);
        _tiles[count] = result;
        return result;
    }

    private static Geometry BuildTile(int index, int count)
    {
        if (count == 1)
        {
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd };
            ring.Children.Add(new EllipseGeometry(new Point(), TileOuter, TileOuter));
            ring.Children.Add(new EllipseGeometry(new Point(), TileInner, TileInner));
            ring.Freeze();
            return ring;
        }

        // Скругление: строим плитку меньше на радиус скругления и обводим пером такой же толщины ×2
        // с круглыми углами; объединение — плитка нужного размера со скруглёнными углами.
        var k = TileCorner;
        var core = Sector(index, count, TileInner + k, TileOuter - k, TileGap / 2 + k);
        var pen = new Pen(Brushes.Black, 2 * k) { LineJoin = PenLineJoin.Round };
        var widened = core.GetWidenedPathGeometry(pen);
        var tile = Geometry.Combine(core, widened, GeometryCombineMode.Union, null);
        tile.Freeze();
        return tile;
    }

    /// <summary>Кольцевой сектор между радиусами ri и ro; боковые края параллельны границе сектора на расстоянии halfGap.</summary>
    private static Geometry Sector(int index, int count, double ri, double ro, double halfGap)
    {
        var (start, end) = WheelGeometry.SectorAngles(index, count);
        double Offset(double r) => Math.Asin(Math.Min(1, halfGap / r)) * 180 / Math.PI;
        var so = start + Offset(ro);
        var eo = end - Offset(ro);
        var si = start + Offset(ri);
        var ei = end - Offset(ri);

        Point At(double deg, double r)
        {
            var (x, y) = WheelGeometry.PointAt(deg, r);
            return new Point(x, y);
        }

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(so, ro), isFilled: true, isClosed: true);
            ctx.ArcTo(At(eo, ro), new Size(ro, ro), 0, eo - so > 180, SweepDirection.Clockwise, true, true);
            ctx.LineTo(At(ei, ri), true, true);
            ctx.ArcTo(At(si, ri), new Size(ri, ri), 0, ei - si > 180, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    // ---- текст ----

    /// <summary>
    /// Размер шрифта, при котором самое длинное слово помещается в ширину (не меньше min):
    /// «Нормализовать» лучше чуть мельче, чем с многоточием.
    /// </summary>
    private static double FitSize(string text, Typeface face, double size, double width, double min, double dpi)
    {
        var widest = 0.0;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var ft = new FormattedText(word, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size,
                Brushes.Black, null, TextFormattingMode.Ideal, dpi);
            widest = Math.Max(widest, ft.WidthIncludingTrailingWhitespace);
        }
        return widest <= width - 2 ? size : Math.Max(min, size * (width - 2) / widest);
    }

    private (double Dx, double Dy) Offset(Point p) => (p.X - ActualWidth / 2, p.Y - ActualHeight / 2);

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
