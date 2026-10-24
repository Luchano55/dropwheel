using DropWheel.Capture;
using DropWheel.Localization;
using DropWheel.Settings;

namespace DropWheel.Tests.Capture;

/// <summary>Снимок экрана (D-CAPTURE): выделение, мониторы и DPI, раскладка подписи и панели, имя файла, сочетание.</summary>
public class CaptureTests : IDisposable
{
    // Основной 1920×1080 при 100 %; справа — 2560×1440 при 150 %, сдвинут вниз на 100 px;
    // слева — выше основного (отрицательные координаты).
    private static readonly PixelRect Main = new(0, 0, 1920, 1080);
    private static readonly PixelRect Right = new(1920, 100, 2560, 1440);
    private static readonly PixelRect Left = new(-1280, -300, 1280, 1024);

    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    // ---------- прямоугольники ----------

    [Fact]
    public void Rect_edges_intersect_union()
    {
        var r = PixelRect.FromEdges(10, 20, 110, 70);
        Assert.Equal((100, 50, 110, 70), (r.Width, r.Height, r.Right, r.Bottom));
        Assert.True(r.Contains(10, 20));
        Assert.False(r.Contains(110, 20)); // правый край не входит
        Assert.Equal(PixelRect.FromEdges(50, 20, 110, 70), r.Intersect(new PixelRect(50, 0, 500, 500)));
        Assert.True(r.Intersect(new PixelRect(500, 500, 5, 5)).IsEmpty);
        Assert.Equal(PixelRect.FromEdges(-1280, -300, 4480, 1540), Left.Union(Main).Union(Right));
        Assert.Equal(Main, default(PixelRect).Union(Main));
        Assert.Equal(0, Main.DistanceSquared(5, 5));
        Assert.Equal(100, Main.DistanceSquared(1929, 5));
    }

    // ---------- выделение ----------

    [Theory]
    [InlineData(100, 100, 300, 250)] // вправо-вниз
    [InlineData(300, 250, 100, 100)] // влево-вверх
    [InlineData(300, 100, 100, 250)] // влево-вниз
    [InlineData(100, 250, 300, 100)] // вправо-вверх
    public void Drag_in_any_direction_gives_same_rect(int ax, int ay, int bx, int by)
    {
        Assert.Equal(new PixelRect(100, 100, 200, 150), CaptureSession.FromDrag(ax, ay, bx, by, Main));
    }

    [Fact]
    public void Drag_is_clamped_to_screen_and_thin_drag_is_nothing()
    {
        Assert.Equal(PixelRect.FromEdges(1800, 1000, 1920, 1080), CaptureSession.FromDrag(1800, 1000, 2500, 1600, Main));
        Assert.Equal(PixelRect.FromEdges(0, 0, 50, 50), CaptureSession.FromDrag(-40, -40, 50, 50, Main));
        Assert.Null(CaptureSession.FromDrag(100, 100, 101, 400, Main)); // 1 px шириной
        Assert.Null(CaptureSession.FromDrag(-500, -500, -10, -10, Main)); // целиком за экраном
        Assert.NotNull(CaptureSession.FromDrag(100, 100, 100 + CaptureSession.MinSize, 400, Main));
    }

    [Fact]
    public void Drag_across_monitors_keeps_whole_rect()
    {
        var s = new CaptureSession([Main, Right, Left]);
        s.Press(1800, 500);
        s.Move(2200, 900);
        Assert.Equal(PixelRect.FromEdges(1800, 500, 2200, 900), s.Highlight);
        Assert.True(s.IsDragging);
        Assert.Equal(PixelRect.FromEdges(1800, 500, 2200, 900), s.Release(2200, 900));
        Assert.False(s.IsDragging);
        Assert.Equal(s.Selection, s.Highlight);
    }

    [Fact]
    public void Click_picks_topmost_window_then_monitor()
    {
        PixelRect editor = new(100, 100, 800, 600), browser = new(50, 50, 1400, 900), maximized = new(1912, 92, 2576, 1456);
        var s = new CaptureSession([Main, Right], [editor, browser, maximized]);

        s.Hover(200, 200);
        Assert.Equal(editor, s.Highlight); // верхнее из двух
        s.Hover(1300, 800);
        Assert.Equal(browser, s.Highlight);
        s.Hover(1700, 1000);
        Assert.Equal(Main, s.Highlight); // окна нет — монитор

        s.Press(1300, 800);
        s.Move(1302, 803); // в пределах ClickSlop — всё ещё щелчок
        Assert.Equal(browser, s.Highlight);
        Assert.Equal(browser, s.Release(1303, 802));

        // Развёрнутое окно чуть шире монитора — подрезается по экрану.
        s.Press(3000, 500);
        Assert.Equal(Right, s.Release(3000, 500));

        // Окно на два монитора — целиком (подрезка только по краям экрана).
        PixelRect wide = new(1000, 200, 2000, 600);
        s = new CaptureSession([Main, Right], [wide]);
        s.Press(1500, 400);
        Assert.Equal(PixelRect.FromEdges(1000, 200, 3000, 800), s.Release(1500, 400));
    }

