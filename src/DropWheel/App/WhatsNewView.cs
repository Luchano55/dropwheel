using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DropWheel.Localization;
using DropWheel.Ui;
using DropWheel.Updates;

namespace DropWheel.App;

/// <summary>
/// Содержимое окна «Что нового» (D-WHATSNEW): заголовок, текст разделов CHANGELOG (с прокруткой),
/// ссылка «Все изменения» и кнопка «Понятно». Сам ничего не читает: разделы приносит окно или скриншот-тест.
/// </summary>
public sealed class WhatsNewView : Border
{
    public const double DefaultWidth = 520;
    private const double MaxTextHeight = 380;

    private SettingsPalette _palette;
    private readonly string _version;
    private readonly IReadOnlyList<ChangelogNote> _notes;

    public event Action? CloseRequested;
    public event Action? AllChangesRequested;

    public WhatsNewView(SettingsPalette palette, string version, IReadOnlyList<ChangelogNote> notes)
    {
        _palette = palette;
        _version = version;
        _notes = notes;
        Width = DefaultWidth;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Documents.TextElement.SetFontFamily(this, UiFonts.Text);
        System.Windows.Documents.TextElement.SetFontSize(this, 14);
        Build();
    }

    public static string TitleFor(string version) => Loc.F(Loc.T.WhatsNewTitle, version);

    public SettingsPalette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Build();
        }
    }

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
        var row = new DockPanel { LastChildFill = false };
        var ok = new TrimView.PrimaryButton(p, Loc.T.WhatsNewOk) { MinWidth = 120 };
        ok.Click += () => CloseRequested?.Invoke();
        DockPanel.SetDock(ok, Dock.Right);
        row.Children.Add(ok);
        row.Children.Add(MakeLink(p));
        footer.Child = row;
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new StackPanel { Margin = new Thickness(24, 20, 8, 16) };
        body.Children.Add(new TextBlock
        {
            Text = TitleFor(_version), FontFamily = UiFonts.Display, FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = p.Text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 16, 12),
        });

        var text = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        if (_notes.Count == 0)
            text.Children.Add(Line(p, Loc.T.WhatsNewMissing, p.TextSecondary));
        foreach (var note in _notes)
        {
            // Несколько версий сразу (обновились через одну) — над каждой её номер.
            if (_notes.Count > 1)
                text.Children.Add(new TextBlock
                {
                    Text = note.Version, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = p.Text,
                    Margin = new Thickness(0, text.Children.Count == 0 ? 0 : 14, 0, 4),
                });
            foreach (var line in note.Text.Split('\n'))
                text.Children.Add(MakeLine(p, line));
        }
        body.Children.Add(new ScrollViewer
        {
            Content = text, MaxHeight = MaxTextHeight, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
        });
        root.Children.Add(body);
        Child = root;
    }

    /// <summary>Строка раздела: пункт «•» — с висячим отступом, подзаголовок — полужирным, пустая — промежуток.</summary>
    private static FrameworkElement MakeLine(SettingsPalette p, string line)
    {
        if (line.Length == 0) return new Border { Height = 8 };
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("• ", StringComparison.Ordinal))
            return new TextBlock
            {
                // Подзаголовок («Исправлено») — без точки в конце; обычный абзац — обычным шрифтом.
                Text = line, FontWeight = ".:!?…)»".Contains(line[^1]) ? FontWeights.Normal : FontWeights.SemiBold, Foreground = p.Text, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4),
            };

        var level = line.Length - trimmed.Length > 0 ? 1 : 0;
        var grid = new Grid { Margin = new Thickness(level * 18, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = "•", Foreground = p.TextSecondary });
        var tb = Line(p, trimmed[2..], p.Text);
        Grid.SetColumn(tb, 1);
        grid.Children.Add(tb);
        return grid;
    }

    private static TextBlock Line(SettingsPalette p, string text, Brush brush) =>
        new() { Text = text, Foreground = brush, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };

    /// <summary>Ссылка «Все изменения» — открывает страницу выпусков в браузере.</summary>
    private TextBlock MakeLink(SettingsPalette p)
    {
        var link = new TextBlock
        {
            Text = Loc.T.WhatsNewAll, FontSize = 13, Foreground = p.Accent, Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center, Focusable = true, FocusVisualStyle = null,
        };
        AutomationProperties.SetName(link, Loc.T.WhatsNewAll);
        link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
        link.MouseLeave += (_, _) => link.TextDecorations = null;
        link.MouseLeftButtonUp += (_, _) => AllChangesRequested?.Invoke();
        link.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                AllChangesRequested?.Invoke();
            }
        };
        return link;
    }
}
