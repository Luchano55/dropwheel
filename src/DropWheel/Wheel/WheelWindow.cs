using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Wheel;

/// <summary>
/// Прозрачное окно поверх всех с колесом. Одно на сценарий, переиспользуется (Show/Hide).
/// Сценарий перетаскивания: ShowAt(x, y) → DragEnter → FilesEntered → хост вызывает SetItems → Drop → FilesDropped.
/// Сценарий меню проводника: SetItems → CloseOnDeactivate = true → ShowAt(x, y) → клик → ItemChosen.
/// После закрытия колесо сбрасывается в «ждём файлы» (без секторов и без «Нечего предложить»).
/// Прятать колесо снаружи — через Cancel(), а не Hide(): так сбрасывается состояние.
/// <para>
/// Вид: круглое окно (SetWindowRgn) с системным акриловым размытием под ним, если Windows это умеет
/// и «Эффекты прозрачности» включены; мягкую тень тогда рисует отдельное прозрачное для мыши окно
/// под колесом (круглая форма обрезала бы её). Без размытия — плотный диск с тенью в том же окне.
/// Открытие — «вырастает» из точки курсора: 0.3 → 1.03 → 1 с проявлением (~270 мс); закрытие — 110 мс;
/// при броске на лепесток — короткая вспышка перед закрытием. Без анимаций Windows всё мгновенно.
/// </para>
/// </summary>
public sealed class WheelWindow : Window
{
    /// <summary>Сторона окна в DIP: диск 288 + место для тени.</summary>
    public const double DipSize = 312;

    private const double OpenMs = 270;
    private const double CloseMs = 110;
    private const double AcceptMs = 110;
    private const double OpenScale = 0.3;
    private const double CloseScale = 0.85;
    /// <summary>Отступ диска от края окна, DIP.</summary>
    private const double DiscInset = DipSize / 2 - WheelControl.DiscRadius;

    private readonly WheelControl _wheel = new();
    private readonly WheelState _state = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Tween _tween = new();

    private IntPtr _hwnd;
    private int _sizePx;               // сторона окна в физических пикселях
    private int _leftPx, _topPx;       // левый верхний угол окна
    private uint _dpi = 96;
    private bool _blur;                // под окном системное размытие, окно круглое
    private Point _origin = new(DipSize / 2, DipSize / 2); // откуда «вырастает» колесо (DIP окна)
    private WheelShadowWindow? _shadow;
    private bool _shadowFailed;

    private string[]? _files;          // пути текущего перетаскивания
    private Point? _lastPoint;         // последняя позиция курсора над колесом
    private Point? _lastMouse;         // для отличия настоящего движения мыши от синтетического
    private (int X, int Y)? _anchor;   // куда просили поставить центр (физические пиксели)
    private bool _pressed;
    private int? _pressIndex;
    private bool _pressCenter;
    private bool _pressDots;
    private bool _keyboard;            // подсветку ведут стрелки, а не мышь
    private bool _open;                // показано и не закрывается
    private bool _closing;             // идёт анимация закрытия
    private bool _hiding;              // Hide() сам вызывает Deactivated — не считать это отменой

    /// <summary>Выбран сектор кликом: id операции. Окно уже закрывается.</summary>
    public event Action<string>? ItemChosen;
    /// <summary>Esc, клик/drop в центр, drop мимо секторов или на «Нечего предложить». Окно уже закрывается.</summary>
    public event Action? Canceled;
    /// <summary>
    /// Над колесом появились файлы (DragEnter с FileDrop). Хост считает сектора и тут же вызывает SetItems.
    /// Вызывается внутри OLE-цикла перетаскивания — обработчик должен быть быстрым.
    /// </summary>
    public event Action<string[]>? FilesEntered;
    /// <summary>Файлы брошены на сектор: (пути, id операции). Окно уже закрывается; обработчик должен быть быстрым.</summary>
    public event Action<string[], string>? FilesDropped;

