using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DropWheel.Jobs;
using DropWheel.Text;
using DropWheel.Ui;

namespace DropWheel.App;

/// <summary>Цвета окна задач у трея (Windows 11: светлая/тёмная тема, поверх системной подложки или сплошной фон).</summary>
public sealed class FlyoutPalette
{
    private FlyoutPalette(bool light, AccentColors accent, bool overBackdrop)
    {
        IsLight = light;
        OverBackdrop = overBackdrop;
        Accent = B(light ? accent.Dark1 : accent.Light2);
        Link = B(light ? accent.Dark1 : accent.Light2);
        if (light)
        {
            Solid = Color.FromRgb(0xF9, 0xF9, 0xF9);
            Background = overBackdrop ? B(0x8C, 0xF9, 0xF9, 0xF9) : B(Solid);
            Divider = B(0x14, 0x00, 0x00, 0x00);
            RowHover = B(0x0A, 0x00, 0x00, 0x00);
            Text = B(0xE4, 0x00, 0x00, 0x00);
            TextSecondary = B(0x9E, 0x00, 0x00, 0x00);
            TextTertiary = B(0x72, 0x00, 0x00, 0x00);
            Error = B(0xFF, 0xC4, 0x2B, 0x1C);
            Success = B(0xFF, 0x0F, 0x7B, 0x0F);
            Neutral = B(0xFF, 0x8A, 0x8A, 0x8A);
            Track = B(0x1F, 0x00, 0x00, 0x00);
            ThumbBack = B(0x0D, 0x00, 0x00, 0x00);
            ButtonHover = B(0x0F, 0x00, 0x00, 0x00);
            ButtonPressed = B(0x08, 0x00, 0x00, 0x00);
            Focus = B(0xE4, 0x00, 0x00, 0x00);
        }
        else
        {
            Solid = Color.FromRgb(0x2C, 0x2C, 0x2C);
            Background = overBackdrop ? B(0x73, 0x20, 0x20, 0x20) : B(Solid);
            Divider = B(0x17, 0xFF, 0xFF, 0xFF);
            RowHover = B(0x0F, 0xFF, 0xFF, 0xFF);
            Text = B(0xFF, 0xFF, 0xFF, 0xFF);
            TextSecondary = B(0xC5, 0xFF, 0xFF, 0xFF);
            TextTertiary = B(0x87, 0xFF, 0xFF, 0xFF);
            Error = B(0xFF, 0xFF, 0x99, 0xA4);
            Success = B(0xFF, 0x6C, 0xCB, 0x5F);
            Neutral = B(0xFF, 0x9D, 0x9D, 0x9D);
            Track = B(0x29, 0xFF, 0xFF, 0xFF);
            ThumbBack = B(0x14, 0xFF, 0xFF, 0xFF);
            ButtonHover = B(0x14, 0xFF, 0xFF, 0xFF);
            ButtonPressed = B(0x0A, 0xFF, 0xFF, 0xFF);
            Focus = B(0xFF, 0xFF, 0xFF, 0xFF);
        }
        OnBadge = B(light ? Colors.White : Color.FromRgb(0x1C, 0x1C, 0x1C));
    }

    public static FlyoutPalette Light(AccentColors? accent = null, bool overBackdrop = false) =>
        new(true, accent ?? AccentColors.Default, overBackdrop);

    public static FlyoutPalette Dark(AccentColors? accent = null, bool overBackdrop = false) =>
        new(false, accent ?? AccentColors.Default, overBackdrop);

    public bool IsLight { get; }
    /// <summary>Под окном системная подложка (акрил Windows 11) — фон полупрозрачный.</summary>
    public bool OverBackdrop { get; }
    /// <summary>Сплошной цвет фона — для обводки значков-бейджей и подсказок.</summary>
    public Color Solid { get; }
    public Brush Background { get; }
    public Brush Divider { get; }
    public Brush RowHover { get; }
    public Brush Text { get; }
    public Brush TextSecondary { get; }
    public Brush TextTertiary { get; }
    public Brush Accent { get; }
    public Brush Link { get; }
    public Brush Error { get; }
    public Brush Success { get; }
    public Brush Neutral { get; }
    public Brush OnBadge { get; }
    public Brush Track { get; }
    public Brush ThumbBack { get; }
    public Brush ButtonHover { get; }
    public Brush ButtonPressed { get; }
    public Brush Focus { get; }

