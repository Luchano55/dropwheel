using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Capture;

/// <summary>Что сделать с выбранной областью снимка.</summary>
internal enum CaptureAction
{
    Copy,
    Save,
    Wheel,
    Text,
    /// <summary>Окно «Пометки» (D-ANNOTATE).</summary>
    Annotate,
}

/// <summary>
/// Содержимое окна оверлея одного монитора (решение D-CAPTURE): стоп-кадр монитора, затемнение вокруг выделения,
/// рамка, подпись размера в пикселях, подсказка и панель кнопок. Всё в DIP этого окна; что рисовать — решает
/// <see cref="CaptureOverlay"/> (по <see cref="CaptureSession"/>). Без окна — его же рисуют скриншот-тесты.
/// </summary>
internal sealed partial class CaptureView : Canvas
{
    /// <summary>Затемнение вне выделения.</summary>
    private static readonly Brush Dim = SettingsPalette.B(Color.FromArgb(0x80, 0, 0, 0));
    private static readonly Brush Halo = SettingsPalette.B(Color.FromArgb(0x66, 0, 0, 0));

    private readonly Image _image = new() { Stretch = Stretch.Fill };
    private readonly System.Windows.Shapes.Path _dim = new() { Fill = Dim, IsHitTestVisible = false };
    private readonly Rectangle _halo = new() { Stroke = Halo, IsHitTestVisible = false, SnapsToDevicePixels = true };
    private readonly Rectangle _frame = new() { Stroke = Brushes.White, IsHitTestVisible = false, SnapsToDevicePixels = true };
    private readonly Border _label = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2, 7, 3), IsHitTestVisible = false };
    private readonly TextBlock _labelText = new() { FontSize = 12 };
    private readonly Border _hint = new() { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 7, 16, 8), IsHitTestVisible = false };
    private readonly TextBlock _hintText = new() { FontSize = 13 };
    private readonly Border _toolbar = new()
    {
        CornerRadius = new CornerRadius(8),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(4),
        Cursor = Cursors.Arrow,
        Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.35 },
    };
    private readonly bool _ocr;
    private SettingsPalette _palette;

    /// <param name="record">Режим записи экрана (D-RECORD) и выбранный в нём формат; null — снимок.</param>
    public CaptureView(SettingsPalette palette, bool ocr, RecordFormat? record = null)
    {
        _palette = palette;
        _ocr = ocr;
        _record = record;
        ClipToBounds = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        _labelText.FontFamily = _hintText.FontFamily = UiFonts.Text;
        _label.Child = _labelText;
        _hint.Child = _hintText;
        Children.Add(_image);
        Children.Add(_dim);
        Children.Add(_halo);
        Children.Add(_frame);
        Children.Add(_label);
        Children.Add(_hint);
        Children.Add(_toolbar);
        BuildToolbar();
        Show(null, null, toolbar: false, hint: false);
    }

    /// <summary>Нажата кнопка панели.</summary>
    public event Action<CaptureAction>? ActionRequested;

    /// <summary>Нажат крестик панели.</summary>
    public event Action? CloseRequested;

    /// <summary>Стоп-кадр монитора (растягивается на всё окно: физические пиксели 1:1).</summary>
    public ImageSource? Frame
    {
        get => _image.Source;
        set => _image.Source = value;
    }

    public SettingsPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            BuildToolbar();
            Render();
        }
    }

    private DipRect? _highlight;
    private string? _size;
    private bool _showToolbar, _showHint;

    /// <summary>
    /// Нарисовать: подсвеченная область (DIP окна; может выходить за окно), подпись размера, панель кнопок, подсказка.
    /// highlight = null — затемнено всё.
    /// </summary>
    public void Show(DipRect? highlight, string? sizeText, bool toolbar, bool hint)
    {
        _highlight = highlight;
        _size = sizeText;
        _showToolbar = toolbar && highlight is not null;
        _showHint = hint;
        Render();
    }

    private void Render()
    {
        var w = double.IsNaN(Width) ? ActualWidth : Width;
        var h = double.IsNaN(Height) ? ActualHeight : Height;
        _image.Width = w;
        _image.Height = h;
        var area = new DipRect(0, 0, w, h);
        // Рамка — ровно в один физический пиксель снаружи выделения (при 150 % — 1/1.5 DIP).
        var px = 1 / Math.Max(0.5, VisualTreeHelper.GetDpi(this).DpiScaleX);

        var full = new RectangleGeometry(new Rect(0, 0, w, h));
        if (_highlight is { } s)
        {
            var r = new Rect(s.X, s.Y, Math.Max(0, s.Width), Math.Max(0, s.Height));
            _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(r));
            Place(_frame, r.X - px, r.Y - px, r.Width + 2 * px, r.Height + 2 * px);
            _frame.StrokeThickness = px;
            Place(_halo, r.X - 2 * px, r.Y - 2 * px, r.Width + 4 * px, r.Height + 4 * px);
            _halo.StrokeThickness = px;
            _frame.Visibility = _halo.Visibility = Visibility.Visible;
        }
        else
        {
            _dim.Data = full;
            _frame.Visibility = _halo.Visibility = Visibility.Collapsed;
        }

        _label.Background = SettingsPalette.B(_palette.Solid);
        _label.BorderBrush = _palette.ControlBorder;
        _labelText.Foreground = _palette.Text;
        if (_highlight is { } sel && _size is { Length: > 0 })
        {
            _labelText.Text = _size;
            _label.Visibility = Visibility.Visible;
            _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var (lx, ly) = CaptureLayout.SizeLabel(sel, _label.DesiredSize.Width, _label.DesiredSize.Height, area);
            SetLeft(_label, Math.Round(lx));
            SetTop(_label, Math.Round(ly));
        }
        else _label.Visibility = Visibility.Collapsed;

        _hint.Background = SettingsPalette.B(_palette.Solid);
        _hintText.Foreground = _palette.Text;
        _hintText.Text = _record is null ? Loc.T.CaptureHint : Loc.T.RecordHint;
        _hint.Visibility = _showHint ? Visibility.Visible : Visibility.Collapsed;
        if (_showHint)
        {
            _hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(_hint, Math.Round((w - _hint.DesiredSize.Width) / 2));
            SetTop(_hint, 24);
        }

        _toolbar.Visibility = _showToolbar ? Visibility.Visible : Visibility.Collapsed;
        if (_showToolbar && _highlight is { } t)
        {
            _toolbar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var (tx, ty) = CaptureLayout.Toolbar(t, _toolbar.DesiredSize.Width, _toolbar.DesiredSize.Height, area);
            SetLeft(_toolbar, Math.Round(tx));
            SetTop(_toolbar, Math.Round(ty));
        }
    }

    private static void Place(FrameworkElement e, double x, double y, double w, double h)
    {
        SetLeft(e, x);
        SetTop(e, y);
        e.Width = Math.Max(0, w);
        e.Height = Math.Max(0, h);
    }

    private void BuildToolbar()
    {
        var p = _palette;
        _toolbar.Background = SettingsPalette.B(p.Solid);
        _toolbar.BorderBrush = p.ControlBorder;
        if (_record is not null)
        {
            _toolbar.Child = RecordToolbar(p);
            return;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(CaptureAction action, string text, string glyph)
        {
            var button = new SettingsButton(p, text, glyph, subtle: true) { Margin = new Thickness(0, 0, 2, 0) };
            button.Click += () => ActionRequested?.Invoke(action);
            row.Children.Add(button);
        }
        Add(CaptureAction.Copy, Loc.T.CaptureCopy, Glyphs.Copy);
        Add(CaptureAction.Save, Loc.T.CaptureSave, Glyphs.Save);
        Add(CaptureAction.Wheel, Loc.T.CaptureWheel, Glyphs.Convert);
        Add(CaptureAction.Annotate, Loc.T.CaptureAnnotate, Glyphs.Annotate);
        if (_ocr) Add(CaptureAction.Text, Loc.T.CaptureText, Glyphs.Text);
        row.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 6, 6, 6), Background = p.Divider });
        var close = new SettingsButton(p, null, Glyphs.Cancel, Loc.T.CaptureClose, subtle: true);
        close.Click += () => CloseRequested?.Invoke();
        row.Children.Add(close);
        _toolbar.Child = row;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Render();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Render();
    }
}