    /// <summary>Прятать колесо при потере фокуса (режим клика из меню проводника).</summary>
    public bool CloseOnDeactivate { get; set; }

    /// <summary>Плотность заливок колеса, % (настройка «Прозрачность колеса»); меняется и у открытого колеса.</summary>
    public int FillPercent
    {
        get => _wheel.FillPercent;
        set => _wheel.FillPercent = value;
    }

    /// <summary>Колесо открыто и ждёт выбора (во время анимации закрытия — уже false).</summary>
    public bool IsOpen => _open;

    public WheelWindow()
    {
        Title = "DropWheel";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        AllowDrop = true;
        Focusable = true;
        Width = Height = DipSize;
        UseLayoutRounding = true;

        _wheel.HorizontalAlignment = HorizontalAlignment.Center;
        _wheel.VerticalAlignment = VerticalAlignment.Center;
        _wheel.IsPending = true;
        _wheel.RenderTransform = _scale;

        // Почти невидимая (альфа 1/255) подложка диска без анимации: у слоистого окна пиксели с нулевой
        // альфой «проницаемы» для мыши и перетаскивания, а колесо появляется из прозрачности —
        // без подложки первый DragEnter мог бы уйти окну под колесом.
        var backing = new System.Windows.Shapes.Ellipse
        {
            Width = 2 * WheelControl.DiscRadius,
            Height = 2 * WheelControl.DiscRadius,
            Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var root = new System.Windows.Controls.Grid();
        root.Children.Add(backing);
        root.Children.Add(_wheel);
        Content = root;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WheelNative.MakeToolWindow(_hwnd);
        };
        // При переезде на монитор с другим масштабом WPF сам меняет размер окна — потом ставим своё место ещё раз.
        DpiChanged += (_, _) =>
        {
            if (_anchor is { } a && IsVisible) Dispatcher.InvokeAsync(() => Place(a.X, a.Y));
        };
        SystemTheme.Changed += OnThemeChanged;
    }

    /// <summary>
    /// Показать колесо с центром в точке экрана (физические пиксели — из хуков или GetCursorPos).
    /// Активирует окно, только если CloseOnDeactivate (режим клика); при перетаскивании фокус не забираем.
    /// </summary>
    public void ShowAt(int screenX, int screenY) => ShowAt(screenX, screenY, CloseOnDeactivate);

    /// <summary>
    /// Показать колесо с центром в точке экрана (физические пиксели).
    /// activate = false — для перетаскивания (цикл OLE принадлежит источнику, окно не активируем);
    /// activate = true — для клика, чтобы работали Esc, Tab и стрелки.
    /// </summary>
    public void ShowAt(int screenX, int screenY, bool activate)
    {
        FinishClose();
        _anchor = (screenX, screenY);
        ShowActivated = activate;
        _keyboard = false;
        _lastMouse = null;
        UpdateHover(null);
        _wheel.EmptyHint = activate ? Loc.T.WheelEmptyClick : Loc.T.WheelEmptyDrag;

        new WindowInteropHelper(this).EnsureHandle();
        ApplyTheme();

        var appearing = !IsVisible;
        // Дважды: первый SetWindowPos может перенести окно на монитор с другим DPI,
        // и WPF пересчитает размер; второй ставит точные координаты и размер.
        Place(screenX, screenY);
        Place(screenX, screenY);
        if (appearing && Motion.Enabled) SetAppearance(OpenScale, 0);
        if (appearing)
        {
            if (_blur) ShowShadow();
            Show();
        }
        Place(screenX, screenY);
        _open = true;

        if (appearing)
            _tween.Run(OpenMs, t => t, t => SetAppearance(OpenScale + (1 - OpenScale) * OpenEase(t), Math.Min(1, t / 0.55)));

        if (activate)
        {
            WheelNative.TrySetForeground(_hwnd);
            Activate();
            Focus();
            // Как у всплывающих меню: мышь у колеса, клик мимо закрывает его, даже если Windows
            // не дала окну стать активным (тогда Deactivated не придёт).
            Mouse.Capture(this, CaptureMode.SubTree);
        }
    }

