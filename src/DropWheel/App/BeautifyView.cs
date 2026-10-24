using System.Globalization;
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
/// Содержимое окна «Оформить картинку» (D-BEAUTIFY): имя файла и размер результата, предпросмотр (на шахматке —
/// видна прозрачность), фон кружками-образцами, ползунки «Отступ» и «Скругление», переключатель «Тень»,
/// кнопки «Копировать», «Сохранить», «Отмена». Ничего не запускает само: предпросмотр рисует окно
/// (<see cref="BeautifyWindow"/>) или скриншот-тест — так вид можно снять без ImageMagick.
/// </summary>
public sealed class BeautifyView : Border
{
    public const double DefaultWidth = 640;
    private const double PreviewHeight = 340;

    private SettingsPalette _palette;
    private readonly string _fileName;
    private BeautifyStyle _style;
    private ImageSource? _preview;
    private int _imageW, _imageH;
    private string? _message;
    private string? _status;
    private bool _failed;

    private Image _image = null!;
    private TextBlock _overlay = null!, _size = null!, _statusText = null!;
    private TextBlock _padValue = null!, _radiusValue = null!;
    private TrimView.PrimaryButton _save = null!;
    private SettingsButton _copy = null!;

    public BeautifyView(SettingsPalette palette, string fileName, BeautifyStyle? style = null)
    {
        _palette = palette;
        _fileName = fileName;
        _style = style ?? BeautifyStyle.Default;
        _message = Loc.T.CropLoading;
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

    /// <summary>Выбранное оформление. Установка из кода перестраивает элементы, но не вызывает <see cref="StyleChanged"/>.</summary>
    public BeautifyStyle CurrentStyle
    {
        get => _style;
        set
        {
            _style = value;
            Build();
        }
    }

    /// <summary>Картинка открылась: размер полной картинки после поворота (для подписи размера результата).</summary>
    public void SetImage(int width, int height)
    {
        _failed = false;
        _imageW = width;
        _imageH = height;
        _message = _preview is null ? Loc.T.BeautifyRendering : null;
        Build();
    }

    /// <summary>Нарисованный предпросмотр (или null и причина — не получился).</summary>
    public void SetPreview(ImageSource? preview, string? message = null)
    {
        _preview = preview;
        _message = preview is null ? message ?? Loc.T.BeautifyRenderFailed : null;
        _image.Source = preview;
        ShowOverlay();
    }

    /// <summary>Картинка не открылась: причина, кнопки недоступны.</summary>
    public void SetFailed(string message)
    {
        _failed = true;
        _message = message;
        _preview = null;
        _imageW = _imageH = 0;
        Build();
    }

    /// <summary>Строка рядом с «Копировать»: «Скопировано в буфер обмена», «Рисую…»; null — пусто.</summary>
    public void SetStatus(string? status)
    {
        _status = status;
        _statusText.Text = status ?? "";
    }

    /// <summary>Оформление изменил пользователь (фон, ползунок, тень) — окну пора перерисовать предпросмотр.</summary>
    public event Action<BeautifyStyle>? StyleChanged;
    public event Action<BeautifyStyle>? SaveRequested;
    public event Action<BeautifyStyle>? CopyRequested;
    public event Action? CancelRequested;

    public bool CanSave => !_failed && _imageW > 0;

    /// <summary>Enter в окне.</summary>
    public void RequestSave()
    {
        if (CanSave) SaveRequested?.Invoke(_style);
    }

    public void RequestCopy()
    {
        if (CanSave) CopyRequested?.Invoke(_style);
    }

    public void RequestCancel() => CancelRequested?.Invoke();

    private void Change(BeautifyStyle style)
    {
        if (style == _style) return;
        _style = style;
        UpdateTexts();
        StyleChanged?.Invoke(style);
    }

    // ---------- вид ----------

    private void Build()
    {
        var p = _palette;
        Background = p.Background;
        var root = new DockPanel { LastChildFill = true };

        // Низ: «Копировать» и состояние слева, «Сохранить» / «Отмена» справа.
        var footer = new Border
        {
            BorderBrush = p.Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 16, 24, 18),
        };
        var bar = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _save = new TrimView.PrimaryButton(p, Loc.T.BeautifySave) { MinWidth = 120 };
        _save.Click += RequestSave;
        var cancel = new SettingsButton(p, Loc.T.Cancel) { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += RequestCancel;
        buttons.Children.Add(_save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        bar.Children.Add(buttons);
        _copy = new SettingsButton(p, Loc.T.BeautifyCopy, Glyphs.Copy) { VerticalAlignment = VerticalAlignment.Center };
        _copy.Click += RequestCopy;
        DockPanel.SetDock(_copy, Dock.Left);
        bar.Children.Add(_copy);
        _statusText = new TextBlock
        {
            Text = _status ?? "", FontSize = 12.5, Foreground = p.TextSecondary, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis,
        };
        bar.Children.Add(_statusText);
        footer.Child = bar;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };

        // Заголовок: значок фото, имя файла, размер результата.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = Glyphs.Photo, FontFamily = UiFonts.Icons, FontSize = 16, Foreground = p.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0),
        });
        _size = new TextBlock { FontSize = 13, Foreground = p.TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(_size, Dock.Right);
        header.Children.Add(_size);
        header.Children.Add(new TextBlock
        {
            Text = _fileName, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = p.Text,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = _fileName,
        });
        body.Children.Add(header);

        // Предпросмотр на шахматке: прозрачный фон и тень видны как в редакторах картинок.
        var preview = new Grid { Height = PreviewHeight };
        preview.Children.Add(new Border { Background = Checker(p.IsLight), CornerRadius = new CornerRadius(8) });
        _image = new Image { Source = _preview, Stretch = Stretch.Uniform, Margin = new Thickness(14) };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        preview.Children.Add(_image);
        _overlay = new TextBlock
        {
            FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(14, 8, 14, 9),
            Foreground = SettingsPalette.B(Colors.White),
            Background = SettingsPalette.B(Color.FromArgb(0xA0, 0x20, 0x20, 0x20)),
        };
        preview.Children.Add(_overlay);
        body.Children.Add(preview);

        // Настройки: фон, отступ, скругление, тень.
        var grid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddLabel(grid, 0, Loc.T.BeautifyBackground, top: true);
        var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(swatches, Loc.T.BeautifyBackground);
        var pills = new List<Swatch>();
        foreach (var bg in ImageBeautify.Backgrounds)
        {
            var swatch = new Swatch(p, bg, ImageBeautify.Label(bg)) { IsSelected = bg.Key == _style.Background };
            swatch.Click += () =>
            {
                foreach (var other in pills) other.IsSelected = ReferenceEquals(other, swatch);
                Change(_style with { Background = bg.Key });
            };
            pills.Add(swatch);
            swatches.Children.Add(swatch);
        }
        Grid.SetRow(swatches, 0);
        Grid.SetColumn(swatches, 1);
        Grid.SetColumnSpan(swatches, 2);
        grid.Children.Add(swatches);

        AddLabel(grid, 1, Loc.T.BeautifyPadding);
        var pad = new ValueSlider(p, ImageBeautify.MinPadding, ImageBeautify.MaxPadding, _style.Padding, Loc.T.BeautifyPadding)
            { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        pad.ValueChanged += v => Change(_style with { Padding = v });
        Place(grid, pad, 1, 1);
        _padValue = ValueText(p);
        Place(grid, _padValue, 1, 2);

        AddLabel(grid, 2, Loc.T.BeautifyRadius);
        var radius = new ValueSlider(p, ImageBeautify.MinRadius, ImageBeautify.MaxRadius, _style.Radius, Loc.T.BeautifyRadius)
            { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        radius.ValueChanged += v => Change(_style with { Radius = v });
        Place(grid, radius, 2, 1);
        _radiusValue = ValueText(p);
        Place(grid, _radiusValue, 2, 2);

        AddLabel(grid, 3, Loc.T.BeautifyShadow);
        var shadow = new ToggleSwitch(p, _style.Shadow, Loc.T.BeautifyShadow)
            { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        shadow.Toggled += on => Change(_style with { Shadow = on });
        Place(grid, shadow, 3, 1);
        body.Children.Add(grid);

        var suffix = OperationCatalog.Find(ImageBeautify.Id) is { } op ? Loc.Suffix(op) : "";
        body.Children.Add(new TextBlock
        {
            Text = Loc.F(Loc.T.BeautifyHint, Path.GetFileNameWithoutExtension(_fileName) + suffix + ".png"),
            FontSize = 12, Foreground = p.TextSecondary, Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap,
        });

        root.Children.Add(body);
        Child = root;
        UpdateTexts();
        ShowOverlay();
    }

    private void AddLabel(Grid grid, int row, string text, bool top = false)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = 14, Foreground = _palette.Text,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center, Margin = new Thickness(0, top ? 6 : 0, 12, 0),
        };
        Place(grid, label, row, 0);
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static TextBlock ValueText(SettingsPalette p) => new()
    {
        FontFamily = UiFonts.Display, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = p.Text,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right,
    };

    private void UpdateTexts()
    {
        _padValue.Text = string.Create(CultureInfo.InvariantCulture, $"{_style.Padding} %");
        _radiusValue.Text = string.Create(CultureInfo.InvariantCulture, $"{_style.Radius} px");
        if (_imageW > 0)
        {
            var l = ImageBeautify.Measure(_style, _imageW, _imageH);
            _size.Text = string.Create(CultureInfo.InvariantCulture, $"{_imageW} × {_imageH} → {l.Width} × {l.Height}");
        }
        else _size.Text = "";
        _save.SetEnabled(CanSave);
        _copy.SetEnabled(CanSave);
    }

    private void ShowOverlay()
    {
        _overlay.Text = _message ?? "";
        _overlay.Visibility = string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Шахматка 8 px: светлая или тёмная под тему.</summary>
    internal static Brush Checker(bool light, double cell = 8)
    {
        var a = light ? Color.FromRgb(0xF4, 0xF4, 0xF4) : Color.FromRgb(0x2B, 0x2B, 0x2B);
        var b = light ? Color.FromRgb(0xE2, 0xE2, 0xE2) : Color.FromRgb(0x36, 0x36, 0x36);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(SettingsPalette.B(a), null, new RectangleGeometry(new Rect(0, 0, 2 * cell, 2 * cell))));
        var dark = new GeometryGroup();
        dark.Children.Add(new RectangleGeometry(new Rect(0, 0, cell, cell)));
        dark.Children.Add(new RectangleGeometry(new Rect(cell, cell, cell, cell)));
        group.Children.Add(new GeometryDrawing(SettingsPalette.B(b), null, dark));
        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 2 * cell, 2 * cell), ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Заливка кружка-образца: градиент по диагонали (как у ImageMagick, 135°), цвет или шахматка.</summary>
    internal static Brush SwatchBrush(BeautifyBackground bg)
    {
        if (bg.IsTransparent) return Checker(light: true, cell: 4);
        var from = (Color)ColorConverter.ConvertFromString(bg.From);
        var to = (Color)ColorConverter.ConvertFromString(bg.To);
        Brush brush = bg.IsGradient ? new LinearGradientBrush(from, to, new Point(0, 0), new Point(1, 1)) : new SolidColorBrush(from);
        brush.Freeze();
        return brush;
    }

    /// <summary>Пилюля фона: кружок-образец и подпись; выбранная — акцентом, как пилюли окна настроек.</summary>
    private sealed class Swatch : Pressable
    {
        private readonly TextBlock _text;
        private bool _selected;

        public Swatch(SettingsPalette palette, BeautifyBackground bg, string label) : base(palette)
        {
            CornerRadius = new CornerRadius(15);
            Padding = new Thickness(7, 5, 12, 6);
            Margin = new Thickness(0, 0, 6, 6);
            Cursor = Cursors.Arrow;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = SwatchBrush(bg),
                BorderBrush = palette.ControlBorder, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            _text = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(_text);
            Child = row;
            AutomationProperties.SetName(this, label);
            UpdateLook();
        }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                _selected = value;
                UpdateLook();
            }
        }

        protected override void UpdateLook()
        {
            var p = Palette;
            if (_selected)
            {
                Background = p.Accent;
                _text.Foreground = p.OnAccent;
                BorderBrush = HasKeyboardFocus ? p.Text : p.Accent;
                _text.FontWeight = FontWeights.SemiBold;
            }
            else
            {
                Background = IsPressed ? p.ControlPressed : IsHovered ? p.ControlHover : p.Control;
                _text.Foreground = p.Text;
                BorderBrush = HasKeyboardFocus ? p.Text : p.ControlBorder;
                _text.FontWeight = FontWeights.Normal;
            }
        }
    }
}
