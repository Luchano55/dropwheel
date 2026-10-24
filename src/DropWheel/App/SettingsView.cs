using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DropWheel.Conversions;
using DropWheel.Input;
using DropWheel.Localization;
using DropWheel.Settings;
using DropWheel.Shell;
using DropWheel.Ui;
using DropWheel.Updates;
using DropWheel.Wheel;
using S = DropWheel.Ui.Strings.Settings;

namespace DropWheel.App;

public enum SettingsPage { General, Quality, Wheel }

/// <summary>
/// Содержимое окна настроек (раздел 10 ТЗ) в духе «Параметров» Windows 11: слева разделы, справа карточки.
/// Изменения применяются сразу — через <see cref="Update"/> (приложение применяет и сохраняет их);
/// кнопок «ОК/Отмена» нет. Без ссылок на приложение — скриншот-тесты собирают вид сами.
/// </summary>
public sealed class SettingsView : Border
{
    public const double DefaultWidth = 920;
    public const double DefaultHeight = 680;
    private const double NavWidth = 236;
    private const double PreviewSize = 232;

    private SettingsPalette _palette;
    private AccentColors _accent;
    private AppSettings _settings;
    private SettingsPage _page = SettingsPage.General;
    private string _group = WheelGroups.Photo;
    private OperationMode _mode = OperationMode.Convert;
    private string _sharedFolder;
    private string _clipboardFolder;
    private readonly int _build;
    private readonly string? _version;
    private readonly UpdateChannel _embeddedChannel;

    private ScrollViewer _scroll = null!;
    private StackPanel _nav = null!;
    private StackPanel _wheelList = null!;
    private Border _wheelPreviewHost = null!;
    private WheelControl? _preview;

    /// <summary>Значение пилюли «Своя…» горячей клавиши.</summary>
    private static readonly object CustomHotkey = new();
    private Action? _startHotkeyCapture;
    /// <summary>Не null, пока ждём нажатия своей горячей клавиши: код VK → принята ли.</summary>
    private Func<int, bool>? _hotkeyKey;

    /// <param name="build">Номер сборки для подписи внизу (0 — сборка разработчика).</param>
    /// <param name="version">Версия для людей (0.3.0, 0.3.0-dev.57); null — показать только номер сборки.</param>
    /// <param name="embeddedChannel">Канал обновлений, зашитый при сборке, — для подписи «Как при установке».</param>
    public SettingsView(SettingsPalette palette, AppSettings settings, AccentColors? accent = null, int build = 0,
        string? version = null, UpdateChannel embeddedChannel = UpdateChannel.Dev)
    {
        _palette = palette;
        _accent = accent ?? AccentColors.Default;
        _settings = settings;
        _build = build;
        _version = version;
        _embeddedChannel = embeddedChannel;
        _sharedFolder = settings.Conversion.OutputFolder ?? ConversionSettings.DefaultSharedFolder;
        _clipboardFolder = settings.ClipboardFolder ?? ClipboardPlan.DefaultImageFolder;
        Update = change => _settings = change(_settings);
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(this, UiFonts.Text);
        System.Windows.Documents.TextElement.SetFontSize(this, 14);
        PreviewKeyDown += OnPreviewKeyDown;
        Build();
    }

