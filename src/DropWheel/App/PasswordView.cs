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
/// Содержимое окна «Защитить PDF паролем» / «Снять пароль с PDF» (D-PDFPASS): имя файла, пояснение,
/// поле пароля (у «Защитить» — ещё повтор и «Пароли не совпадают»), «Показать пароль», кнопки.
/// «Защитить» доступна, только когда пароль не пустой и повтор совпадает (<see cref="PdfPassword.CanProtect"/>).
/// Пароль живёт только в полях окна; наружу — один раз, событием <see cref="Confirmed"/>.
/// </summary>
public sealed class PasswordView : Border
{
    public const double DefaultWidth = 440;

    private SettingsPalette _palette;
    private readonly string _fileName;
    private readonly bool _protect;
    private string _password = "", _repeat = "";
    private bool _reveal;

    private SecretField _first = null!;
    private SecretField? _second;
    private FrameworkElement _mismatch = null!;
    private TrimView.PrimaryButton _ok = null!;

    public PasswordView(SettingsPalette palette, string fileName, bool protect)
    {
        _palette = palette;
        _fileName = fileName;
        _protect = protect;
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

    public bool Protect => _protect;

    /// <summary>Заголовок окна.</summary>
    public string Title => _protect ? Loc.T.PdfProtectTitle : Loc.T.PdfUnlockTitle;

    /// <summary>Пароль виден буквами («Показать пароль»).</summary>
    public bool Reveal
    {
        get => _reveal;
        set
        {
            _reveal = value;
            Build();
        }
    }

    public bool CanConfirm => _protect ? PdfPassword.CanProtect(_password, _repeat) : PdfPassword.IsValid(_password);

    /// <summary>«Пароли не совпадают» сейчас показано.</summary>
    public bool ShowsMismatch => _protect && PdfPassword.ShowMismatch(_password, _repeat);

    /// <summary>Заполнить поля (скриншот-тесты, проверка кнопки).</summary>
    public void SetPasswords(string password, string? repeat = null)
    {
        _password = password;
        _repeat = repeat ?? "";
        Build();
    }

    /// <summary>Нажата «Защитить» / «Снять пароль» (или Enter): пароль для задачи.</summary>
    public event Action<string>? Confirmed;
    public event Action? CancelRequested;

    public void RequestConfirm()
    {
        if (!CanConfirm) return;
        var password = _password;
        Clear();
        Confirmed?.Invoke(password);
    }

    public void RequestCancel()
    {
        Clear();
        CancelRequested?.Invoke();
    }

    /// <summary>Фокус — в первое поле (окно открылось).</summary>
    public void FocusPassword() => _first.FocusInput();

    /// <summary>Забыть пароль: поля пустые (окно закрывается).</summary>
    private void Clear()
    {
        _password = _repeat = "";
        _first.Clear();
        _second?.Clear();
    }

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
        _ok = new TrimView.PrimaryButton(p, _protect ? Loc.T.PdfProtectButton : Loc.T.PdfUnlockButton) { MinWidth = 120 };
        _ok.Click += RequestConfirm;
        var cancel = new SettingsButton(p, Loc.T.Cancel) { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += RequestCancel;
        buttons.Children.Add(_ok);
        buttons.Children.Add(cancel);
        footer.Child = buttons;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel { Margin = new Thickness(24, 18, 24, 20) };

        // Заголовок: замок, имя файла.
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var icon = new TextBlock
        {
            Text = _protect ? Glyphs.Lock : Glyphs.Unlock, FontFamily = UiFonts.Icons, FontSize = 16, Foreground = p.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0),
        };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        header.Children.Add(new TextBlock
        {
            Text = _fileName, FontWeight = FontWeights.SemiBold, FontSize = 15, Foreground = p.Text,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = _fileName,
        });
        body.Children.Add(header);
        body.Children.Add(new TextBlock
        {
            Text = _protect ? Loc.T.PdfProtectIntro : Loc.T.PdfUnlockIntro, FontSize = 12, Foreground = p.TextSecondary,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        });

        body.Children.Add(Label(Loc.T.PdfPasswordLabel));
        _first = new SecretField(p, Loc.T.PdfPasswordLabel, _password, _reveal);
        _first.Changed += text =>
        {
            _password = text;
            UpdateState();
        };
        body.Children.Add(_first);

        _second = null;
        if (_protect)
        {
            var repeatLabel = Label(Loc.T.PdfPasswordRepeat);
            repeatLabel.Margin = new Thickness(0, 12, 0, 6);
            body.Children.Add(repeatLabel);
            _second = new SecretField(p, Loc.T.PdfPasswordRepeat, _repeat, _reveal);
            _second.Changed += text =>
            {
                _repeat = text;
                UpdateState();
            };
            body.Children.Add(_second);
        }

        // «Пароли не совпадают» — красным, со значком (как подсказки ошибок Windows 11).
        var error = SettingsPalette.B(p.IsLight ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0xFF, 0x99, 0xA4));
        var mismatch = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        mismatch.Children.Add(new TextBlock
        {
            Text = Glyphs.Error, FontFamily = UiFonts.Icons, FontSize = 12, Foreground = error,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 6, 0),
        });
        mismatch.Children.Add(new TextBlock { Text = Loc.T.PdfPasswordMismatch, FontSize = 12, Foreground = error });
        _mismatch = mismatch;
        body.Children.Add(mismatch);

        // «Показать пароль» — флажок с подписью (щелчок по подписи тоже переключает).
        var show = new CheckMark(p, _reveal, Loc.T.PdfPasswordShow) { Margin = new Thickness(0, 0, 10, 0) };
        show.Changed += on => ToggleReveal(on);
        var showText = new TextBlock
        {
            Text = Loc.T.PdfPasswordShow, FontSize = 14, Foreground = p.Text, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
        };
        showText.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            ToggleReveal(!_reveal);
        };
        var showRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        showRow.Children.Add(show);
        showRow.Children.Add(showText);
        body.Children.Add(showRow);

        root.Children.Add(body);
        Child = root;
        UpdateState();
    }

    /// <summary>«Показать пароль»: поля пересоздаются — курсор возвращается в первое поле.</summary>
    private void ToggleReveal(bool on)
    {
        Reveal = on;
        Dispatcher.BeginInvoke(FocusPassword, System.Windows.Threading.DispatcherPriority.Input);
    }

    private TextBlock Label(string text) =>
        new() { Text = text, FontSize = 13, Foreground = _palette.Text, Margin = new Thickness(0, 0, 0, 6) };

    private void UpdateState()
    {
        _ok.SetEnabled(CanConfirm);
        _mismatch.Visibility = ShowsMismatch ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Поле пароля в стиле Windows 11: скруглённая рамка, снизу — акцент, пока в поле фокус.
    /// Скрыто — PasswordBox (точки), «Показать пароль» — обычный TextBox с тем же текстом.
    /// </summary>
    private sealed class SecretField : Border
    {
        private readonly SettingsPalette _palette;
        private readonly PasswordBox? _hidden;
        private readonly TextBox? _visible;

        public SecretField(SettingsPalette palette, string automationName, string text, bool reveal)
        {
            _palette = palette;
            CornerRadius = new CornerRadius(4);
            Background = palette.Control;
            MinHeight = 34;
            Control input;
            if (reveal)
            {
                _visible = new TextBox { Text = text, AcceptsReturn = false, AcceptsTab = false };
                _visible.TextChanged += (_, _) => Changed?.Invoke(_visible.Text);
                input = _visible;
            }
            else
            {
                _hidden = new PasswordBox { Password = text };
                _hidden.PasswordChanged += (_, _) => Changed?.Invoke(_hidden.Password);
                input = _hidden;
            }
            input.Background = Brushes.Transparent;
            input.BorderThickness = new Thickness(0);
            input.Foreground = palette.Text;
            input.FontSize = 14;
            input.Padding = new Thickness(6, 5, 6, 6);
            input.VerticalContentAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(input, automationName);
            if (_visible is not null)
            {
                _visible.CaretBrush = palette.Text;
                _visible.SelectionBrush = palette.Accent;
            }
            else
            {
                _hidden!.CaretBrush = palette.Text;
                _hidden.SelectionBrush = palette.Accent;
            }
            Child = input;
            IsKeyboardFocusWithinChanged += (_, _) => UpdateLook();
            Loaded += (_, _) =>
            {
                // После «Показать пароль» поле пересоздано — курсор в конец текста.
                if (_visible is not null) _visible.CaretIndex = _visible.Text.Length;
            };
            UpdateLook();
        }

        public event Action<string>? Changed;

        public void FocusInput()
        {
            if (_visible is not null)
            {
                _visible.Focus();
                Keyboard.Focus(_visible);
            }
            else if (_hidden is not null)
            {
                _hidden.Focus();
                Keyboard.Focus(_hidden);
            }
        }

        public void Clear()
        {
            _hidden?.Clear();
            _visible?.Clear();
        }

        private void UpdateLook()
        {
            var focused = IsKeyboardFocusWithin;
            BorderBrush = focused ? _palette.Accent : _palette.ControlBorder;
            BorderThickness = focused ? new Thickness(1, 1, 1, 2) : new Thickness(1);
            Padding = focused ? new Thickness(0) : new Thickness(0, 0, 0, 1);
        }
    }
}
