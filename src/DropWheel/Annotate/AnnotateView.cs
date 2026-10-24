using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.App;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Annotate;

/// <summary>
/// Содержимое окна «Пометки» (D-ANNOTATE): сверху — инструменты, цвета, толщина, «Отменить» / «Повторить» /
/// «Удалить последнюю»; в середине — картинка, вписанная в окно, и пометки поверх; снизу — «Копировать»,
/// подсказки, «Сохранить» и «Отмена». Сохранять и копировать — дело окна (<see cref="AnnotateWindow"/>); без окна
/// вид снимают скриншот-тесты.
/// </summary>
internal sealed class AnnotateView : Border
{
    public const double DefaultWidth = 900, DefaultHeight = 640;

    // Последний выбор за этот запуск программы — следующее окно открывается с ним.
    private static AnnotationTool _lastTool = AnnotationTool.Arrow;
    private static string _lastColor = AnnotationColors.Default;
    private static StrokeSize _lastSize = StrokeSize.Medium;

    private static readonly AnnotationTool[] Tools =
    [
        AnnotationTool.Arrow, AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Pen,
        AnnotationTool.Marker, AnnotationTool.Text, AnnotationTool.Blur, AnnotationTool.Number,
    ];

    private SettingsPalette _palette;
    private readonly string _saveHint;
    private readonly AnnotateCanvas _canvas;
    private readonly Canvas _textLayer = new() { ClipToBounds = true };
    private readonly List<(AnnotationTool Tool, ChoiceButton Button)> _toolButtons = [];
    private readonly List<(string Color, ChoiceButton Button)> _colorButtons = [];
    private readonly List<(StrokeSize Size, ChoiceButton Button)> _sizeButtons = [];
    private SettingsButton _undo = null!, _redo = null!, _delete = null!, _copy = null!;
    private TrimView.PrimaryButton _save = null!;
    private TextBlock _status = null!, _overlay = null!;
    private Border _confirm = null!;
    private string? _statusText, _message;
    private bool _confirming;
    private TextBox? _editor;
    private ImagePoint _editorAt;

    public AnnotateView(SettingsPalette palette, string saveHint)
    {
        _palette = palette;
        _saveHint = saveHint;
        _message = Loc.T.CropLoading;
        Width = DefaultWidth;
        Height = DefaultHeight;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(this, UiFonts.Text);
        System.Windows.Documents.TextElement.SetFontSize(this, 14);
        Tool = _lastTool;
        Color = _lastColor;
        Size = _lastSize;
        _canvas = new AnnotateCanvas(this);
        Document.Changed += OnDocumentChanged;
        Build();
    }

    public AnnotationDocument Document { get; } = new();

    /// <summary>Рисует пометки; null — картинки ещё нет.</summary>
    public AnnotationRenderer? Renderer { get; private set; }

    public bool CanSave => Renderer is not null;

    public AnnotationTool Tool { get; private set; }
    public string Color { get; private set; }
    public StrokeSize Size { get; private set; }