    [Fact]
    public void Monitor_selection_and_gaps()
    {
        var s = new CaptureSession([Main, Right, Left]);
        Assert.Equal(Right, s.SelectMonitor(3000, 900));
        Assert.Equal(Right, s.Selection);
        Assert.Equal(Left, s.MonitorAt(-5, -200));
        Assert.Equal(Right, s.MonitorAt(2500, 50)); // промежуток над правым монитором — ближайший
        Assert.Equal(Main, s.MonitorAt(1000, 1300)); // под основным
    }

    [Fact]
    public void New_press_replaces_selection_and_hover_does_not_move_it()
    {
        var s = new CaptureSession([Main]);
        s.Press(10, 10);
        s.Release(500, 400);
        var first = s.Selection;
        Assert.NotNull(first);
        s.Hover(900, 900);
        Assert.Equal(first, s.Highlight); // выбранное не меняется от движения мыши
        s.Press(600, 600);
        Assert.Null(s.Selection);
        s.Move(700, 601);
        Assert.Null(s.Highlight); // 1 px высотой — пока нечего подсвечивать
        Assert.Null(s.Release(700, 601)); // слишком тонко — ничего, снова подсветка под курсором
        Assert.Equal(Main, s.Highlight);
        s.SelectMonitor(5, 5);
        s.Clear(5, 5);
        Assert.Null(s.Selection);
        Assert.Throws<ArgumentException>(() => new CaptureSession([]));
    }

    // ---------- DPI ----------

    [Fact]
    public void Physical_and_dip_per_monitor()
    {
        var m150 = new CaptureMonitor(Right, 144);
        Assert.Equal(1.5, m150.Scale);
        Assert.Equal((2560 / 1.5, 1440 / 1.5), (m150.DipWidth, m150.DipHeight));
        Assert.Equal((100.0, 200.0), m150.ToLocalDip(1920 + 150, 100 + 300));
        Assert.Equal(new DipRect(-20, 0, 200, 100), m150.ToLocalDip(new PixelRect(1890, 100, 300, 150)));
        Assert.Equal((1920 + 150, 100 + 300), m150.ToScreen(100, 200));
        Assert.Equal((1920 + 2, 101), m150.ToScreen(1.2, 0.4)); // округление до пикселя

        var m100 = new CaptureMonitor(Left, 96);
        Assert.Equal((10.0, 20.0), m100.ToLocalDip(-1270, -280));
        Assert.Equal(1.0, new CaptureMonitor(Main, 0).Scale); // DPI не узнали — 100 %
        Assert.Equal(1.25, new CaptureMonitor(Main, 120).Scale);
    }

    // ---------- раскладка ----------

    private static readonly DipRect Area = new(0, 0, 1280, 720);

    [Fact]
    public void Toolbar_goes_below_then_above_then_inside()
    {
        var (x, y) = CaptureLayout.Toolbar(new DipRect(100, 100, 400, 300), 360, 44, Area);
        Assert.Equal((140.0, 408.0), (x, y)); // под выделением, по правому краю

        (_, y) = CaptureLayout.Toolbar(new DipRect(100, 400, 400, 300), 360, 44, Area);
        Assert.Equal(400 - 8 - 44, y); // снизу нет места — над ним

        (x, y) = CaptureLayout.Toolbar(Area, 360, 44, Area);
        Assert.Equal((1280 - 360 - 8.0, 720 - 8 - 44.0), (x, y)); // весь экран — внутри у нижнего края

        (x, _) = CaptureLayout.Toolbar(new DipRect(5, 100, 50, 50), 360, 44, Area);
        Assert.Equal(8, x); // не за левым краем
    }

    [Fact]
    public void Size_label_above_or_inside()
    {
        Assert.Equal((100.0, 100 - 6 - 24.0), CaptureLayout.SizeLabel(new DipRect(100, 100, 400, 300), 90, 24, Area));
        Assert.Equal((100.0, 6.0), CaptureLayout.SizeLabel(new DipRect(100, 0, 400, 300), 90, 24, Area));
        Assert.Equal(1280 - 90 - 8, CaptureLayout.SizeLabel(new DipRect(1250, 100, 30, 30), 90, 24, Area).X);
        Assert.Equal("1920 × 1080", CaptureLayout.SizeText(Main));
    }

