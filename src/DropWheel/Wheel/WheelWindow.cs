using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
/// и «Эффекты прозрачности» включены; иначе — почти непрозрачный диск с мягкой тенью.
/// Открытие — увеличение 0.85→1 с проявлением (140 мс), закрытие — 90 мс, при броске на сектор —
/// короткая вспышка сектора перед закрытием. Без анимаций Windows всё мгновенно.
/// </para>
/// </summary>
public sealed class WheelWindow : Window
{
    /// <summary>Сторона окна в DIP.</summary>
    public const double DipSize = 300;

    private const double OpenMs = 140;
    private const double CloseMs = 90;
    private const double AcceptMs = 110;
    private const double OpenScale = 0.85;

    private readonly WheelControl _wheel = new();
    private readonly WheelState _state = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly Tween _tween = new();

    private IntPtr _hwnd;
    private int _sizePx;               // сторона окна в физических пикселях
    private uint _dpi = 96;
    private bool _blur;                // под окном системное размытие, окно круглое

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
        _wheel.RenderTransformOrigin = new Point(0.5, 0.5);
        _wheel.RenderTransform = _scale;
        Content = _wheel;

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
        _wheel.EmptyHint = activate ? "Щёлкните, чтобы закрыть" : "Отпустите — ничего не произойдёт";

        new WindowInteropHelper(this).EnsureHandle();
        ApplyTheme();

        var appearing = !IsVisible;
        if (appearing && Motion.Enabled) SetAppearance(OpenScale, 0);

        // Дважды: первый SetWindowPos может перенести окно на монитор с другим DPI,
        // и WPF пересчитает размер; второй ставит точные координаты и размер.
        Place(screenX, screenY);
        Place(screenX, screenY);
        if (appearing) Show();
        Place(screenX, screenY);
        _open = true;

        if (appearing)
            _tween.Run(OpenMs, Motion.EaseOutCubic, t => SetAppearance(OpenScale + (1 - OpenScale) * t, t));

        if (activate)
        {
            WheelNative.TrySetForeground(_hwnd);
            Activate();
            Focus();
        }
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
        if (!_wheel.IsOnDisc(p)) return;
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
        ToggleMode();
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
            if (!same) FilesEntered?.Invoke(files);
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

        var wantBlur = SystemTheme.TransparencyEnabled && NativeEffects.SupportsAcrylic && NativeEffects.EffectsAllowed;
        var blur = wantBlur && NativeEffects.SetAcrylic(_hwnd, true, palette.AcrylicTint);
        if (!blur && _blur)
        {
            NativeEffects.SetAcrylic(_hwnd, false);
            NativeEffects.SetCircleRegion(_hwnd, _sizePx, 0);
        }
        _blur = blur;
        _wheel.Backdrop = blur ? WheelBackdrop.Translucent : WheelBackdrop.Solid;
        UpdateRegion();
    }

    private void OnThemeChanged()
    {
        if (IsVisible) ApplyTheme();
    }

    private void SetAppearance(double scale, double opacity)
    {
        _scale.ScaleX = _scale.ScaleY = scale;
        _wheel.Opacity = opacity;
        UpdateRegion();
    }

    /// <summary>Круглая форма окна по текущему размеру диска — только при размытии (оно рисуется на всё окно).</summary>
    private void UpdateRegion()
    {
        if (!_blur || _sizePx <= 0) return;
        var diameter = (int)Math.Round(2 * WheelControl.DiscRadius * _dpi / 96.0 * _scale.ScaleX);
        if (!NativeEffects.SetCircleRegion(_hwnd, _sizePx, Math.Max(2, diameter)))
        {
            // Форма не встала — без неё размытие было бы квадратным. Остаёмся на плотном диске.
            NativeEffects.SetAcrylic(_hwnd, false);
            _blur = false;
            _wheel.Backdrop = WheelBackdrop.Solid;
        }
    }

    /// <summary>Закрыть с анимацией; accepted — номер принятого сектора (сначала вспышка).</summary>
    private void CloseAnimated(int? accepted)
    {
        _open = false;
        _closing = true;
        _pressed = false;
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
        _tween.Run(CloseMs, Motion.EaseInQuad, t => SetAppearance(1 - 0.06 * t, 1 - t), FinishClose);

    /// <summary>Если колесо закрывается — спрятать сразу и сбросить (перед новым показом).</summary>
    private void FinishClose()
    {
        if (!_closing) return;
        _closing = false;
        _tween.Stop(complete: false);
        _hiding = true;
        try { Hide(); }
        finally { _hiding = false; }
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
        if (size != _sizePx || dpi != _dpi)
        {
            _sizePx = size;
            _dpi = dpi;
            UpdateRegion();
        }
    }
}