    /// <summary>
    /// Наборы секторов и файлы (для размера в центре — режим клика из меню проводника).
    /// Режим — «Конвертация» (или «Инструменты», если конвертировать нечего).
    /// </summary>
    public void SetItems(IReadOnlyList<WheelItem> convertItems, IReadOnlyList<WheelItem> toolItems, IReadOnlyList<string>? paths)
    {
        FinishClose();
        _wheel.IdleText = SizeText(paths);
        SetItems(convertItems, toolItems);
    }

    /// <summary>Наборы секторов; режим — «Конвертация» (или «Инструменты», если конвертировать нечего).</summary>
    public void SetItems(IReadOnlyList<WheelItem> convertItems, IReadOnlyList<WheelItem> toolItems)
    {
        FinishClose();
        _state.SetItems(convertItems, toolItems);
        _wheel.IsPending = false;
        ApplyState();
    }

    /// <summary>
    /// Конвертация ↔ Инструменты. Вызывает хост по повторному нажатию горячей клавиши;
    /// в режиме клика — Tab, правая кнопка или клик по точкам. Если второй режим пуст — ничего не делает.
    /// </summary>
    public void ToggleMode()
    {
        if (!_open || !_state.HasBothModes) return;
        _state.Toggle();
        ApplyState();
    }

    /// <summary>Спрятать колесо с событием Canceled (например, кнопку отпустили, а DragEnter так и не пришёл).</summary>
    public void Cancel()
    {
        if (!_open) return;
        CloseAnimated(null);
        Canceled?.Invoke();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemTheme.Changed -= OnThemeChanged;
        _tween.Stop(complete: false);
        _shadow?.Close();
        base.OnClosed(e);
    }

