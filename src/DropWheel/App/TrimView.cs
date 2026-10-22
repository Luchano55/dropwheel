using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.App;

/// <summary>
/// Содержимое окна «Обрезать видео» (D-TRIM): имя файла, кадр в месте ручки, шкала с миниатюрами и ручками
/// «начало» / «конец», время (мм:сс.д), галочка «Точно по кадру», кнопки «Обрезать» и «Отмена».
/// Ничего не запускает само: кадры приносит окно (<see cref="TrimWindow"/>) или скриншот-тест —
/// так вид можно снять без ffmpeg.
/// </summary>
public sealed class TrimView : Border
{
    public const double DefaultWidth = 600;
    public const int ThumbnailCount = 10;
    private const double PreviewHeight = 316;

    private SettingsPalette _palette;
    private readonly string _fileName;
    private TrimRange? _range;
    private bool _exact;
    private string? _message;
    private bool _failed;
    private ImageSource? _frame;
    private double _frameTime;
    private readonly ImageSource?[] _thumbnails = new ImageSource?[ThumbnailCount];
    private TrimHandle _active = TrimHandle.Start;

    private TrimTimeline _timeline = null!;
    private Image _image = null!;
    private TextBlock _overlay = null!;
    private Border _badge = null!;
    private TextBlock _badgeText = null!;
    private TextBlock _start = null!, _length = null!, _end = null!, _duration = null!;
    private PrimaryButton _trim = null!;

    public TrimView(SettingsPalette palette, string fileName)
    {
        _palette = palette;
        _fileName = fileName;
        _message = Loc.T.TrimLoading;
        Width = DefaultWidth;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(this, UiFonts.Text);
        System.Windows.Documents.TextElement.SetFontSize(this, 14);
        Build();
    }