    /// <summary>Подсказка (ToolTip) в цветах темы.</summary>
    public ToolTip MakeToolTip() => new()
    {
        Background = B(Solid),
        Foreground = Text,
        BorderBrush = Divider,
        Padding = new Thickness(8, 5, 8, 6),
        FontFamily = UiFonts.Text,
        FontSize = 12,
    };

    private static Brush B(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Brush B(byte a, byte r, byte g, byte b) => B(Color.FromArgb(a, r, g, b));
}

/// <summary>Строка окна задач — данные для показа (без ссылок на очередь; тесты собирают её сами).</summary>
public sealed record JobRowModel(object Key, string Name, string Operation, JobState State)
{
    /// <summary>0..1.</summary>
    public double Progress { get; init; }
    /// <summary>Короткая причина ошибки.</summary>
    public string? Error { get; init; }
    /// <summary>Имя результата (файл или папка).</summary>
    public string? ResultName { get; init; }
    /// <summary>Значок, пока нет миниатюры.</summary>
    public string Glyph { get; init; } = Glyphs.Convert;
    /// <summary>Миниатюра файла (превью или значок типа).</summary>
    public ImageSource? Thumbnail { get; init; }
    /// <summary>Миниатюра — значок типа файла, а не превью: рисуется целиком по центру, без обрезки.</summary>
    public bool ThumbnailIsIcon { get; init; }

    /// <summary>Прогресс неизвестен (ImageMagick и встроенные движки его не сообщают).</summary>
    public bool Indeterminate => State == JobState.Running && Progress <= 0;
    public bool IsActive => State is JobState.Queued or JobState.Running;
    public bool IsFinished => !IsActive;
}

/// <summary>
/// Содержимое окна задач у трея (как всплывающие окна Windows 11): заголовок со сводкой и «Очистить»,
/// строки задач с миниатюрой, прогрессом и кнопками, «пусто» с подсказкой. Действия — событиями.
/// </summary>
public sealed class JobsFlyoutView : Border
{
    public const double FlyoutWidth = 380;
    private const double MaxListHeight = 432;

    private readonly string _hotkeyName;
    private readonly List<JobRowView> _views = [];
    private readonly TranslateTransform _slide = new();
    private FlyoutPalette _palette;
    private IReadOnlyList<JobRowModel> _rows = [];

    private TextBlock _summary = null!;
    private FlyoutButton _clear = null!;
    private StackPanel _list = null!;
    private ScrollViewer _scroll = null!;
    private Border _indicator = null!;
    private FrameworkElement _empty = null!;

    public event Action<object>? OpenRequested;
    public event Action<object>? RevealRequested;
    public event Action<object>? CancelRequested;
    public event Action<object>? DetailsRequested;
    public event Action? ClearRequested;

    /// <param name="hotkeyName">Как называть горячую клавишу в подсказке («Пробел»).</param>
    public JobsFlyoutView(FlyoutPalette palette, string hotkeyName)
    {
        _palette = palette;
        _hotkeyName = hotkeyName;
        Width = FlyoutWidth;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(this, UiFonts.Text);
        System.Windows.Documents.TextElement.SetFontSize(this, 13);
        Build();
    }

