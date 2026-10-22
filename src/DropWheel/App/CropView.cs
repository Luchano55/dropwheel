using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.App;

/// <summary>
/// Содержимое окна «Обрезать картинку» (D-CROP): имя файла и размер, картинка с рамкой (<see cref="CropCanvas"/>),
/// пропорции пилюлями («Свободно», «1:1», «4:3», «16:9», «Как было»), размер рамки в пикселях,
/// кнопки «Обрезать» и «Отмена». Ничего не запускает само: картинку приносит окно (<see cref="CropWindow"/>)
/// или скриншот-тест — так вид можно снять без ImageMagick.
/// </summary>
public sealed class CropView : Border
{
    public const double DefaultWidth = 640;
    private const double CanvasHeight = 400;

    private SettingsPalette _palette;
    private readonly string _fileName;
    private ImageSource? _image;
    private int _imageW, _imageH;
    private CropRect? _rect;
    private CropAspect _aspect = CropAspect.Free;
    private string? _message;
    private bool _failed;

    private CropCanvas _canvas = null!;
    private TextBlock _overlay = null!;
    private TextBlock _size = null!, _original = null!;
    private TrimView.PrimaryButton _crop = null!;

    public CropView(SettingsPalette palette, string fileName)
    {
        _palette = palette;
        _fileName = fileName;
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

    /// <summary>Рамка в пикселях картинки; null — картинки ещё нет.</summary>
    public CropRect? Rect
    {
        get => _rect;
        set
        {
            _rect = value;
            _canvas.Selection = value;
            UpdateTexts();
        }
    }

    /// <summary>Пропорция рамки: при смене рамка становится самой большой с этой пропорцией по центру («Свободно» — рамка та же).</summary>
    public CropAspect Aspect
    {
        get => _aspect;
        set
        {
            _aspect = value;
            if (_imageW > 0 && value != CropAspect.Free)
                _rect = ImageCrop.Largest(ImageCrop.Aspect(value, _imageW, _imageH), _imageW, _imageH);
            Build();
        }
    }

    /// <summary>Картинка открылась: показ (может быть уменьшенной копией) и настоящий размер после поворота; рамка — вся картинка.</summary>
    public void SetImage(ImageSource image, int width, int height)
    {
        _failed = false;
        _message = null;
        _image = image;
        _imageW = width;
        _imageH = height;
        _rect = CropRect.Whole(width, height);
        Build();
    }

    /// <summary>Картинка не открылась: причина, «Обрезать» недоступна.</summary>
    public void SetFailed(string message)
    {
        _failed = true;
        _message = message;
        _image = null;
        _imageW = _imageH = 0;
        _rect = null;
        Build();
    }

    /// <summary>«Обрезать»: рамка в пикселях картинки и размер картинки.</summary>
    public event Action<CropRect, int, int>? CropRequested;
    public event Action? CancelRequested;

    /// <summary>Можно ли нажать «Обрезать»: картинка открыта и рамка — не вся картинка.</summary>
    public bool CanCrop => !_failed && _rect is { } r && _imageW > 0 && !r.IsWhole(_imageW, _imageH);

    /// <summary>Enter в окне.</summary>
    public void RequestCrop()
    {
        if (CanCrop) CropRequested?.Invoke(_rect!.Value, _imageW, _imageH);
    }

    public void RequestCancel() => CancelRequested?.Invoke();

    // ---------- вид ----------

    private void Build()
    {
        var p = _palette;
        Background = p.Background;
        var root = new DockPanel { LastChildFill = true };

        var footer = new Border
        {
            BorderBrush = p.Divider, BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(24, 16, 24, 18),
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _crop = new TrimView.PrimaryButton(p, Loc.T.CropButton) { MinWidth = 120 };
        _crop.Click += RequestCrop;
        var cancel = new SettingsButton(p, Loc.T.Cancel) { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += RequestCancel;
        buttons.Children.Add(_crop);
        buttons.Children.Add(cancel);
        footer.Child = buttons;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel { Margin = new Thickness(24, 18, 24, 18) };

        // Заголовок: значок фото, имя файла, размер картинки.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = Glyphs.Photo, FontFamily = UiFonts.Icons, FontSize = 16, Foreground = p.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0),
        });
        _original = new TextBlock { FontSize = 13, Foreground = p.TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        DockPanel.SetDock(_original, Dock.Right);
        header.Children.Add(_original);
        header.Children.Add(new TextBlock
        {
            Text = _fileName, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = p.Text,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = _fileName,
        });
        body.Children.Add(header);

        // Картинка с рамкой на тёмной подложке.
        var preview = new Grid { Height = CanvasHeight };
        preview.Children.Add(new Border { Background = SettingsPalette.B(Color.FromRgb(0x1C, 0x1C, 0x1C)), CornerRadius = new CornerRadius(8) });
        _canvas = new CropCanvas(p) { Aspect = ImageCrop.Aspect(_aspect, _imageW, _imageH) };
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.HighQuality);
        _canvas.SetImage(_image, _imageW, _imageH);
        _canvas.Selection = _rect;
        _canvas.SelectionChanged += r =>
        {
            _rect = r;
            UpdateTexts();
        };
        preview.Children.Add(_canvas);
        _overlay = new TextBlock
        {
            Foreground = SettingsPalette.B(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)), FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24),
            Text = _message ?? "", Visibility = string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible,
        };
        preview.Children.Add(_overlay);
        body.Children.Add(preview);

        // Пропорции — слева, размер рамки — справа.
        var row = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        _size = new TextBlock
        {
            FontFamily = UiFonts.Display, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = p.Text,
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12, 4, 0, 0),
        };
        AutomationProperties.SetName(_size, Loc.T.CropTitle);
        DockPanel.SetDock(_size, Dock.Right);
        row.Children.Add(_size);
        var pills = new ChoicePills(p,
        [
            (CropAspect.Free, Loc.T.CropFree),
            (CropAspect.Square, "1:1"),
            (CropAspect.FourThree, _imageH > _imageW ? "3:4" : "4:3"),
            (CropAspect.SixteenNine, _imageH > _imageW ? "9:16" : "16:9"),
            (CropAspect.Original, Loc.T.CropOriginal),
        ], _aspect, Loc.T.CropTitle) { Margin = new Thickness(0, 0, 0, -6) };
        pills.SelectionChanged += v => Aspect = (CropAspect)v;
        row.Children.Add(pills);
        body.Children.Add(row);
        body.Children.Add(new TextBlock
        {
            Text = Loc.T.CropHint, FontSize = 12, Foreground = p.TextSecondary, Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        root.Children.Add(body);
        Child = root;
        UpdateTexts();
    }

    private static string Size(int w, int h) => string.Create(CultureInfo.InvariantCulture, $"{w} × {h}");

    private void UpdateTexts()
    {
        _size.Text = _rect is { } r ? Size(r.W, r.H) : "— × —";
        _original.Text = _imageW > 0 ? Size(_imageW, _imageH) : "";
        _crop.SetEnabled(CanCrop);
    }
}
