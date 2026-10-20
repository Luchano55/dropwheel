using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DropWheel.Wheel;

/// <summary>
/// Прозрачное окно поверх всех с колесом. Одно на приложение, переиспользуется (Show/Hide).
/// Сценарий перетаскивания: ShowAt(x, y) → DragEnter → FilesEntered → хост вызывает SetItems → Drop → FilesDropped.
/// Сценарий меню проводника: SetItems → CloseOnDeactivate = true → ShowAt(x, y) → клик → ItemChosen.
/// После скрытия колесо сбрасывается в «ждём файлы» (без секторов и без «Нечего предложить»).
/// Прятать колесо снаружи — через Cancel(), а не Hide(): так сбрасывается состояние.
/// </summary>
public sealed class WheelWindow : Window
{
    /// <summary>Сторона окна в DIP.</summary>
    public const double DipSize = 300;

    private readonly WheelControl _wheel = new();
    private readonly WheelState _state = new();

    private string[]? _files;          // пути текущего перетаскивания
    private Point? _lastPoint;         // последняя позиция курсора над колесом
    private (int X, int Y)? _anchor;   // куда просили поставить центр (физические пиксели)
    private bool _pressed;
    private int? _pressIndex;
    private bool _pressCenter;
    private bool _hiding;              // Hide() сам вызывает Deactivated — не считать это отменой

    /// <summary>Выбран сектор кликом: id операции. Окно уже скрыто.</summary>
    public event Action<string>? ItemChosen;
    /// <summary>Esc, клик/drop в центр, drop мимо секторов или на «Нечего предложить». Окно уже скрыто.</summary>
    public event Action? Canceled;
    /// <summary>
    /// Над колесом появились файлы (DragEnter с FileDrop). Хост считает сектора и тут же вызывает SetItems.
    /// Вызывается внутри OLE-цикла перетаскивания — обработчик должен быть быстрым.
    /// </summary>
    public event Action<string[]>? FilesEntered;
    /// <summary>Файлы брошены на сектор: (пути, id операции). Окно уже скрыто; обработчик должен быть быстрым.</summary>
    public event Action<string[], string>? FilesDropped;

    /// <summary>Прятать колесо при потере фокуса (режим клика из меню проводника).</summary>
    public bool CloseOnDeactivate { get; set; }

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

        _wheel.HorizontalAlignment = HorizontalAlignment.Center;
        _wheel.VerticalAlignment = VerticalAlignment.Center;
        _wheel.IsPending = true;
        Content = _wheel;