    public SettingsPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Build();
        }
    }

    /// <summary>Выбранный кусок; null — длительность ещё неизвестна.</summary>
    public TrimRange? Range
    {
        get => _range;
        set
        {
            _range = value;
            _timeline.Range = value;
            UpdateTexts();
        }
    }

    /// <summary>Ручка, чей кадр показывается.</summary>
    public TrimHandle ActiveHandle
    {
        get => _active;
        set
        {
            _active = value;
            _timeline.ActiveHandle = value;
        }
    }

    public bool Exact
    {
        get => _exact;
        set
        {
            _exact = value;
            Build();
        }
    }

    /// <summary>Длительность прочитана: ручки — по краям ролика, можно выбирать.</summary>
    public void SetDuration(double seconds)
    {
        _failed = false;
        _message = null;
        Range = new TrimRange(seconds);
        UpdateOverlay();
    }

    /// <summary>Видео не прочиталось: показываем причину, «Обрезать» недоступна.</summary>
    public void SetFailed(string message)
    {
        _failed = true;
        _message = message;
        _range = null;
        _timeline.Range = null;
        UpdateTexts();
        UpdateOverlay();
    }

    /// <summary>Кадр для предпросмотра (момент seconds); null с сообщением — кадр не получился.</summary>
    public void SetFrame(ImageSource? frame, double seconds, string? message = null)
    {
        _frame = frame;
        _frameTime = seconds;
        if (!_failed) _message = message;
        _image.Source = frame;
        UpdateOverlay();
    }

    public void SetThumbnail(int index, ImageSource? image)
    {
        if (index < 0 || index >= ThumbnailCount) return;
        _thumbnails[index] = image;
        _timeline.SetThumbnail(index, image);
    }

    /// <summary>Пользователь сдвинул ручку: показать кадр в этом месте (окно подгружает его с задержкой).</summary>
    public event Action<double>? PreviewRequested;
    /// <summary>«Обрезать»: кусок и «точно по кадру».</summary>
    public event Action<TrimRange, bool>? TrimRequested;
    public event Action? CancelRequested;

    /// <summary>Можно ли нажать «Обрезать»: длительность известна и выбран не весь ролик.</summary>
    public bool CanTrim => _range is { IsWhole: false } && !_failed;

    /// <summary>Enter в окне.</summary>
    public void RequestTrim()
    {
        if (CanTrim) TrimRequested?.Invoke(_range!, _exact);
    }

    public void RequestCancel() => CancelRequested?.Invoke();

    // ---------- вид ----------

    private void Build()
    {
        var p = _palette;
        Background = p.Background;
        var root = new DockPanel { LastChildFill = true };

        // Низ: кнопки справа, над ними — тонкая линия (как подвал диалогов Windows 11).
        var footer = new Border
        {
            BorderBrush = p.Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 16, 24, 18),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _trim = new PrimaryButton(p, Loc.T.TrimButton) { MinWidth = 120 };
        _trim.Click += RequestTrim;
        var cancel = new SettingsButton(p, Loc.T.Cancel) { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += RequestCancel;
        buttons.Children.Add(_trim);
        buttons.Children.Add(cancel);
        footer.Child = buttons;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };

        // Заголовок: значок видео, имя файла, длительность.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = Glyphs.Video, FontFamily = UiFonts.Icons, FontSize = 16, Foreground = p.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0),
        });
        _duration = new TextBlock { FontSize = 13, Foreground = p.TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(_duration, Dock.Right);
        header.Children.Add(_duration);
        header.Children.Add(new TextBlock
        {
            Text = _fileName, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = p.Text,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = _fileName,
        });
        body.Children.Add(header);

        // Кадр: чёрная «рамка кино», картинка по центру с сохранением пропорций.
        var preview = new Grid { Height = PreviewHeight };
        preview.Children.Add(new Border { Background = SettingsPalette.B(Color.FromRgb(0x0C, 0x0C, 0x0C)), CornerRadius = new CornerRadius(8) });
        _image = new Image { Stretch = Stretch.Uniform, Source = _frame };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        preview.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(8), ClipToBounds = true, Child = _image,
            Clip = new RectangleGeometry(new Rect(0, 0, DefaultWidth - 48, PreviewHeight), 8, 8),
        });
        _overlay = new TextBlock
        {
            Foreground = SettingsPalette.B(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24),
        };
        preview.Children.Add(_overlay);
        _badgeText = new TextBlock { Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold };
        _badge = new Border
        {
            Background = SettingsPalette.B(Color.FromArgb(0xB0, 0, 0, 0)), CornerRadius = new CornerRadius(4),
            Padding = new Thickness(7, 2, 7, 3), Margin = new Thickness(10),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Child = _badgeText,
        };
        preview.Children.Add(_badge);
        body.Children.Add(preview);

        // Шкала.
        _timeline = new TrimTimeline(p) { Margin = new Thickness(-12, 12, -12, 0), Range = _range, ActiveHandle = _active };
        _timeline.Thumbnails = _thumbnails;
        _timeline.RangeChanged += (range, handle) =>
        {
            _range = range;
            _active = handle;
            UpdateTexts();
            PreviewRequested?.Invoke(handle == TrimHandle.Start ? range.Start : range.End);
        };
        body.Children.Add(_timeline);

        // Время: начало — слева, длина — посередине, конец — справа.
        var times = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        for (var i = 0; i < 3; i++) times.ColumnDefinitions.Add(new ColumnDefinition());
        _start = AddTime(times, 0, Loc.T.TrimStart, HorizontalAlignment.Left);
        _length = AddTime(times, 1, Loc.T.TrimLength, HorizontalAlignment.Center);
        _end = AddTime(times, 2, Loc.T.TrimEnd, HorizontalAlignment.Right);
        body.Children.Add(times);
        body.Children.Add(new TextBlock
        {
            Text = Loc.T.TrimDragHint, FontSize = 12, Foreground = p.TextSecondary, Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        // «Точно по кадру» — в карточке, как настройки.
        var check = new CheckMark(p, _exact, Loc.T.TrimExact) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 12, 0) };
        check.Changed += on => _exact = on;
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = Loc.T.TrimExact, FontSize = 14, Foreground = p.Text, TextWrapping = TextWrapping.Wrap });
        texts.Children.Add(new TextBlock
        {
            Text = Loc.T.TrimExactHint, FontSize = 12, Foreground = p.TextSecondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0),
        });
        var row = new DockPanel { Background = Brushes.Transparent, Cursor = Cursors.Arrow };
        DockPanel.SetDock(check, Dock.Left);
        row.Children.Add(check);
        row.Children.Add(texts);
        // Щелчок по подписи — тоже переключает.
        texts.Background = Brushes.Transparent;
        texts.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Exact = !_exact; // перестраивает вид с новой отметкой
        };
        body.Children.Add(new Border
        {
            Background = p.Card, BorderBrush = p.CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 13), Margin = new Thickness(0, 16, 0, 0),
            Child = row,
        });

        root.Children.Add(body);
        Child = root;
        UpdateTexts();
        UpdateOverlay();
    }

    private TextBlock AddTime(Grid grid, int column, string label, HorizontalAlignment align)
    {
        var panel = new StackPanel { HorizontalAlignment = align };
        var textAlign = align switch
        {
            HorizontalAlignment.Left => TextAlignment.Left,
            HorizontalAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Center,
        };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = _palette.TextSecondary, TextAlignment = textAlign });
        var value = new TextBlock { FontFamily = UiFonts.Display, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = _palette.Text, TextAlignment = textAlign };
        AutomationProperties.SetName(value, label);
        panel.Children.Add(value);
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
        return value;
    }

    private void UpdateTexts()
    {
        const string none = "--:--.-";
        _start.Text = _range is { } r ? VideoTrim.Format(r.Start) : none;
        _end.Text = _range is { } r2 ? VideoTrim.Format(r2.End) : none;
        _length.Text = _range is { } r3 ? VideoTrim.Format(r3.Length) : none;
        _duration.Text = _range is { } r4 ? VideoTrim.Format(r4.Duration) : "";
        _trim.SetEnabled(CanTrim);
    }

    private void UpdateOverlay()
    {
        _overlay.Text = _message ?? "";
        _overlay.Visibility = string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible;
        _image.Opacity = _message is null ? 1 : 0.35;
        _badgeText.Text = VideoTrim.Format(_frameTime);
        _badge.Visibility = _frame is not null && !_failed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Главная кнопка диалога — залита акцентом (как «ОК» в Windows 11). Её же берёт окно «Обрезать картинку».</summary>
    internal sealed class PrimaryButton : Pressable
    {
        private readonly TextBlock _text;
        private bool _enabled = true;

        public PrimaryButton(SettingsPalette palette, string text) : base(palette)
        {
            CornerRadius = new CornerRadius(4);
            Cursor = Cursors.Arrow;
            Padding = new Thickness(12, 5, 12, 6);
            MinHeight = 32;
            _text = new TextBlock
            {
                Text = text, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Child = _text;
            AutomationProperties.SetName(this, text);
            UpdateLook();
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            IsEnabled = enabled;
            UpdateLook();
        }

        protected override void UpdateLook()
        {
            var p = Palette;
            if (!_enabled)
            {
                Background = p.Control;
                BorderBrush = p.ControlBorder;
                _text.Foreground = p.TextTertiary;
                Opacity = 1;
                return;
            }
            Background = p.Accent;
            BorderBrush = HasKeyboardFocus ? p.Text : p.Accent;
            _text.Foreground = p.OnAccent;
            Opacity = IsPressed ? 0.8 : IsHovered ? 0.9 : 1;
        }
    }
}