    /// <summary>Ждём свою горячую клавишу: нажатие забираем себе, в элементы окна оно не идёт.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_hotkeyKey is null) return;
        e.Handled = true;
        if (e.IsRepeat) return;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        _hotkeyKey(KeyInterop.VirtualKeyFromKey(key));
    }

    /// <summary>Нажать «Своя…» (для тестов).</summary>
    internal void BeginHotkeyCapture() => _startHotkeyCapture?.Invoke();

    /// <summary>Нажата клавиша в режиме «Нажмите клавишу…» (для тестов); true — принята.</summary>
    internal bool CaptureHotkey(int vk) => _hotkeyKey?.Invoke(vk) ?? false;

    /// <summary>
    /// Применить правку к настройкам и вернуть новые. Приложение подставляет свою функцию
    /// (применяет на ходу и сохраняет в settings.json); по умолчанию — просто хранит у себя.
    /// </summary>
    public Func<Func<AppSettings, AppSettings>, AppSettings> Update { get; set; }

    /// <summary>Выбор папки: (текущая, заголовок окна) → выбранная (null — отказались).</summary>
    public Func<string, string, string?>? PickFolder { get; set; }

    public event Action? OpenLogsRequested;
    public event Action? CheckUpdatesRequested;

    /// <summary>Настройки, которые показывает окно. Установка снаружи (трей, «последний выбор») перестраивает вид.</summary>
    public AppSettings Settings
    {
        get => _settings;
        set
        {
            if (Equals(value, _settings)) return;
            _settings = value;
            if (value.Conversion.OutputFolder is { } folder) _sharedFolder = folder;
            if (value.ClipboardFolder is { } clipFolder) _clipboardFolder = clipFolder;
            ShowPage();
        }
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

    public AccentColors Accent
    {
        get => _accent;
        set
        {
            _accent = value;
            Build();
        }
    }

    public SettingsPage Page
    {
        get => _page;
        set
        {
            if (_page == value) return;
            _page = value;
            BuildNav();
            ShowPage();
        }
    }

    /// <summary>Тип файлов, открытый на странице «Колесо».</summary>
    public string WheelGroup
    {
        get => _group;
        set
        {
            _group = WheelGroups.All.Contains(value) ? value : WheelGroups.Photo;
            if (_page == SettingsPage.Wheel) ShowPage();
        }
    }

    public OperationMode WheelMode
    {
        get => _mode;
        set
        {
            _mode = value;
            if (_page == SettingsPage.Wheel) ShowPage();
        }
    }

    private void Change(Func<AppSettings, AppSettings> change) => _settings = Update(change);

    /// <summary>
    /// Сменился язык интерфейса (Loc.Current) — перестроить всё окно на новом языке. Отложено: смена приходит
    /// из обработчика переключателя, который сейчас будет заменён.
    /// </summary>
    public void Relocalize() => Dispatcher.BeginInvoke(Build);

    // ---------- каркас ----------

    private void Build()
    {
        Background = _palette.Background;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NavWidth) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new DockPanel { Margin = new Thickness(12, 14, 8, 14), LastChildFill = true };
        var footer = BuildFooter();
        DockPanel.SetDock(footer, Dock.Bottom);
        left.Children.Add(footer);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 18) };
        header.Children.Add(new LogoMark { Size = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        header.Children.Add(new TextBlock
        {
            Text = "DropWheel", FontFamily = UiFonts.Display, FontWeight = FontWeights.SemiBold, FontSize = 18,
            Foreground = _palette.Text, VerticalAlignment = VerticalAlignment.Center,
        });
        DockPanel.SetDock(header, Dock.Top);
        left.Children.Add(header);
        _nav = new StackPanel();
        left.Children.Add(_nav);
        root.Children.Add(left);

        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            PanningMode = PanningMode.VerticalOnly,
            Focusable = false,
        };
        Grid.SetColumn(_scroll, 1);
        root.Children.Add(_scroll);
        Child = root;

        BuildNav();
        ShowPage();
    }

    private void BuildNav()
    {
        if (_nav is null) return;
        _nav.Children.Clear();
        AddNav(SettingsPage.General, Glyphs.Settings, S.PageGeneral);
        AddNav(SettingsPage.Quality, Glyphs.Quality, S.PageQuality);
        AddNav(SettingsPage.Wheel, Glyphs.WheelPage, S.PageWheel);
    }

    private void AddNav(SettingsPage page, string glyph, string text)
    {
        var item = new NavItem(_palette, glyph, text, page == _page);
        item.Click += () => Page = page;
        _nav.Children.Add(item);
    }

    private FrameworkElement BuildFooter()
    {
        var p = _palette;
        var panel = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        panel.Children.Add(new TextBlock { Text = S.Version(_build, _version), FontSize = 12, Foreground = p.TextSecondary, Margin = new Thickness(2, 0, 0, 4) });
        var check = new SettingsLink(p, S.CheckUpdates) { HorizontalAlignment = HorizontalAlignment.Left };
        check.Click += () => CheckUpdatesRequested?.Invoke();
        panel.Children.Add(check);
        var logs = new SettingsLink(p, S.OpenLogs) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 0) };
        logs.Click += () => OpenLogsRequested?.Invoke();
        panel.Children.Add(logs);
        return panel;
    }

    private void ShowPage()
    {
        if (_scroll is null) return;
        var page = new StackPanel { Margin = new Thickness(28, 18, 32, 28), MaxWidth = 860 };
        page.Children.Add(new TextBlock
        {
            Text = _page switch { SettingsPage.Quality => S.PageQuality, SettingsPage.Wheel => S.PageWheel, _ => S.PageGeneral },
            FontFamily = UiFonts.Display, FontWeight = FontWeights.SemiBold, FontSize = 28,
            Foreground = _palette.Text, Margin = new Thickness(0, 0, 0, 18),
        });
        _hotkeyKey = null;
        _startHotkeyCapture = null;
        switch (_page)
        {
            case SettingsPage.Quality: BuildQuality(page); break;
            case SettingsPage.Wheel: BuildWheel(page); break;
            default: BuildGeneral(page); break;
        }
        _scroll.Content = page;
    }

    // ---------- общие кирпичики ----------

    private Border Card(UIElement content, Thickness? margin = null) => new()
    {
        Background = _palette.Card,
        BorderBrush = _palette.CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(18, 14, 18, 14),
        Margin = margin ?? new Thickness(0, 0, 0, 4),
        Child = content,
    };

    private TextBlock SectionTitle(string text) => new()
    {
        Text = text, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = _palette.Text,
        Margin = new Thickness(2, 18, 0, 8),
    };

    private StackPanel Titles(string title, string? hint)
    {
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 14, Foreground = _palette.Text, TextWrapping = TextWrapping.Wrap });
        if (hint is not null)
            panel.Children.Add(new TextBlock
            {
                Text = hint, FontSize = 12, Foreground = _palette.TextSecondary, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        return panel;
    }

    /// <summary>Строка: заголовок с пояснением слева, элемент управления справа.</summary>
    private Grid Row(string title, string? hint, FrameworkElement control)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = Titles(title, hint);
        titles.Margin = new Thickness(0, 0, 16, 0);
        grid.Children.Add(titles);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private Border ToggleCard(string title, string hint, bool isOn, Action<bool> changed, Thickness? margin = null)
    {
        var toggle = new ToggleSwitch(_palette, isOn, title);
        toggle.Toggled += changed;
        var row = Row(title, hint, toggle);
        // Щелчок по всей строке — как в «Параметрах» Windows.
        row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && IsInside(d, toggle)) return;
            toggle.IsOn = !toggle.IsOn;
            changed(toggle.IsOn);
        };
        return Card(row, margin);
    }

    private static bool IsInside(DependencyObject d, DependencyObject ancestor)
    {
        for (var x = d; x is not null; x = x is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (ReferenceEquals(x, ancestor)) return true;
        return false;
    }

    // ---------- Основное ----------

    private void BuildGeneral(StackPanel page)
    {
        var p = _palette;

        // Горячая клавиша
        // Готовые клавиши + «Своя…» (D-HOTKEY): щелчок → «Нажмите клавишу…», следующее нажатие в окне — новая клавиша.
        static string Valid(string? name) => Hotkeys.ToVirtualKey(name) is null ? Hotkeys.Default : name!;
        static string? Preset(string name) =>
            Hotkeys.Names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        var current = Valid(_settings.Hotkey);
        var hotkeys = new ChoicePills(p, Hotkeys.Names.Select(n => ((object)n, Hotkeys.DisplayName(n)))
                .Append((CustomHotkey, Preset(current) is null ? Hotkeys.DisplayName(current) : S.HotkeyCustom)),
            (object?)Preset(current) ?? CustomHotkey, S.HotkeyTitle) { Margin = new Thickness(0, 12, 0, -6) };
        var hotkeyNote = new TextBlock
        {
            FontSize = 12, Foreground = p.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };
        void Note(string? text)
        {
            hotkeyNote.Text = text ?? "";
            hotkeyNote.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
        }
        // Буква или цифра: работает, но во время перетаскивания не печатается — предупредить.
        void Show(string name)
        {
            _hotkeyKey = null;
            var preset = Preset(name);
            hotkeys.SetLabel(CustomHotkey, preset is null ? Hotkeys.DisplayName(name) : S.HotkeyCustom);
            hotkeys.Select((object?)preset ?? CustomHotkey, raise: false);
            Note(Hotkeys.ToVirtualKey(name) is { } vk && Hotkeys.Types(vk) ? S.HotkeyTypes(Hotkeys.DisplayName(name)) : null);
        }
        _startHotkeyCapture = () =>
        {
            hotkeys.Select(CustomHotkey, raise: false);
            hotkeys.SetLabel(CustomHotkey, S.HotkeyPress);
            Note(null);
            hotkeys.FocusPill(CustomHotkey);
            _hotkeyKey = vk =>
            {
                if (vk == Hotkeys.Escape)
                {
                    Show(Valid(_settings.Hotkey));
                    return false;
                }
                switch (Hotkeys.Check(vk))
                {
                    case HotkeyCheck.Modifier: Note(S.HotkeyModifier); return false;
                    case HotkeyCheck.Reserved: Note(S.HotkeyReserved(Hotkeys.KeyName(vk))); return false;
                }
                var name = Hotkeys.FromVirtualKey(vk)!;
                Change(s => s with { Hotkey = name });
                Show(name);
                return true;
            };
        };
        hotkeys.SelectionChanged += v =>
        {
            if (ReferenceEquals(v, CustomHotkey))
            {
                _startHotkeyCapture();
                return;
            }
            Change(s => s with { Hotkey = (string)v });
            Show((string)v);
        };
        Show(current);
        var hotkeyPanel = new StackPanel();
        hotkeyPanel.Children.Add(Titles(S.HotkeyTitle, S.HotkeyHint));
        hotkeyPanel.Children.Add(hotkeys);
        hotkeyPanel.Children.Add(hotkeyNote);
        page.Children.Add(Card(hotkeyPanel));

        // Куда класть результат
        var shared = _settings.Conversion.OutputFolder is not null;
        var where = new ChoicePills(p, [(false, S.OutputBeside), (true, S.OutputShared)], shared, S.OutputTitle)
            { Margin = new Thickness(0, 12, 0, 0) };
        var folderText = new TextBlock
        {
            Text = _sharedFolder, FontSize = 12.5, Foreground = p.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0), ToolTip = _sharedFolder,
        };
        var browse = new SettingsButton(p, S.OutputBrowse, Glyphs.FolderOpen);
        var folderRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.Children.Add(folderText);
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        void ShowFolderRow(bool on)
        {
            folderRow.IsEnabled = on;
            folderRow.Opacity = on ? 1 : 0.45;
        }
        ShowFolderRow(shared);
        where.SelectionChanged += v =>
        {
            var on = (bool)v;
            ShowFolderRow(on);
            Change(s => s with { Conversion = s.Conversion with { OutputFolder = on ? _sharedFolder : null } });
        };
        browse.Click += () =>
        {
            if (PickFolder?.Invoke(_sharedFolder, S.OutputBrowseTitle) is not { Length: > 0 } chosen) return;
            _sharedFolder = chosen;
            folderText.Text = chosen;
            folderText.ToolTip = chosen;
            Change(s => s with { Conversion = s.Conversion with { OutputFolder = chosen } });
        };
        var outputPanel = new StackPanel();
        outputPanel.Children.Add(Titles(S.OutputTitle, null));
        outputPanel.Children.Add(where);
        outputPanel.Children.Add(folderRow);
        page.Children.Add(Card(outputPanel));
        // Готовый результат — сразу в буфер обмена (D-COPYRESULT).
        page.Children.Add(ToggleCard(S.CopyResult, S.CopyResultHint, _settings.CopyResultToClipboard,
            on => Change(s => s with { CopyResultToClipboard = on })));

        // Язык интерфейса: «Как в Windows» / «Русский» / «English» — применяется сразу (приложение перестраивает окна).
        var languages = new ChoicePills(p,
            [(Loc.Auto, S.LanguageAuto), (Loc.Russian, Loc.NativeName(UiLanguage.Ru)), (Loc.English, Loc.NativeName(UiLanguage.En))],
            Loc.Normalize(_settings.Language), S.LanguageTitle) { Margin = new Thickness(0, 12, 0, -6) };
        languages.SelectionChanged += v => Change(s => s with { Language = (string)v });
        var languagePanel = new StackPanel();
        languagePanel.Children.Add(Titles(S.LanguageTitle, S.LanguageHint));
        languagePanel.Children.Add(languages);
        page.Children.Add(Card(languagePanel));

        page.Children.Add(WheelOpacityCard());

        // Windows
        page.Children.Add(SectionTitle(S.SystemTitle));
        page.Children.Add(ToggleCard(S.AutoStart, S.AutoStartHint, _settings.AutoStart,
            on => Change(s => s with { AutoStart = on })));
        page.Children.Add(ToggleCard(S.ContextMenu, S.ContextMenuHint, _settings.ContextMenuEnabled,
            on => Change(s => s with { ContextMenuEnabled = on })));
        page.Children.Add(ToggleCard(S.ClipboardHotkey, S.ClipboardHotkeyHint, _settings.ClipboardHotkey,
            on => Change(s => s with { ClipboardHotkey = on })));
        page.Children.Add(ClipboardImageCard());
        page.Children.Add(ToggleCard(S.KeepClipboardImage, S.KeepClipboardImageHint, _settings.KeepClipboardImage,
            on => Change(s => s with { KeepClipboardImage = on })));
        page.Children.Add(ToggleCard(S.Updates, S.UpdatesHint, _settings.CheckForUpdates,
            on => Change(s => s with { CheckForUpdates = on })));

        // Канал обновлений (D-RELEASE): «Как при установке» / «Стабильный» / «Тестовый».
        var channels = new ChoicePills(p,
            [(UpdateChannels.Auto, S.UpdateChannelAuto(_embeddedChannel)), (UpdateChannels.Stable, S.UpdateChannelStable),
             (UpdateChannels.Dev, S.UpdateChannelDev)],
            UpdateChannels.Normalize(_settings.UpdateChannel), S.UpdateChannelTitle) { Margin = new Thickness(0, 12, 0, -6) };
        channels.SelectionChanged += v => Change(s => s with { UpdateChannel = (string)v });
        var channelPanel = new StackPanel();
        channelPanel.Children.Add(Titles(S.UpdateChannelTitle, S.UpdateChannelHint));
        channelPanel.Children.Add(channels);
        page.Children.Add(Card(channelPanel));
    }

    /// <summary>
    /// «Картинки из буфера обмена»: «По умолчанию» (как «Куда класть результат» или «Изображения\DropWheel»)
    /// или своя папка с кнопкой выбора. Под «По умолчанию» серым показан путь, куда сейчас ляжет картинка.
    /// </summary>
    private Border ClipboardImageCard()
    {
        var p = _palette;
        var own = _settings.ClipboardFolder is not null;
        var choice = new ChoicePills(p, [(false, S.ClipboardFolderDefault), (true, S.ClipboardFolderOwn)], own, S.ClipboardImageTitle)
            { Margin = new Thickness(0, 12, 0, 0) };
        var folderText = new TextBlock
        {
            FontSize = 12.5, Foreground = p.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        var browse = new SettingsButton(p, S.OutputBrowse, Glyphs.FolderOpen);
        var folderRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderRow.Children.Add(folderText);
        Grid.SetColumn(browse, 1);
        folderRow.Children.Add(browse);
        void ShowFolder(bool on)
        {
            var folder = on ? _clipboardFolder : ClipboardPlan.ImageFolder(null, _settings.Conversion);
            folderText.Text = folder;
            folderText.ToolTip = folder;
            browse.IsEnabled = on;
            browse.Opacity = on ? 1 : 0.45;
        }
        ShowFolder(own);
        choice.SelectionChanged += v =>
        {
            var on = (bool)v;
            Change(s => s with { ClipboardFolder = on ? _clipboardFolder : null });
            ShowFolder(on);
        };
        browse.Click += () =>
        {
            if (PickFolder?.Invoke(_clipboardFolder, S.ClipboardFolderBrowseTitle) is not { Length: > 0 } chosen) return;
            _clipboardFolder = chosen;
            Change(s => s with { ClipboardFolder = chosen });
            ShowFolder(true);
        };
        var panel = new StackPanel();
        panel.Children.Add(Titles(S.ClipboardImageTitle, S.ClipboardImageHint));
        panel.Children.Add(choice);
        panel.Children.Add(folderRow);
        return Card(panel);
    }

    /// <summary>
    /// «Прозрачность колеса»: ползунок «плотнее ↔ прозрачнее» (вправо — прозрачнее; хранится плотность 40–100 %)
    /// и мини-колесо на цветной «подложке», чтобы было видно, как оно просвечивает. Применяется сразу.
    /// </summary>
    private Border WheelOpacityCard()
    {
        var p = _palette;
        var percent = WheelFill.Clamp(_settings.WheelOpacity);
        // Ползунок — прозрачность: 0 = плотность 100 %, 60 = плотность 40 %.
        var slider = new ValueSlider(p, 0, WheelFill.MaxPercent - WheelFill.MinPercent, WheelFill.MaxPercent - percent, S.WheelOpacityTitle)
            { Width = 220, Margin = new Thickness(8, 0, 8, 0) };
        TextBlock End(string text) => new()
        {
            Text = text, FontSize = 12, Foreground = p.TextSecondary, VerticalAlignment = VerticalAlignment.Center,
        };
        var sliderRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        sliderRow.Children.Add(End(S.WheelOpacitySolid));
        sliderRow.Children.Add(slider);
        sliderRow.Children.Add(End(S.WheelOpacityClear));

        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(Titles(S.WheelOpacityTitle, S.WheelOpacityHint));
        left.Children.Add(sliderRow);

        // Образец: колесо для фото поверх цветной полосатой «картинки» — прозрачность видна сразу.
        var ops = WheelGroups.Operations(WheelGroups.Photo);
        var (convert, tools) = WheelArrangement.Arrange(ops, WheelGroups.Photo, _settings.Wheel);
        var sample = new WheelControl
        {
            Width = WheelWindow.DipSize, Height = WheelWindow.DipSize, IsHitTestVisible = false,
            Palette = p.IsLight ? WheelPalette.Light(_accent) : WheelPalette.Dark(_accent),
            Backdrop = WheelBackdrop.Solid, IsPending = false, Mode = OperationMode.Convert,
            Items = convert, ShowModeDots = convert.Count > 0 && tools.Count > 0,
            IdleText = S.GroupName(WheelGroups.Photo), FillPercent = percent,
        };
        var host = new Border
        {
            Width = 132, Height = 132, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            Background = OpacitySampleBackground(), Padding = new Thickness(4),
            Child = new Viewbox { Child = sample, Stretch = Stretch.Uniform },
            Margin = new Thickness(16, 0, 0, 0),
        };

        slider.ValueChanged += v =>
        {
            var fill = WheelFill.MaxPercent - v;
            sample.FillPercent = fill;
            Change(s => s with { WheelOpacity = fill });
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(host, 1);
        grid.Children.Add(host);
        return Card(grid);
    }

    /// <summary>Пёстрая подложка образца: диагональные полосы — сквозь прозрачное колесо их видно.</summary>
    private static Brush OpacitySampleBackground()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0.25, 0.25), SpreadMethod = GradientSpreadMethod.Repeat,
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x80, 0xED), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2F, 0x80, 0xED), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0x99, 0x4A), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF2, 0x99, 0x4A), 1));
        brush.Freeze();
        return brush;
    }

    // ---------- Качество ----------

    private void BuildQuality(StackPanel page)
    {
        var p = _palette;
        page.Children.Add(new TextBlock
        {
            Text = S.QualityIntro, FontSize = 13, Foreground = p.TextSecondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, -8, 0, 14),
        });
        var c = _settings.Conversion;
        page.Children.Add(SliderCard(S.JpegTitle, S.JpegHint, ConversionSettings.MinImageQuality, ConversionSettings.MaxImageQuality,
            c.JpegQuality, v => Change(s => s with { Conversion = s.Conversion with { JpegQuality = v } })));
        page.Children.Add(SliderCard(S.WebpTitle, S.WebpHint, ConversionSettings.MinImageQuality, ConversionSettings.MaxImageQuality,
            c.WebpQuality, v => Change(s => s with { Conversion = s.Conversion with { WebpQuality = v } })));
        page.Children.Add(SliderCard(S.CrfTitle, S.CrfHint, ConversionSettings.MinVideoCrf, ConversionSettings.MaxVideoCrf,
            c.VideoCrf, v => Change(s => s with { Conversion = s.Conversion with { VideoCrf = v } })));

        var mp3 = new ChoicePills(p, ConversionSettings.Mp3Bitrates.Select(b => ((object)b, S.Kbps(b))),
            ConversionSettings.Mp3Bitrates.Contains(c.Mp3BitrateKbps) ? c.Mp3BitrateKbps : null, S.Mp3Title)
            { Margin = new Thickness(0, 12, 0, -6) };
        mp3.SelectionChanged += v => Change(s => s with { Conversion = s.Conversion with { Mp3BitrateKbps = (int)v } });
        var mp3Panel = new StackPanel();
        mp3Panel.Children.Add(Titles(S.Mp3Title, S.Mp3Hint));
        mp3Panel.Children.Add(mp3);
        page.Children.Add(Card(mp3Panel));
    }

    private Border SliderCard(string title, string hint, int min, int max, int value, Action<int> changed)
    {
        var p = _palette;
        var slider = new ValueSlider(p, min, max, value, title) { Width = 240 };
        var label = new TextBlock
        {
            Text = Math.Clamp(value, min, max).ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = p.Text, Width = 34,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 1),
        };
        slider.ValueChanged += v =>
        {
            label.Text = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            changed(v);
        };
        var control = new StackPanel { Orientation = Orientation.Horizontal };
        control.Children.Add(slider);
        control.Children.Add(label);
        return Card(Row(title, hint, control));
    }

    // ---------- Колесо ----------

    private void BuildWheel(StackPanel page)
    {
        var p = _palette;
        page.Children.Add(new TextBlock
        {
            Text = S.WheelIntro, FontSize = 13, Foreground = p.TextSecondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, -8, 0, 14),
        });
        page.Children.Add(ToggleCard(S.MruFirst, S.MruFirstHint, _settings.Wheel.MruFirst, on =>
        {
            Change(s => s with { Wheel = s.Wheel with { MruFirst = on } });
            RefreshWheel();
        }, new Thickness(0, 0, 0, 16)));

        var groups = new ChoicePills(p, WheelGroups.All.Select(g => ((object)g, S.GroupName(g))), _group, S.PageWheel)
            { Margin = new Thickness(0, 0, 0, 8) };
        groups.SelectionChanged += v => WheelGroup = (string)v;
        page.Children.Add(groups);

        // Карточка: режим и «Сбросить» сверху; список слева, мини-колесо справа.
        var top = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modes = new ChoicePills(p, [(OperationMode.Convert, S.ModeConvert), (OperationMode.Tool, S.ModeTools)], _mode, S.PageWheel)
            { Margin = new Thickness(0, 0, 0, -6), VerticalAlignment = VerticalAlignment.Center };
        modes.SelectionChanged += v => WheelMode = (OperationMode)v;
        top.Children.Add(modes);
        var reset = new SettingsButton(p, S.Reset, Glyphs.Undo, S.ResetHint);
        reset.Click += () =>
        {
            Change(s => s with { Wheel = s.Wheel.Reset(_group) });
            RefreshWheel();
        };
        Grid.SetColumn(reset, 1);
        top.Children.Add(reset);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _wheelList = new StackPanel { Margin = new Thickness(-6, 0, 18, 0) };
        body.Children.Add(_wheelList);

        var previewPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        previewPanel.Children.Add(new TextBlock
        {
            Text = S.Preview, FontSize = 12, Foreground = p.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        });
        _wheelPreviewHost = new Border
        {
            Width = PreviewSize + 16, Height = PreviewSize + 16, CornerRadius = new CornerRadius(12),
            Background = p.Preview, Padding = new Thickness(8),
        };
        previewPanel.Children.Add(_wheelPreviewHost);
        Grid.SetColumn(previewPanel, 1);
        body.Children.Add(previewPanel);

        var cardPanel = new StackPanel();
        cardPanel.Children.Add(top);
        cardPanel.Children.Add(body);
        page.Children.Add(Card(cardPanel));

        _preview = null;
        RefreshWheel();
    }

    /// <summary>Список операций выбранного типа и режима и мини-колесо — по текущим настройкам.</summary>
    private void RefreshWheel()
    {
        if (_page != SettingsPage.Wheel || _wheelList is null) return;
        var p = _palette;
        var ops = WheelGroups.Operations(_group);
        var entries = WheelArrangement.Ordered(ops, _mode, _settings.Wheel.For(_group, _mode));

        _wheelList.Children.Clear();
        if (entries.Count == 0)
            _wheelList.Children.Add(new TextBlock
            {
                Text = S.NothingInMode, FontSize = 13, Foreground = p.TextSecondary, Margin = new Thickness(6, 8, 0, 8),
                TextWrapping = TextWrapping.Wrap,
            });
        var shown = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            // Счёт — для одного файла: «от 2 файлов» (Один PDF) на колесе одного файла не бывает и место не занимает.
            var overLimit = !entry.Hidden && entry.Operation.MinFiles <= 1 && ++shown > WheelGeometry.MaxSectors;
            _wheelList.Children.Add(EntryRow(entries, i, overLimit));
        }

        var (convert, tools) = WheelArrangement.Arrange(ops, _group, _settings.Wheel);
        _preview ??= new WheelControl { Width = WheelWindow.DipSize, Height = WheelWindow.DipSize, IsHitTestVisible = false };
        _preview.Palette = p.IsLight ? WheelPalette.Light(_accent) : WheelPalette.Dark(_accent);
        _preview.Backdrop = WheelBackdrop.Solid;
        _preview.FillPercent = _settings.WheelOpacity;
        _preview.IsPending = false;
        _preview.Mode = _mode;
        _preview.Items = _mode == OperationMode.Convert ? convert : tools;
        _preview.ShowModeDots = convert.Count > 0 && tools.Count > 0;
        _preview.IdleText = S.GroupName(_group);
        _wheelPreviewHost.Child ??= new Viewbox { Child = _preview, Stretch = Stretch.Uniform };
    }

    private FrameworkElement EntryRow(IReadOnlyList<WheelEntry> entries, int index, bool overLimit)
    {
        var p = _palette;
        var entry = entries[index];
        var op = entry.Operation;
        var row = new Grid { Margin = new Thickness(0, 1, 0, 1), MinHeight = 40, Background = Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckMark(p, !entry.Hidden, S.ShowOnWheel + ": " + Loc.Label(op))
        {
            Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = p.MakeToolTip(S.ShowOnWheel),
        };
        check.Changed += visible => Edit(entries, hiddenId: op.Id, visible: visible);
        row.Children.Add(check);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Opacity = entry.Hidden ? 0.55 : 1 };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock
        {
            Text = OperationGlyphs.For(op), FontFamily = UiFonts.Icons, FontSize = 14, Foreground = p.TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        });
        title.Children.Add(new TextBlock { Text = Loc.Label(op), FontSize = 14, Foreground = p.Text, VerticalAlignment = VerticalAlignment.Center });
        text.Children.Add(title);
        var notes = Notes(op, overLimit);
        if (notes.Length > 0)
            text.Children.Add(new TextBlock
            {
                Text = notes, FontSize = 12, Foreground = overLimit ? p.Accent : p.TextTertiary,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 1, 0, 0),
            });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var up = new SettingsButton(p, null, Glyphs.Up, S.MoveUp, subtle: true);
        up.SetEnabled(index > 0);
        up.Click += () => Edit(entries, move: (index, index - 1));
        var down = new SettingsButton(p, null, Glyphs.Down, S.MoveDown, subtle: true);
        down.SetEnabled(index < entries.Count - 1);
        down.Click += () => Edit(entries, move: (index, index + 1));
        buttons.Children.Add(up);
        buttons.Children.Add(down);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(buttons);

        // Перетаскивание строки мышью — то же, что кнопки «Выше/Ниже».
        Point? pressedAt = null;
        row.PreviewMouseLeftButtonDown += (_, e) =>
            pressedAt = e.OriginalSource is DependencyObject d && (IsInside(d, check) || IsInside(d, buttons)) ? null : e.GetPosition(row);
        row.PreviewMouseLeftButtonUp += (_, _) => pressedAt = null;
        row.MouseMove += (_, e) =>
        {
            if (pressedAt is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            var now = e.GetPosition(row);
            if (Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance
                && Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            pressedAt = null;
            try { DragDrop.DoDragDrop(row, new DataObject(typeof(WheelRowDrag), new WheelRowDrag(index)), DragDropEffects.Move); }
            catch (Exception) { /* перетаскивание не удалось — остаются кнопки «Выше/Ниже» */ }
        };
        row.AllowDrop = true;
        row.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(typeof(WheelRowDrag)) ? DragDropEffects.Move : DragDropEffects.None;
            row.Background = e.Effects == DragDropEffects.Move ? p.NavHover : Brushes.Transparent;
            e.Handled = true;
        };
        row.DragLeave += (_, _) => row.Background = Brushes.Transparent;
        row.Drop += (_, e) =>
        {
            row.Background = Brushes.Transparent;
            if (e.Data.GetData(typeof(WheelRowDrag)) is WheelRowDrag drag && drag.Index != index)
                Edit(entries, move: (drag.Index, index));
        };
        return row;
    }

    private sealed record WheelRowDrag(int Index);

    private string Notes(Operation op, bool overLimit)
    {
        var parts = new List<string>();
        if (WheelGroups.OnlyFor(op, _group) is { } only) parts.Add(S.OnlyFor(only));
        if (op.MinFiles > 1) parts.Add(S.FromFiles(op.MinFiles));
        if (op.Engine == EngineKind.Office && op.Fallback is null) parts.Add(S.NeedsOffice);
        if (overLimit) parts.Add(S.OverLimit);
        return string.Join(" · ", parts);
    }

    /// <summary>Правка списка: переставить (move) или показать/скрыть (hiddenId). Сохраняется весь порядок.</summary>
    private void Edit(IReadOnlyList<WheelEntry> entries, (int From, int To)? move = null, string? hiddenId = null, bool visible = true)
    {
        var order = entries.Select(e => e.Operation.Id).ToList();
        var hidden = entries.Where(e => e.Hidden).Select(e => e.Operation.Id).ToList();
        if (move is var (from, to) && from >= 0 && from < order.Count && to >= 0 && to < order.Count)
        {
            var id = order[from];
            order.RemoveAt(from);
            order.Insert(to, id);
        }
        if (hiddenId is not null)
        {
            hidden.Remove(hiddenId);
            if (!visible) hidden.Add(hiddenId);
        }
        var group = _group;
        var mode = _mode;
        Change(s => s with { Wheel = s.Wheel.WithLayout(group, mode, order, hidden) });
        RefreshWheel();
    }

    /// <summary>Пункт раздела слева: значок, подпись; выбранный — с полоской акцента.</summary>
    private sealed class NavItem : Pressable
    {
        private readonly bool _selected;
        private readonly Border _bar;

        public NavItem(SettingsPalette palette, string glyph, string text, bool selected) : base(palette)
        {
            _selected = selected;
            CornerRadius = new CornerRadius(5);
            Margin = new Thickness(0, 1, 0, 1);
            Padding = new Thickness(0, 9, 12, 10);
            Cursor = Cursors.Arrow;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _bar = new Border
            {
                Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5), Background = palette.Accent,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                Visibility = selected ? Visibility.Visible : Visibility.Hidden,
            };
            grid.Children.Add(_bar);
            var icon = new TextBlock
            {
                Text = glyph, FontFamily = UiFonts.Icons, FontSize = 16, Foreground = palette.Text,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(icon, 1);
            grid.Children.Add(icon);
            var label = new TextBlock { Text = text, FontSize = 14, Foreground = palette.Text, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 2);
            grid.Children.Add(label);
            Child = grid;
            System.Windows.Automation.AutomationProperties.SetName(this, text);
            UpdateLook();
        }

        protected override void UpdateLook()
        {
            var p = Palette;
            Background = _selected ? p.NavSelected : IsPressed ? p.ControlPressed : IsHovered ? p.NavHover : Brushes.Transparent;
            BorderBrush = HasKeyboardFocus ? p.Text : Brushes.Transparent;
        }
    }
}