    // ---------- имя файла ----------

    [Fact]
    public void File_name_and_free_name()
    {
        var now = new DateTime(2026, 10, 24, 15, 30, 5);
        Assert.Equal("Снимок 2026-10-24 15-30-05.png", CaptureNaming.FileName(now));
        var dir = Path.Combine(_dir.Path, "Мои фото");
        var first = Path.Combine(dir, "Снимок 2026-10-24 15-30-05.png");
        Assert.Equal(first, CaptureNaming.FilePath(dir, now, _ => false));
        Assert.Equal(Path.Combine(dir, "Снимок 2026-10-24 15-30-05 (1).png"), CaptureNaming.FilePath(dir, now, p => p == first));
    }

    // ---------- сочетание ----------

    [Theory]
    [InlineData("Ctrl+Alt+S", KeyChord.Control | KeyChord.Alt, 0x53, "Ctrl+Alt+S")]
    [InlineData(" alt + control + s ", KeyChord.Control | KeyChord.Alt, 0x53, "Ctrl+Alt+S")]
    [InlineData("Shift+Ctrl+PrtSc", KeyChord.Control | KeyChord.Shift, 0x2C, "Ctrl+Shift+PrintScreen")]
    [InlineData("PrintScreen", 0u, 0x2C, "PrintScreen")]
    [InlineData("Print Screen", 0u, 0x2C, "PrintScreen")]
    [InlineData("F9", 0u, 0x78, "F9")]
    [InlineData("win+shift+F24", KeyChord.Win | KeyChord.Shift, 0x87, "Shift+Win+F24")]
    [InlineData("Ctrl+5", KeyChord.Control, 0x35, "Ctrl+5")]
    public void Chord_parses(string text, uint mods, int vk, string canonical)
    {
        var chord = KeyChord.Parse(text);
        Assert.NotNull(chord);
        Assert.Equal((mods, vk), (chord.Modifiers, chord.Key));
        Assert.Equal(canonical, chord.ToString());
        Assert.Equal(chord, KeyChord.Parse(chord.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S")]            // просто буква — это набор текста
    [InlineData("Shift+S")]
    [InlineData("Ctrl+Alt")]     // нет клавиши
    [InlineData("Ctrl+S+D")]     // две клавиши
    [InlineData("Ctrl+Ctrl+S")]
    [InlineData("Ctrl++S")]
    [InlineData("Ctrl+Space")]   // не из списка
    [InlineData("F0")]
    [InlineData("F25")]
    [InlineData("F01")]
    [InlineData("Ctrl+Ы")]
    public void Bad_chord_is_rejected(string? text)
    {
        Assert.Null(KeyChord.Parse(text));
    }

    [Fact]
    public void Chord_default_and_display()
    {
        Assert.Equal("Ctrl+Alt+S", KeyChord.Default.ToString());
        Assert.Equal(KeyChord.DefaultText, KeyChord.Normalize("garbage"));
        Assert.Equal("Ctrl+Print Screen", KeyChord.Parse("ctrl+prtsc")!.DisplayName);
    }

    [Theory]
    [InlineData("""{ "captureKeys": "shift+ctrl+prtsc" }""", true, "Ctrl+Shift+PrintScreen")]
    [InlineData("""{ "captureKeys": "S", "captureHotkey": false }""", false, "Ctrl+Alt+S")]
    [InlineData("""{ "captureKeys": null }""", true, "Ctrl+Alt+S")]
    [InlineData("{}", true, "Ctrl+Alt+S")]
    public void Settings_capture_hotkey(string json, bool on, string keys)
    {
        var path = Path.Combine(_dir.Path, "settings.json");
        File.WriteAllText(path, json);
        var s = AppSettings.Load(path);
        Assert.Equal((on, keys), (s.CaptureHotkey, s.CaptureKeys));
        s.Save(path);
        Assert.Equal(s, AppSettings.Load(path));
    }
}

[Collection(LanguageCollection.Name)]
public class CaptureEnglishTests
{
    [Fact]
    public void English_file_name()
    {
        using var _ = Loc.Use(UiLanguage.En);
        Assert.Equal("Screenshot 2026-10-24 15-30-05.png", CaptureNaming.FileName(new DateTime(2026, 10, 24, 15, 30, 5)));
    }
}