        SourceInitialized += (_, _) => WheelNative.MakeToolWindow(new WindowInteropHelper(this).Handle);
        // При переезде на монитор с другим масштабом WPF сам меняет размер окна — потом ставим своё место ещё раз.
        DpiChanged += (_, _) =>
        {
            if (_anchor is { } a && IsVisible) Dispatcher.InvokeAsync(() => Place(a.X, a.Y));
        };
    }

    /// <summary>
    /// Показать колесо с центром в точке экрана (физические пиксели — из хуков или GetCursorPos).
    /// Активирует окно, только если CloseOnDeactivate (режим клика); при перетаскивании фокус не забираем.
    /// </summary>
    public void ShowAt(int screenX, int screenY) => ShowAt(screenX, screenY, CloseOnDeactivate);

    /// <summary>
    /// Показать колесо с центром в точке экрана (физические пиксели).
    /// activate = false — для перетаскивания (цикл OLE принадлежит источнику, окно не активируем);
    /// activate = true — для клика, чтобы работал Esc.
    /// </summary>
    public void ShowAt(int screenX, int screenY, bool activate)
    {
        _anchor = (screenX, screenY);
        ShowActivated = activate;
        UpdateHover(null);

        // Дважды: первый SetWindowPos может перенести окно на монитор с другим DPI,
        // и WPF пересчитает размер; второй ставит точные координаты и размер.
        Place(screenX, screenY);
        Place(screenX, screenY);

        if (!IsVisible)
        {
            Show();
            _wheel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(100)));
        }
        Place(screenX, screenY);

        if (activate)
        {
            Activate();
            Focus();
        }
    }

    /// <summary>Наборы секторов; режим — «Конвертация» (или «Инструменты», если конвертировать нечего).</summary>
    public void SetItems(IReadOnlyList<WheelItem> convertItems, IReadOnlyList<WheelItem> toolItems)
    {
        _state.SetItems(convertItems, toolItems);
        _wheel.IsPending = false;
        ApplyState();
    }

    /// <summary>Конвертация ↔ Инструменты. Вызывает хост по повторному нажатию горячей клавиши.</summary>
    public void ToggleMode()
    {
        _state.Toggle();
        ApplyState();
    }

    /// <summary>Спрятать колесо с событием Canceled (например, кнопку отпустили, а DragEnter так и не пришёл).</summary>
    public void Cancel()
    {
        if (!IsVisible) return;
        HideWheel();
        Canceled?.Invoke();
    }

    // ---- мышь и клавиатура (режим клика) ----

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateHover(e.GetPosition(_wheel));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        UpdateHover(null);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var p = e.GetPosition(_wheel);
        if (!_wheel.IsOnDisc(p)) return;
        _pressed = true;
        _pressIndex = _wheel.HitTest(p);
        _pressCenter = _wheel.IsCenter(p);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        // Отпускание без нажатия над колесом (хвост клика из меню проводника) — не считается.
        if (!_pressed) return;
        _pressed = false;
        e.Handled = true;

        var p = e.GetPosition(_wheel);
        if (_state.IsEmpty)
        {
            // «Нечего предложить» — клик в любое место = отмена; пока ждём файлы — только центр.
            if (_wheel.IsPending ? _wheel.IsCenter(p) : _wheel.IsOnDisc(p)) Cancel();
            return;
        }

        var index = _wheel.HitTest(p);
        if (index is not null && index == _pressIndex && _state.At(index) is { } item)
        {
            HideWheel();
            ItemChosen?.Invoke(item.Id);
        }
        else if (_pressCenter && _wheel.IsCenter(p))
        {
            Cancel();
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Alt+F4 тоже просто прячет: окно переиспользуется и закрываться не должно.
        if (e.Key == Key.Escape || (e.Key == Key.System && e.SystemKey == Key.F4))
        {
            e.Handled = true;
            Cancel();
        }
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (CloseOnDeactivate && !_hiding) Cancel();
    }

    // ---- перетаскивание ----

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
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
        e.Handled = true;
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        UpdateHover(e.GetPosition(_wheel));
        e.Effects = EffectFor(e);
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        // Не прячемся: пользователь может вернуться к колесу.
        UpdateHover(null);
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        var item = _state.At(_wheel.HitTest(e.GetPosition(_wheel)));
        var files = _files;
        var ok = item is not null && files is { Length: > 0 } && e.AllowedEffects.HasFlag(DragDropEffects.Copy);
        // Только Copy: Move заставил бы источник удалить исходник.
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;

        HideWheel();
        if (ok) FilesDropped?.Invoke(files!, item!.Id);
        else Canceled?.Invoke();
    }

    // ---- внутреннее ----

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
        UpdateHover(_lastPoint);
    }

    private void UpdateHover(Point? p)
    {
        _lastPoint = p;
        _wheel.HoverIndex = p is { } pt ? _wheel.HitTest(pt) : null;
        _wheel.IsCenterHovered = p is { } pc && _wheel.IsCenter(pc);
    }

    private void HideWheel()
    {
        _hiding = true;
        try { Hide(); }
        finally { _hiding = false; }
        Reset();
    }

    /// <summary>После скрытия: ждём новых файлов, ничего не подсвечено.</summary>
    private void Reset()
    {
        _files = null;
        _pressed = false;
        _anchor = null;
        _state.SetItems([], []);
        _wheel.IsPending = true;
        ApplyState();
        UpdateHover(null);
    }

    /// <summary>Поставить окно центром в точку: размер — DIP × DPI монитора под точкой, не вылезая за рабочую область.</summary>
    private void Place(int x, int y)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var (work, dpi) = WheelNative.MonitorAt(x, y);
        var size = WheelGeometry.ToPixels(DipSize, dpi);
        var (left, top) = WheelGeometry.PlaceInside(x, y, size, work.Left, work.Top, work.Right, work.Bottom);
        WheelNative.SetWindowPos(hwnd, WheelNative.HwndTopmost, left, top, size, size,
            WheelNative.SwpNoActivate | WheelNative.SwpNoOwnerZOrder);
    }
}