    public SettingsPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Build();
        }
    }

    public event Action? SaveRequested;
    public event Action? CopyRequested;
    /// <summary>«Отмена», Esc: окно решает — закрыть сразу или сначала спросить (<see cref="ShowConfirm"/>).</summary>
    public event Action? CancelRequested;
    /// <summary>«Закрыть» в вопросе «Закрыть без сохранения?».</summary>
    public event Action? DiscardConfirmed;

    /// <summary>Картинка открылась (полный размер). Пометки рисуются в её пикселях.</summary>
    public void SetImage(BitmapSource image)
    {
        Renderer = new AnnotationRenderer(image);
        _message = null;
        Build();
    }

    /// <summary>Картинка не открылась: причина, «Сохранить» и «Копировать» недоступны.</summary>
    public void SetFailed(string message)
    {
        Renderer = null;
        _message = message;
        Build();
    }

    /// <summary>Строка внизу: «Скопировано в буфер обмена» и т. п.; null — подсказка по клавишам.</summary>
    public void SetStatus(string? status)
    {
        _statusText = status;
        UpdateStatus();
    }

    public void SelectTool(AnnotationTool tool)
    {
        CommitText();
        Tool = _lastTool = tool;
        _canvas.Cursor = tool == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross;
        foreach (var (t, b) in _toolButtons) b.IsSelected = t == tool;
    }

    public void SelectColor(string color)
    {
        Color = _lastColor = color;
        foreach (var (c, b) in _colorButtons) b.IsSelected = c == color;
        StyleEditor();
    }

    public void SelectSize(StrokeSize size)
    {
        Size = _lastSize = size;
        foreach (var (s, b) in _sizeButtons) b.IsSelected = s == size;
        StyleEditor();
    }

    public void RequestSave()
    {
        CommitText();
        if (CanSave && !_confirming) SaveRequested?.Invoke();
    }

    public void RequestCopy()
    {
        CommitText();
        if (CanSave && !_confirming) CopyRequested?.Invoke();
    }

    public void RequestCancel()
    {
        if (_editor is not null) CancelText();
        CancelRequested?.Invoke();
    }

    /// <summary>Спросить «Закрыть без сохранения?» поверх картинки.</summary>
    public void ShowConfirm()
    {
        _confirming = true;
        _confirm.Visibility = Visibility.Visible;
    }

    public void HideConfirm()
    {
        _confirming = false;
        _confirm.Visibility = Visibility.Collapsed;
    }

    public bool IsConfirming => _confirming;

    /// <summary>Клавиши окна (PreviewKeyDown): true — обработано.</summary>
    public bool HandleKey(Key key, ModifierKeys modifiers)
    {
        var ctrl = modifiers.HasFlag(ModifierKeys.Control);
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (_confirming)
        {
            // Esc и Enter — «Вернуться» (главная кнопка вопроса): пометки случайно не пропадут.
            if (key is Key.Escape or Key.Enter) HideConfirm();
            return key is Key.Escape or Key.Enter;
        }
        if (_editor is not null)
        {
            if (key == Key.Escape) CancelText();
            else if (key == Key.Enter && !shift) CommitText();
            else return false; // остальное печатает поле ввода
            return true;
        }
        if (_canvas.IsDrawing && key == Key.Escape)
        {
            _canvas.CancelDrawing();
            return true;
        }
        switch (key)
        {
            case Key.Escape:
                RequestCancel();
                return true;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                Document.Redo();
                return true;
            case Key.Z when ctrl:
                Document.Undo();
                return true;
            case Key.C when ctrl:
                RequestCopy();
                return true;
            case Key.S when ctrl:
                RequestSave();
                return true;
            case Key.Delete or Key.Back when modifiers == ModifierKeys.None:
                Document.RemoveLast();
                return true;
        }
        if (modifiers == ModifierKeys.None)
        {
            var index = key is >= Key.D1 and <= Key.D8 ? key - Key.D1 : key is >= Key.NumPad1 and <= Key.NumPad8 ? key - Key.NumPad1 : -1;
            if (index >= 0)
            {
                SelectTool(Tools[index]);
                return true;
            }
        }
        return false;
    }

    /// <summary>Окно закрылось: отпустить картинку и клетки размытия.</summary>
    public void Release()
    {
        Renderer?.Clear();
        Renderer = null;
        _canvas.InvalidateVisual();
    }

    private void OnDocumentChanged()
    {
        _statusText = null;
        _canvas.InvalidateVisual();
        UpdateButtons();
        UpdateStatus();
    }

    // ---------- текст ----------

    /// <summary>Поле ввода текста в точке картинки (щелчок инструментом «Текст»).</summary>
    internal void BeginText(ImagePoint at)
    {
        CommitText();
        if (Renderer is null) return;
        _editorAt = at;
        _editor = new TextBox
        {
            AcceptsReturn = true, BorderThickness = new Thickness(1), Padding = new Thickness(0), MinWidth = 40,
            Background = SettingsPalette.B(System.Windows.Media.Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
            FontFamily = UiFonts.Display, FontWeight = FontWeights.SemiBold,
        };
        TextOptions.SetTextFormattingMode(_editor, TextFormattingMode.Ideal);
        var editor = _editor;
        // Ушли из поля (другое окно, Tab) — текст готов; новое поле к тому времени уже своё, его не трогаем.
        editor.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (ReferenceEquals(_editor, editor)) CommitText();
        });
        StyleEditor();
        _textLayer.Children.Add(_editor);
        PlaceEditor();
        _editor.Focus();
        Keyboard.Focus(_editor);
        UpdateStatus();
    }

    /// <summary>Готово с текстом: Enter, щелчок мимо, другой инструмент, «Сохранить». Пустой текст — ничего.</summary>
    internal void CommitText()
    {
        if (_editor is not { } editor) return;
        _editor = null;
        _textLayer.Children.Remove(editor);
        var text = editor.Text.TrimEnd();
        var shape = new Annotation(AnnotationTool.Text, [_editorAt], Color, Size) { Text = text };
        if (shape.IsMeaningful()) Document.Add(shape);
        UpdateStatus();
    }

    private void CancelText()
    {
        if (_editor is not { } editor) return;
        _editor = null;
        _textLayer.Children.Remove(editor);
        UpdateStatus();
    }

    private void StyleEditor()
    {
        if (_editor is null || Renderer is null) return;
        var color = AnnotationRenderer.ToColor(Color);
        _editor.Foreground = SettingsPalette.B(color);
        _editor.CaretBrush = SettingsPalette.B(color);
        _editor.BorderBrush = _palette.Accent;
        _editor.FontSize = Math.Max(8, Renderer.FontSize(Size) * _canvas.Mapping.Scale);
    }

    internal void PlaceEditor()
    {
        if (_editor is null) return;
        var (x, y) = _canvas.Mapping.ToView(_editorAt);
        // Поле ввода сдвинуто на свою рамку и внутренний отступ — текст встаёт туда же, куда ляжет пометка.
        Canvas.SetLeft(_editor, x - 3);
        Canvas.SetTop(_editor, y - 1);
        StyleEditor();
    }

    // ---------- вид ----------

    private void Build()
    {
        var p = _palette;
        Background = p.Background;
        CommitText();
        var root = new DockPanel { LastChildFill = true };

        // Верх: инструменты · цвета · толщина · отменить/повторить/удалить.
        var top = new Border { BorderBrush = p.Divider, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(16, 10, 16, 10) };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal };
        _toolButtons.Clear();
        _colorButtons.Clear();
        _sizeButtons.Clear();
        for (var i = 0; i < Tools.Length; i++)
        {
            var tool = Tools[i];
            var name = Strings.Annotate.Tool(tool);
            var button = new ChoiceButton(p, ToolIcon(tool, p), $"{name} ({i + 1})", name) { IsSelected = tool == Tool };
            button.Click += () => SelectTool(tool);
            _toolButtons.Add((tool, button));
            bar.Children.Add(button);
        }
        bar.Children.Add(Separator(p));
        foreach (var color in AnnotationColors.All)
        {
            var dot = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = SettingsPalette.B(AnnotationRenderer.ToColor(color)),
                BorderBrush = p.ControlBorder, BorderThickness = new Thickness(1),
            };
            var name = Strings.Annotate.Color(color);
            var button = new ChoiceButton(p, dot, name, name, round: true) { IsSelected = color == Color };
            button.Click += () => SelectColor(color);
            _colorButtons.Add((color, button));
            bar.Children.Add(button);
        }
        bar.Children.Add(Separator(p));
        foreach (var size in new[] { StrokeSize.Thin, StrokeSize.Medium, StrokeSize.Thick })
        {
            var d = size switch { StrokeSize.Thin => 4.0, StrokeSize.Medium => 7.0, _ => 11.0 };
            var dot = new Border { Width = d, Height = d, CornerRadius = new CornerRadius(d / 2), Background = p.Text };
            var name = Strings.Annotate.Size(size);
            var button = new ChoiceButton(p, dot, name, name) { IsSelected = size == Size };
            button.Click += () => SelectSize(size);
            _sizeButtons.Add((size, button));
            bar.Children.Add(button);
        }
        bar.Children.Add(Separator(p));
        _undo = new SettingsButton(p, null, Glyphs.Undo, Loc.T.AnnotateUndo, subtle: true) { Width = 32, Height = 32 };
        _undo.Click += () => Document.Undo();
        _redo = new SettingsButton(p, null, Glyphs.Redo, Loc.T.AnnotateRedo, subtle: true) { Width = 32, Height = 32 };
        _redo.Click += () => Document.Redo();
        _delete = new SettingsButton(p, null, Glyphs.Delete, Loc.T.AnnotateDeleteLast, subtle: true) { Width = 32, Height = 32 };
        _delete.Click += () => Document.RemoveLast();
        bar.Children.Add(_undo);
        bar.Children.Add(_redo);
        bar.Children.Add(_delete);
        top.Child = bar;
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        // Низ: «Копировать» и подсказки слева, «Сохранить» / «Отмена» справа.
        var footer = new Border { BorderBrush = p.Divider, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 12, 16, 14) };
        var bottom = new DockPanel { LastChildFill = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _save = new TrimView.PrimaryButton(p, Loc.T.AnnotateSave) { MinWidth = 120 };
        _save.Click += RequestSave;
        var cancel = new SettingsButton(p, Loc.T.Cancel) { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += RequestCancel;
        buttons.Children.Add(_save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        _copy = new SettingsButton(p, Loc.T.AnnotateCopy, Glyphs.Copy) { VerticalAlignment = VerticalAlignment.Center };
        _copy.Click += RequestCopy;
        DockPanel.SetDock(_copy, Dock.Left);
        bottom.Children.Add(_copy);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        _status = new TextBlock { FontSize = 12.5, Foreground = p.TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis };
        texts.Children.Add(_status);
        texts.Children.Add(new TextBlock
        {
            Text = Loc.F(Loc.T.AnnotateSaveHint, _saveHint), FontSize = 12, Foreground = p.TextTertiary,
            TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = _saveHint,
        });
        bottom.Children.Add(texts);
        footer.Child = bottom;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        // Середина: шахматка (видна прозрачность), картинка с пометками, поле текста, сообщение, вопрос.
        var stage = new Grid { ClipToBounds = true, Background = BeautifyView.Checker(p.IsLight) };
        Detach(_canvas);
        Detach(_textLayer);
        stage.Children.Add(_canvas);
        stage.Children.Add(_textLayer);
        _overlay = new TextBlock
        {
            Text = _message ?? "", FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(14, 8, 14, 9),
            Foreground = SettingsPalette.B(Colors.White), Background = SettingsPalette.B(System.Windows.Media.Color.FromArgb(0xA0, 0x20, 0x20, 0x20)),
            Visibility = string.IsNullOrEmpty(_message) ? Visibility.Collapsed : Visibility.Visible,
        };
        stage.Children.Add(_overlay);
        _confirm = BuildConfirm(p);
        stage.Children.Add(_confirm);
        root.Children.Add(stage);

        Child = root;
        _canvas.Cursor = Tool == AnnotationTool.Text ? Cursors.IBeam : Cursors.Cross;
        _canvas.InvalidateVisual();
        UpdateButtons();
        UpdateStatus();
    }

    private Border BuildConfirm(SettingsPalette p)
    {
        var card = new Border
        {
            Background = SettingsPalette.B(p.Solid), BorderBrush = p.CardBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(24, 20, 24, 20), MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Direction = 270, Opacity = 0.3 },
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = Loc.T.AnnotateDiscardTitle, FontFamily = UiFonts.Display, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = p.Text });
        body.Children.Add(new TextBlock
        {
            Text = Loc.T.AnnotateDiscardText, FontSize = 13.5, Foreground = p.TextSecondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 18),
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var keep = new TrimView.PrimaryButton(p, Loc.T.AnnotateKeep) { MinWidth = 110 };
        keep.Click += HideConfirm;
        var discard = new SettingsButton(p, Loc.T.AnnotateDiscard) { MinWidth = 110, Margin = new Thickness(8, 0, 0, 0) };
        discard.Click += () => DiscardConfirmed?.Invoke();
        row.Children.Add(keep);
        row.Children.Add(discard);
        body.Children.Add(row);
        card.Child = body;
        return new Border
        {
            Background = SettingsPalette.B(System.Windows.Media.Color.FromArgb(0x66, 0, 0, 0)), Child = card,
            Visibility = _confirming ? Visibility.Visible : Visibility.Collapsed,
        };
    }

    private static void Detach(FrameworkElement element)
    {
        if (element.Parent is Panel panel) panel.Children.Remove(element);
    }

    private static Border Separator(SettingsPalette p) =>
        new() { Width = 1, Height = 20, Margin = new Thickness(6, 6, 6, 6), Background = p.Divider, VerticalAlignment = VerticalAlignment.Center };

    private void UpdateButtons()
    {
        _undo.SetEnabled(Document.CanUndo);
        _redo.SetEnabled(Document.CanRedo);
        _delete.SetEnabled(!Document.IsEmpty);
        _save.SetEnabled(CanSave);
        _copy.SetEnabled(CanSave);
    }

    private void UpdateStatus()
    {
        if (_status is null) return;
        _status.Text = _statusText ?? (_editor is not null ? Loc.T.AnnotateTextHint : Loc.T.AnnotateKeysHint);
    }

    /// <summary>Значки инструментов — нарисованные (16 × 16): в шрифте значков есть не все, а на Windows 10 — ещё меньше.</summary>
    private static FrameworkElement ToolIcon(AnnotationTool tool, SettingsPalette p)
    {
        var group = new DrawingGroup();
        var text = p.Text;
        var c = (text as SolidColorBrush)?.Color ?? Colors.Gray;
        void Stroke(Geometry g, double w = 1.5, Brush? brush = null) =>
            group.Children.Add(new GeometryDrawing(null, new Pen(brush ?? text, w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, g));
        void Fill(Geometry g, Brush brush) => group.Children.Add(new GeometryDrawing(brush, null, g));
        Fill(new RectangleGeometry(new Rect(0, 0, 16, 16)), Brushes.Transparent);
        switch (tool)
        {
            case AnnotationTool.Arrow:
                Stroke(Geometry.Parse("M3,13 L12.5,3.5"));
                Stroke(Geometry.Parse("M6.5,3 L13,3 L13,9.5"));
                break;
            case AnnotationTool.Rectangle:
                Stroke(new RectangleGeometry(new Rect(2.5, 3.5, 11, 9), 1.5, 1.5));
                break;
            case AnnotationTool.Ellipse:
                Stroke(new EllipseGeometry(new Point(8, 8), 6, 4.8));
                break;
            case AnnotationTool.Pen:
                Stroke(Geometry.Parse("M2,12 C4.5,3 7,3 8,8 C9,13 11.5,13 14,4"));
                break;
            case AnnotationTool.Marker:
            {
                Stroke(Geometry.Parse("M3,11.5 L13,5"), 5, SettingsPalette.B(System.Windows.Media.Color.FromArgb(0x70, c.R, c.G, c.B)));
                Stroke(Geometry.Parse("M3,14.5 L13,14.5"), 1.2);
                break;
            }
            case AnnotationTool.Text:
                Stroke(Geometry.Parse("M3,3.5 L13,3.5 M8,3.5 L8,13.5 M6,13.5 L10,13.5"), 1.6);
                break;
            case AnnotationTool.Blur:
            {
                var faint = SettingsPalette.B(System.Windows.Media.Color.FromArgb(0x55, c.R, c.G, c.B));
                for (var y = 0; y < 3; y++)
                    for (var x = 0; x < 3; x++)
                        Fill(new RectangleGeometry(new Rect(2 + x * 4.2, 2 + y * 4.2, 3.6, 3.6)), (x + y) % 2 == 0 ? text : faint);
                break;
            }
            case AnnotationTool.Number:
                Fill(new EllipseGeometry(new Point(8, 8), 6.5, 6.5), text);
                Stroke(Geometry.Parse("M6.6,6 L8.4,4.8 L8.4,11.4"), 1.5, SettingsPalette.B(p.Solid));
                break;
        }
        group.Freeze();
        return new Image { Source = new DrawingImage(group), Width = 16, Height = 16 };
    }

    // ---------- кнопка выбора ----------

    /// <summary>Кнопка инструмента, цвета или толщины: выбранная — с рамкой цвета акцента.</summary>
    private sealed class ChoiceButton : Pressable
    {
        private bool _selected;

        public ChoiceButton(SettingsPalette palette, UIElement content, string tooltip, string name, bool round = false) : base(palette)
        {
            Width = 32;
            Height = 32;
            Margin = new Thickness(0, 0, 2, 0);
            CornerRadius = new CornerRadius(round ? 16 : 4);
            Cursor = Cursors.Arrow;
            Child = content;
            if (content is FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Center;
                fe.VerticalAlignment = VerticalAlignment.Center;
            }
            ToolTip = palette.MakeToolTip(tooltip);
            AutomationProperties.SetName(this, name);
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
            BorderThickness = new Thickness(_selected ? 2 : 1);
            Background = IsPressed ? p.ControlPressed : _selected ? p.ControlHover : IsHovered ? p.ControlHover : Brushes.Transparent;
            BorderBrush = _selected ? p.Accent : HasKeyboardFocus ? p.Text : Brushes.Transparent;
        }
    }

    // ---------- холст ----------

    /// <summary>
    /// Картинка, вписанная в свою область, и пометки поверх — тот же код рисования, что у результата, под масштабом.
    /// Мышь: тянуть — фигура (Shift — ровно), щелчок «Текстом» — поле ввода, щелчок «Номером» — кружок.
    /// </summary>
    private sealed class AnnotateCanvas : FrameworkElement
    {
        private readonly AnnotateView _view;
        private Annotation? _drawing;
        private ImagePoint _start;

        public AnnotateCanvas(AnnotateView view)
        {
            _view = view;
            ClipToBounds = true;
            Focusable = false;
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        }

        public ViewMapping Mapping { get; private set; }
        public bool IsDrawing => _drawing is not null;

        private ViewMapping Fit()
        {
            var r = _view.Renderer;
            var dpi = Math.Max(0.5, VisualTreeHelper.GetDpi(this).DpiScaleX);
            // Отступ 16 DIP — картинка не прилипает к краям; маленькая — не больше «пиксель в пиксель».
            var m = r is null ? default : ViewMapping.Fit(r.Width, r.Height, Math.Max(1, ActualWidth - 32), Math.Max(1, ActualHeight - 32), 1 / dpi);
            return m with { OffsetX = Math.Round(m.OffsetX + 16), OffsetY = Math.Round(m.OffsetY + 16) };
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
            if (_view.Renderer is not { } r) return;
            Mapping = Fit();
            var (x, y, w, h) = Mapping.Shown;
            dc.DrawRectangle(null, new Pen(SettingsPalette.B(System.Windows.Media.Color.FromArgb(0x30, 0, 0, 0)), 1), new Rect(x - 0.5, y - 0.5, w + 1, h + 1));
            dc.PushClip(new RectangleGeometry(new Rect(x, y, w, h)));
            dc.PushTransform(new MatrixTransform(Mapping.Scale, 0, 0, Mapping.Scale, Mapping.OffsetX, Mapping.OffsetY));
            dc.DrawImage(r.Image, new Rect(0, 0, r.Width, r.Height));
            r.Draw(dc, _view.Document.Shapes, _drawing);
            dc.Pop();
            dc.Pop();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            Mapping = Fit();
            _view.PlaceEditor();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            e.Handled = true;
            if (_view._editor is not null)
            {
                _view.CommitText(); // щелчок мимо поля — текст готов
                return;
            }
            if (_view.Renderer is null || _view._confirming) return;
            Mapping = Fit();
            var pos = e.GetPosition(this);
            var p = Mapping.ToImage(pos.X, pos.Y);
            if (!Mapping.Contains(p)) return;
            switch (_view.Tool)
            {
                case AnnotationTool.Number:
                    _view.Document.Add(new Annotation(AnnotationTool.Number, [p], _view.Color, _view.Size) { Number = _view.Document.NextNumber });
                    return;
                case AnnotationTool.Text:
                    _view.BeginText(p);
                    return;
            }
            _start = p;
            _drawing = new Annotation(_view.Tool, [p, p], _view.Color, _view.Size);
            if (_view.Tool is AnnotationTool.Pen or AnnotationTool.Marker) _drawing = _drawing with { Points = [p] };
            CaptureMouse();
            InvalidateVisual();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_drawing is not { } d || e.LeftButton != MouseButtonState.Pressed) return;
            var pos = e.GetPosition(this);
            var p = Mapping.Clamp(Mapping.ToImage(pos.X, pos.Y));
            if (d.Tool is AnnotationTool.Pen or AnnotationTool.Marker)
            {
                // Точка — не чаще, чем через 1.5 DIP: линия гладкая, а точек не тысячи.
                if (d.End.DistanceTo(p) * Mapping.Scale < 1.5) return;
                _drawing = d with { Points = [.. d.Points, p] };
            }
            else
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) p = Mapping.Clamp(AnnotationGeometry.Constrain(d.Tool, _start, p));
                _drawing = d with { Points = [_start, p] };
            }
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_drawing is not { } d) return;
            _drawing = null;
            ReleaseMouseCapture();
            // Порог «щелчок, а не фигура» — 3 DIP на экране, в пикселях картинки.
            if (d.IsMeaningful(3 / Math.Max(0.0001, Mapping.Scale))) _view.Document.Add(d);
            else InvalidateVisual();
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            if (_drawing is null) return;
            e.Handled = true;
            CancelDrawing();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_drawing is null) return;
            _drawing = null;
            InvalidateVisual();
        }

        public void CancelDrawing()
        {
            _drawing = null;
            if (IsMouseCaptured) ReleaseMouseCapture();
            InvalidateVisual();
        }
    }
}