    // ---- мышь и клавиатура (режим клика) ----

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_open) return;
        var p = e.GetPosition(_wheel);
        // WPF присылает MouseMove и без движения (окно появилось под курсором) — стрелки не сбиваем.
        if (_keyboard && _lastMouse is { } last && (p - last).Length < 2) return;
        _lastMouse = p;
        _keyboard = false;
        UpdateHover(p);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_open || _keyboard) return;
        UpdateHover(null);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_open) return;
        var p = e.GetPosition(_wheel);
        if (!_wheel.IsOnDisc(p))
        {
            // Клик мимо колеса (мышь захвачена в режиме клика) — закрыть, как меню.
            if (CloseOnDeactivate)
            {
                e.Handled = true;
                Cancel();
            }
            return;
        }
        _pressed = true;
        _pressDots = _wheel.IsOnModeDots(p);
        _pressIndex = _pressDots ? null : _wheel.HitTest(p);
        _pressCenter = !_pressDots && _wheel.IsCenter(p);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Отпускание без нажатия над колесом (хвост клика из меню проводника) — не считается.
        if (!_open || !_pressed) return;
        _pressed = false;
        e.Handled = true;

        var p = e.GetPosition(_wheel);
        if (_pressDots)
        {
            if (_wheel.IsOnModeDots(p)) ToggleMode();
            return;
        }
        if (_state.IsEmpty)
        {
            // «Нечего предложить» — клик в любое место = отмена; пока ждём файлы — только центр.
            if (_wheel.IsPending ? _wheel.IsCenter(p) : _wheel.IsOnDisc(p)) Cancel();
            return;
        }

        var index = _wheel.HitTest(p);
        if (index is { } i && index == _pressIndex) Choose(i);
        else if (_pressCenter && _wheel.IsCenter(p)) Cancel();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!_open) return;
        e.Handled = true;
        if (_wheel.IsOnDisc(e.GetPosition(_wheel))) ToggleMode();
        else if (CloseOnDeactivate) Cancel();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_open || !CloseOnDeactivate) return;
        e.Handled = true;
        MoveSelection(e.Delta < 0 ? +1 : -1);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Захват теряется, когда пользователь ушёл в другое окно (Alt+Tab, клик по панели задач).
        if (_open && CloseOnDeactivate && !_hiding && Mouse.Captured is null) Cancel();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_open) return;
        switch (e.Key)
        {
            // Alt+F4 тоже просто прячет: окно переиспользуется и закрываться не должно.
            case Key.Escape:
            case Key.System when e.SystemKey == Key.F4:
                Cancel();
                break;
            case Key.Tab:
                ToggleMode();
                break;
            case Key.Right or Key.Down:
                MoveSelection(+1);
                break;
            case Key.Left or Key.Up:
                MoveSelection(-1);
                break;
            case Key.Enter or Key.Space:
                if (_wheel.HoverIndex is { } i) Choose(i);
                else if (_wheel.IsCenterHovered || _state.IsEmpty) Cancel();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (CloseOnDeactivate && !_hiding && _open) Cancel();
    }

    // ---- перетаскивание ----

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        e.Handled = true;
        if (!_open)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        var files = ReadFiles(e.Data);
        if (files is { Length: > 0 })
        {
            // Вернулись с теми же файлами — сектора и выбранный режим уже есть.
            var same = _files is not null && !_wheel.IsPending && _files.SequenceEqual(files);
            _files = files;
            if (!same)
            {
                _wheel.IdleText = SizeText(files);
                FilesEntered?.Invoke(files);
            }
        }
        else
        {
            // TODO(D5): из браузера приходит ссылка или «виртуальный файл» (FileGroupDescriptor/FileContents),
            // а не путь на диске. Пока решение D5 не принято — показываем «Нечего предложить».
            _files = null;
            SetItems([], []);
        }
        UpdateHover(e.GetPosition(_wheel));
        e.Effects = EffectFor(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Handled = true;
        if (!_open)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        UpdateHover(e.GetPosition(_wheel));
        e.Effects = EffectFor(e);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        // Не прячемся: пользователь может вернуться к колесу.
        if (_open) UpdateHover(null);
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;
        if (!_open)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        var index = _wheel.HitTest(e.GetPosition(_wheel));
        var item = _state.At(index);
        var files = _files;
        var ok = item is not null && files is { Length: > 0 } && e.AllowedEffects.HasFlag(DragDropEffects.Copy);
        // Только Copy: Move заставил бы источник удалить исходник.
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;

        CloseAnimated(ok ? index : null);
        if (ok) FilesDropped?.Invoke(files!, item!.Id);
        else Canceled?.Invoke();
    }

    // ---- внутреннее ----

    private void Choose(int index)
    {
        if (_state.At(index) is not { } item) return;
        CloseAnimated(index);
        ItemChosen?.Invoke(item.Id);
    }

    private void MoveSelection(int delta)
    {
        var n = _state.Items.Count;
        if (n == 0) return;
        _keyboard = true;
        var current = _wheel.HoverIndex ?? (delta > 0 ? -1 : 0);
        _wheel.HoverIndex = ((current + delta) % n + n) % n;
        _wheel.IsCenterHovered = false;
        _wheel.IsDotsHovered = false;
    }

    private DragDropEffects EffectFor(DragEventArgs e) =>
        _files is { Length: > 0 } && _state.At(_wheel.HoverIndex) is not null
        && e.AllowedEffects.HasFlag(DragDropEffects.Copy)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private static string[]? ReadFiles(IDataObject data)
    {
        try
        {
            return data.GetDataPresent(DataFormats.FileDrop) ? data.GetData(DataFormats.FileDrop) as string[] : null;
        }
        catch (Exception)
        {
            // Некоторые источники бросают COMException на чужих форматах.
            return null;
        }
    }

    private void ApplyState()
    {
        _wheel.Items = _state.Items;
        _wheel.Mode = _state.Mode;
        _wheel.ShowModeDots = _state.HasBothModes;
        if (_keyboard && _wheel.HoverIndex is { } i && i >= _state.Items.Count) _wheel.HoverIndex = null;
        if (!_keyboard) UpdateHover(_lastPoint);
    }

    private void UpdateHover(Point? p)
    {
        _lastPoint = p;
        _wheel.HoverIndex = p is { } pt ? _wheel.HitTest(pt) : null;
        var dots = p is { } pd && _wheel.IsOnModeDots(pd);
        _wheel.IsDotsHovered = dots;
        _wheel.IsCenterHovered = !dots && p is { } pc && _wheel.IsCenter(pc);
    }

    /// <summary>Тема, акцент и фон: размытие под окном, если можно, иначе плотный диск с тенью.</summary>
    private void ApplyTheme()
    {
        var palette = WheelPalette.FromSystem();
        _wheel.Palette = palette;

        // Системное размытие на прозрачном (layered) окне WPF Windows рисует квадратом, даже с круглым регионом —
        // так было на живой записи (CI, Windows Server 2025). Поэтому колесо рисует «стекло» само (полупрозрачный
        // диск и тень в своём окне), а размытие оставлено экспериментом: DROPWHEEL_WHEEL_BLUR=1.
        var wantBlur = Environment.GetEnvironmentVariable("DROPWHEEL_WHEEL_BLUR") == "1"
            && SystemTheme.TransparencyEnabled && NativeEffects.SupportsAcrylic && NativeEffects.EffectsAllowed;
        var blur = wantBlur && NativeEffects.SetAcrylic(_hwnd, true, palette.AcrylicTint);
        if (!blur && _blur)
        {
            NativeEffects.SetAcrylic(_hwnd, false);
            NativeEffects.SetCircleRegion(_hwnd, 0, 0, 0);
        }
        _blur = blur;
        _wheel.Backdrop = blur ? WheelBackdrop.Translucent : WheelBackdrop.Solid;
        // С размытием окно круглое и тень обрезалась бы — её рисует окно под колесом.
        _wheel.DrawShadow = !blur;
        if (blur) _shadow?.SetColor(palette.Shadow);
        else _shadow?.Hide();
        UpdateRegion();
    }

    private void OnThemeChanged()
    {
        if (IsVisible) ApplyTheme();
    }

    /// <summary>Открытие: быстрый рост с небольшим «перелётом» (пик ~1.03 на 65 % времени) и успокоение.</summary>
    private static double OpenEase(double t)
    {
        const double c1 = 1.07, c3 = c1 + 1;
        var u = t - 1;
        return 1 + c3 * u * u * u + c1 * u * u;
    }

    /// <summary>Масштаб и прозрачность колеса (и тени) относительно точки «роста» _origin.</summary>
    private void SetAppearance(double scale, double opacity)
    {
        _scale.CenterX = _origin.X - DiscInset;
        _scale.CenterY = _origin.Y - DiscInset;
        _scale.ScaleX = _scale.ScaleY = scale;
        _wheel.Opacity = opacity;
        _shadow?.SetAppearance(_origin, scale, opacity);
        UpdateRegion();
    }

    /// <summary>Круглая форма окна по текущему положению и размеру диска — только при размытии (оно рисуется на всё окно).</summary>
    private void UpdateRegion()
    {
        if (!_blur || _sizePx <= 0) return;
        var s = _scale.ScaleX;
        var k = _dpi / 96.0;
        var c = DipSize / 2;
        var cx = (_origin.X + (c - _origin.X) * s) * k;
        var cy = (_origin.Y + (c - _origin.Y) * s) * k;
        var r = Math.Max(1, WheelControl.DiscRadius * s * k);
        if (!NativeEffects.SetCircleRegion(_hwnd, cx, cy, r))
        {
            // Форма не встала — без неё размытие было бы квадратным. Остаёмся на плотном диске.
            NativeEffects.SetAcrylic(_hwnd, false);
            _blur = false;
            _wheel.Backdrop = WheelBackdrop.Solid;
            _wheel.DrawShadow = true;
            _shadow?.Hide();
        }
    }

    /// <summary>Окно тени под колесом (только при размытии). Не получилось создать — колесо просто без тени.</summary>
    private void ShowShadow()
    {
        if (_shadowFailed) return;
        try
        {
            _shadow ??= new WheelShadowWindow();
            _shadow.SetColor(_wheel.Palette?.Shadow ?? WheelPalette.FromSystem().Shadow);
            _shadow.SetAppearance(_origin, _scale.ScaleX, _wheel.Opacity);
            _shadow.PlaceBelow(_hwnd, _leftPx, _topPx, _sizePx);
            _shadow.Show();
            _shadow.PlaceBelow(_hwnd, _leftPx, _topPx, _sizePx);
        }
        catch (Exception)
        {
            _shadowFailed = true;
            _shadow = null;
        }
    }

    /// <summary>Общий размер файлов для пилюли в центре («1,1 МБ»); null — неизвестен.</summary>
    private static string? SizeText(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0) return null;
        long total = 0;
        var any = false;
        foreach (var path in paths.Take(1000))
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) continue;
                total += info.Length;
                any = true;
            }
            catch (Exception)
            {
                // Нет доступа — просто не считаем этот файл.
            }
        }
        return any ? Loc.FileSize(total) : null;
    }

    /// <summary>Закрыть с анимацией; accepted — номер принятого сектора (сначала вспышка).</summary>
    private void CloseAnimated(int? accepted)
    {
        _open = false;
        _closing = true;
        _pressed = false;
        if (IsMouseCaptureWithin) Mouse.Capture(null);
        if (accepted is { } i && Motion.Enabled)
        {
            _wheel.Pulse(i);
            _tween.Run(AcceptMs, t => t, _ => { }, StartClosing);
        }
        else
        {
            StartClosing();
        }
    }

    private void StartClosing() =>
        _tween.Run(CloseMs, Motion.EaseInQuad, t => SetAppearance(1 - (1 - CloseScale) * t, 1 - t), FinishClose);

    /// <summary>Если колесо закрывается — спрятать сразу и сбросить (перед новым показом).</summary>
    private void FinishClose()
    {
        if (!_closing) return;
        _closing = false;
        _tween.Stop(complete: false);
        _hiding = true;
        try { Hide(); }
        finally { _hiding = false; }
        _shadow?.Hide();
        SetAppearance(1, 1);
        Reset();
    }

    /// <summary>После скрытия: ждём новых файлов, ничего не подсвечено.</summary>
    private void Reset()
    {
        _files = null;
        _pressed = false;
        _anchor = null;
        _keyboard = false;
        _state.SetItems([], []);
        _wheel.IdleText = null;
        _wheel.IsPending = true;
        ApplyState();
        UpdateHover(null);
        _wheel.SnapAnimations();
    }

    /// <summary>Поставить окно центром в точку: размер — DIP × DPI монитора под точкой, не вылезая за рабочую область.</summary>
    private void Place(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hwnd = hwnd;
        var (work, dpi) = WheelNative.MonitorAt(x, y);
        var size = WheelGeometry.ToPixels(DipSize, dpi);
        var (left, top) = WheelGeometry.PlaceInside(x, y, size, work.Left, work.Top, work.Right, work.Bottom);
        WheelNative.SetWindowPos(hwnd, WheelNative.HwndTopmost, left, top, size, size,
            WheelNative.SwpNoActivate | WheelNative.SwpNoOwnerZOrder);
        _leftPx = left;
        _topPx = top;
        _sizePx = size;
        _dpi = dpi;
        // Колесо «растёт» из точки курсора (у края экрана окно сдвинуто — точка не в центре).
        var k = dpi / 96.0;
        _origin = new Point(Math.Clamp((x - left) / k, 0, DipSize), Math.Clamp((y - top) / k, 0, DipSize));
        SetAppearance(_scale.ScaleX, _wheel.Opacity);
        if (_shadow is { IsVisible: true }) _shadow.PlaceBelow(hwnd, left, top, size);
    }
}