    public FlyoutPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Build();
            SetRows(_rows);
        }
    }

    /// <summary>Сдвиг содержимого при появлении окна (окно «выезжает» от панели задач).</summary>
    public TranslateTransform Slide => _slide;

    /// <summary>Строки сверху вниз (новые — сверху). Строки с тем же Key обновляются на месте.</summary>
    public void SetRows(IReadOnlyList<JobRowModel> rows)
    {
        _rows = rows;
        var byKey = _views.ToDictionary(v => v.Model.Key);
        var sameOrder = _views.Count == rows.Count && _views.Select(v => v.Model.Key).SequenceEqual(rows.Select(r => r.Key));
        if (sameOrder)
        {
            for (var i = 0; i < rows.Count; i++) _views[i].Update(rows[i]);
        }
        else
        {
            _views.Clear();
            _list.Children.Clear();
            foreach (var row in rows)
            {
                if (byKey.TryGetValue(row.Key, out var view)) view.Update(row);
                else
                {
                    view = new JobRowView(this, _palette, row);
                    if (Motion.Enabled && IsVisible) FadeIn(view);
                }
                _views.Add(view);
                _list.Children.Add(view);
            }
        }

        _empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _clear.Visibility = rows.Any(r => r.IsFinished) ? Visibility.Visible : Visibility.Collapsed;
        _summary.Text = Summary(rows);
    }

    internal void RaiseOpen(object key) => OpenRequested?.Invoke(key);
    internal void RaiseReveal(object key) => RevealRequested?.Invoke(key);
    internal void RaiseCancel(object key) => CancelRequested?.Invoke(key);
    internal void RaiseDetails(object key) => DetailsRequested?.Invoke(key);

    private static string Summary(IReadOnlyList<JobRowModel> rows)
    {
        if (rows.Count == 0) return "Задач нет";
        var active = rows.Count(r => r.IsActive);
        var done = rows.Count(r => r.State == JobState.Done);
        var failed = rows.Count(r => r.State == JobState.Failed);
        var parts = new List<string>();
        if (active > 0) parts.Add($"в работе: {active}");
        if (done > 0) parts.Add($"готово: {done}");
        if (failed > 0) parts.Add(Russian.Count(failed, "ошибка", "ошибки", "ошибок"));
        if (parts.Count == 0) return "Всё отменено";
        var text = string.Join(" · ", parts);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static void FadeIn(UIElement view)
    {
        view.Opacity = 0;
        view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    private void Build()
    {
        var p = _palette;
        Background = p.Background;
        _views.Clear();

        // Заголовок: значок, «DropWheel», сводка; справа — «Очистить».
        var header = new Grid { Margin = new Thickness(16, 12, 10, 10) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var logo = new LogoMark { Size = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        header.Children.Add(logo);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock
        {
            Text = "DropWheel",
            FontFamily = UiFonts.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = p.Text,
        });
        _summary = new TextBlock { FontSize = 12, Foreground = p.TextSecondary, Margin = new Thickness(0, 1, 0, 0) };
        titles.Children.Add(_summary);
        Grid.SetColumn(titles, 1);
        header.Children.Add(titles);
        _clear = new FlyoutButton(null, "Очистить", "Убрать завершённые из списка", p.Text, p.ButtonHover, p.ButtonPressed,
            p.Focus, p.MakeToolTip());
        _clear.Click += () => ClearRequested?.Invoke();
        Grid.SetColumn(_clear, 2);
        header.Children.Add(_clear);

        var divider = new Border { Height = 1, Background = p.Divider };

        // Список: прокрутка колёсиком/тачпадом, тонкий индикатор справа вместо полосы прокрутки WPF.
        _list = new StackPanel { Margin = new Thickness(6, 6, 6, 8) };
        _scroll = new ScrollViewer
        {
            Content = _list,
            MaxHeight = MaxListHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
        };
        _indicator = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = p.TextTertiary,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 3, 0),
            Opacity = 0.55,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _scroll.ScrollChanged += (_, _) => UpdateIndicator();
        var listHost = new Grid();
        listHost.Children.Add(_scroll);
        listHost.Children.Add(_indicator);

        _empty = BuildEmpty(p);

        var body = new Grid();
        body.Children.Add(listHost);
        body.Children.Add(_empty);

        var root = new DockPanel { LastChildFill = true, RenderTransform = _slide };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(divider, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(divider);
        root.Children.Add(body);
        Child = root;
    }

    /// <summary>Пусто: значок и дружелюбная подсказка, как начать.</summary>
    private FrameworkElement BuildEmpty(FlyoutPalette p)
    {
        var panel = new StackPanel { Margin = new Thickness(32, 26, 32, 30), HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new LogoMark { Size = 44, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) });
        panel.Children.Add(new TextBlock
        {
            Text = "Пока пусто",
            FontFamily = UiFonts.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Foreground = p.Text,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Потащите файл и, не отпуская кнопку мыши, нажмите {_hotkeyName} — появится колесо форматов. " +
                   "Или щёлкните по файлу правой кнопкой → «Конвертировать…».",
            FontSize = 12.5,
            LineHeight = 18,
            Foreground = p.TextSecondary,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });
        return panel;
    }

    private void UpdateIndicator()
    {
        var extent = _scroll.ExtentHeight;
        var viewport = _scroll.ViewportHeight;
        if (extent <= viewport + 1 || viewport <= 0)
        {
            _indicator.Visibility = Visibility.Collapsed;
            return;
        }
        var height = Math.Max(24, viewport * viewport / extent);
        var top = _scroll.VerticalOffset / (extent - viewport) * (viewport - height);
        _indicator.Height = height;
        _indicator.Margin = new Thickness(0, top, 3, 0);
        _indicator.Visibility = Visibility.Visible;
    }
}

/// <summary>Строка задачи: миниатюра с бейджем состояния, имя, операция/статус, прогресс, кнопки.</summary>
internal sealed class JobRowView : Border
{
    private readonly JobsFlyoutView _owner;
    private readonly FlyoutPalette _p;
    private readonly Border _thumb = new();
    private readonly TextBlock _thumbGlyph = new();
    private readonly Image _thumbIcon = new();
    private readonly Border _badge = new();
    private readonly TextBlock _badgeGlyph = new();
    private readonly TextBlock _name = new();
    private readonly TextBlock _detail = new();
    private readonly SlimProgressBar _bar = new();
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private JobState? _actionsFor;
    private ImageSource? _shownThumb;

    public JobRowModel Model { get; private set; }

    public JobRowView(JobsFlyoutView owner, FlyoutPalette palette, JobRowModel model)
    {
        _owner = owner;
        _p = palette;
        Model = model;
        CornerRadius = new CornerRadius(6);
        Padding = new Thickness(8, 7, 6, 7);
        Margin = new Thickness(0, 1, 0, 1);
        Background = Brushes.Transparent;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Миниатюра 40×40 со скруглением, внизу справа — бейдж состояния.
        var thumbHost = new Grid { Width = 44, Height = 44, VerticalAlignment = VerticalAlignment.Center };
        _thumb.Width = _thumb.Height = 40;
        _thumb.CornerRadius = new CornerRadius(6);
        _thumb.HorizontalAlignment = HorizontalAlignment.Left;
        _thumb.VerticalAlignment = VerticalAlignment.Top;
        _thumbGlyph.FontFamily = UiFonts.Icons;
        _thumbGlyph.FontSize = 18;
        _thumbGlyph.Foreground = palette.TextSecondary;
        _thumbGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _thumbGlyph.VerticalAlignment = VerticalAlignment.Center;
        _thumbIcon.Width = _thumbIcon.Height = 28;
        _thumbIcon.Stretch = Stretch.Uniform;
        RenderOptions.SetBitmapScalingMode(_thumbIcon, BitmapScalingMode.HighQuality);
        var thumbContent = new Grid();
        thumbContent.Children.Add(_thumbGlyph);
        thumbContent.Children.Add(_thumbIcon);
        _thumb.Child = thumbContent;
        thumbHost.Children.Add(_thumb);

        _badge.Width = _badge.Height = 17;
        _badge.CornerRadius = new CornerRadius(8.5);
        _badge.BorderThickness = new Thickness(1.5);
        _badge.BorderBrush = new SolidColorBrush(palette.Solid);
        _badge.HorizontalAlignment = HorizontalAlignment.Right;
        _badge.VerticalAlignment = VerticalAlignment.Bottom;
        _badgeGlyph.FontFamily = UiFonts.Icons;
        _badgeGlyph.FontSize = 8;
        _badgeGlyph.Foreground = palette.OnBadge;
        _badgeGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        _badgeGlyph.VerticalAlignment = VerticalAlignment.Center;
        _badge.Child = _badgeGlyph;
        thumbHost.Children.Add(_badge);
        grid.Children.Add(thumbHost);

        var text = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        _name.FontWeight = FontWeights.SemiBold;
        _name.FontSize = 13;
        _name.Foreground = palette.Text;
        _name.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(_name);
        _detail.FontSize = 12;
        _detail.LineHeight = 16;
        _detail.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        _detail.MaxHeight = 32;
        _detail.TextWrapping = TextWrapping.Wrap;
        _detail.TextTrimming = TextTrimming.CharacterEllipsis;
        _detail.Margin = new Thickness(0, 1, 0, 0);
        text.Children.Add(_detail);
        _bar.Margin = new Thickness(0, 7, 0, 2);
        _bar.Track = palette.Track;
        _bar.Fill = palette.Accent;
        text.Children.Add(_bar);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        Grid.SetColumn(_actions, 2);
        grid.Children.Add(_actions);
        Child = grid;

        Update(model);
    }

    public void Update(JobRowModel m)
    {
        Model = m;
        _name.Text = m.Name;

        // Миниатюра: превью — на весь квадрат с обрезкой; значок типа — целиком по центру; иначе — значок шрифта.
        if (!ReferenceEquals(_shownThumb, m.Thumbnail) || _shownThumb is null)
        {
            _shownThumb = m.Thumbnail;
            if (m.Thumbnail is not null && !m.ThumbnailIsIcon)
            {
                var brush = new ImageBrush(m.Thumbnail) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                _thumb.Background = brush;
                _thumbIcon.Source = null;
                _thumbGlyph.Visibility = Visibility.Collapsed;
            }
            else
            {
                _thumb.Background = _p.ThumbBack;
                _thumbIcon.Source = m.Thumbnail;
                _thumbGlyph.Visibility = m.Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        _thumbGlyph.Text = m.Glyph;

        (Brush? badge, string glyph) = m.State switch
        {
            JobState.Done => (_p.Success, Glyphs.Check),
            JobState.Failed => (_p.Error, Glyphs.Cancel),
            JobState.Canceled => (_p.Neutral, Glyphs.Cancel),
            _ => ((Brush?)null, ""),
        };
        _badge.Visibility = badge is null ? Visibility.Collapsed : Visibility.Visible;
        _badge.Background = badge;
        _badgeGlyph.Text = glyph;

        _detail.Foreground = m.State == JobState.Failed ? _p.Error : _p.TextSecondary;
        _detail.Text = m.State switch
        {
            JobState.Queued => $"{m.Operation} · в очереди",
            JobState.Running => m.Progress > 0 ? $"{m.Operation} · {Math.Round(m.Progress * 100)} %" : $"{m.Operation} · выполняется…",
            JobState.Done => m.ResultName is { } result ? $"Готово · {result}" : "Готово",
            JobState.Failed => m.Error ?? "Не получилось",
            _ => $"{m.Operation} · отменено",
        };
        if (m.State == JobState.Failed && m.Error is { Length: > 0 }) _detail.ToolTip = m.Error;

        _bar.Visibility = m.IsActive ? Visibility.Visible : Visibility.Collapsed;
        _bar.Value = m.State == JobState.Running ? m.Progress : 0;
        _bar.IsIndeterminate = m.Indeterminate;
        _bar.Opacity = m.State == JobState.Queued ? 0.6 : 1;

        if (_actionsFor != m.State) BuildActions(m.State);
    }

    /// <summary>Кнопки по состоянию: готово — «Открыть» и «Показать в папке»; в работе — «Отмена»; ошибка — «Подробнее».</summary>
    private void BuildActions(JobState state)
    {
        _actionsFor = state;
        _actions.Children.Clear();
        FlyoutButton Icon(string glyph, string tip, Action<object> act)
        {
            var b = new FlyoutButton(glyph, null, tip, _p.Text, _p.ButtonHover, _p.ButtonPressed, _p.Focus, _p.MakeToolTip());
            b.Click += () => act(Model.Key);
            return b;
        }
        switch (state)
        {
            case JobState.Done:
                _actions.Children.Add(Icon(Glyphs.Open, "Открыть", _owner.RaiseOpen));
                _actions.Children.Add(Icon(Glyphs.FolderOpen, "Показать в папке", _owner.RaiseReveal));
                break;
            case JobState.Queued or JobState.Running:
                _actions.Children.Add(Icon(Glyphs.Cancel, "Отмена", _owner.RaiseCancel));
                break;
            case JobState.Failed:
                var details = new FlyoutButton(null, "Подробнее", "Открыть журнал с подробностями ошибки", _p.Link,
                    _p.ButtonHover, _p.ButtonPressed, _p.Focus, _p.MakeToolTip());
                details.Click += () => _owner.RaiseDetails(Model.Key);
                _actions.Children.Add(details);
                break;
        }
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Background = _p.RowHover;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Background = Brushes.Transparent;
    }

    /// <summary>Двойной щелчок по готовой строке — открыть результат.</summary>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.ClickCount == 2 && Model.State == JobState.Done)
        {
            e.Handled = true;
            _owner.RaiseOpen(Model.Key);
        }
    }
}
