using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.Annotate;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Окно «Пометки» (D-ANNOTATE) → PNG, светлая и тёмная тема, английский. Картинка — нарисованный снимок «рабочего стола»
/// (как у скриншотов снимка экрана, 1650 × 960) с пометками всех видов. И проверки результата: полный размер,
/// картинка пиксель в пиксель, пометки и размытие меняют нужные пиксели.
/// </summary>
public class AnnotateScreenshots
{
    internal static AnnotateView Make(bool light)
    {
        var picture = CaptureScreenshots.FakeDesktop(light);
        var view = new AnnotateView(light ? SettingsPalette.Light() : SettingsPalette.Dark(),
            Loc.Current == UiLanguage.En ? "Screenshot 2026-10-24 15-30-05.png" : "Снимок 2026-10-24 15-30-05.png");
        view.SetImage(picture);
        view.SelectTool(AnnotationTool.Arrow);
        view.SelectColor(AnnotationColors.Red);
        view.SelectSize(StrokeSize.Medium);
        foreach (var shape in Shapes(picture.PixelWidth, picture.PixelHeight)) view.Document.Add(shape);
        return view;
    }

    /// <summary>По пометке каждого вида: рамка вокруг заголовка, стрелка, маркер, размытие, эллипс, карандаш, текст и номера.</summary>
    internal static IEnumerable<Annotation> Shapes(int w, int h)
    {
        ImagePoint P(double x, double y) => new(x * w, y * h);
        var en = Loc.Current == UiLanguage.En;
        yield return new Annotation(AnnotationTool.Rectangle, [P(0.33, 0.255), P(0.62, 0.33)], AnnotationColors.Red, StrokeSize.Medium);
        yield return new Annotation(AnnotationTool.Arrow, [P(0.86, 0.12), P(0.64, 0.27)], AnnotationColors.Red, StrokeSize.Thick);
        yield return new Annotation(AnnotationTool.Marker, [P(0.335, 0.425), P(0.45, 0.425), P(0.60, 0.425)], AnnotationColors.Yellow, StrokeSize.Medium);
        yield return new Annotation(AnnotationTool.Blur, [P(0.33, 0.545), P(0.62, 0.585)], AnnotationColors.Red, StrokeSize.Medium);
        yield return new Annotation(AnnotationTool.Ellipse, [P(0.32, 0.6), P(0.62, 0.68)], AnnotationColors.Green, StrokeSize.Medium);
        yield return new Annotation(AnnotationTool.Pen,
            [P(0.66, 0.62), P(0.69, 0.58), P(0.72, 0.62), P(0.75, 0.57), P(0.78, 0.61)], AnnotationColors.Blue, StrokeSize.Medium);
        yield return new Annotation(AnnotationTool.Text, [P(0.66, 0.42)], AnnotationColors.Purple, StrokeSize.Medium)
            { Text = en ? "Book today!" : "Забронировать сегодня!" };
        yield return new Annotation(AnnotationTool.Number, [P(0.30, 0.29)], AnnotationColors.Blue, StrokeSize.Medium) { Number = 1 };
        yield return new Annotation(AnnotationTool.Number, [P(0.30, 0.425)], AnnotationColors.Blue, StrokeSize.Medium) { Number = 2 };
        yield return new Annotation(AnnotationTool.Number, [P(0.30, 0.64)], AnnotationColors.Blue, StrokeSize.Medium) { Number = 3 };
    }

    internal static void Save(AnnotateView view, bool light, string name) =>
        TrimScreenshots.SaveWindow(view, AnnotateView.DefaultWidth, Loc.T.AnnotateTitle + " — " + (Loc.Current == UiLanguage.En ? "Screenshot.png" : "Снимок.png"),
            view.Palette, light, name);

    [Theory]
    [InlineData(true, "annotate-light")]
    [InlineData(false, "annotate-dark")]
    public void Annotate_window(bool light, string name)
    {
        Sta.Run(() =>
        {
            var view = Make(light);
            Assert.True(view.CanSave);
            if (!light) view.SelectTool(AnnotationTool.Blur);
            Save(view, light, name);
        });
    }

    /// <summary>Белая картинка 400 × 300 с тонкими чёрными полосами через пиксель — любое сглаживание их бы испортило.</summary>
    private static BitmapSource Stripes(double dpi = 96)
    {
        const int w = 400, h = 300, stride = w * 4;
        var pixels = new byte[stride * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var v = (byte)(x % 2 == 0 && y < 100 ? 0 : 255);
                var i = y * stride + x * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = v;
                pixels[i + 3] = 255;
            }
        var image = BitmapSource.Create(w, h, dpi, dpi, PixelFormats.Bgr32, null, pixels, stride);
        image.Freeze();
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        bgra.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private static (byte B, byte G, byte R) At(byte[] pixels, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(72)]   // картинка с «чужим» DPI — результат всё равно в её пикселях
    [InlineData(300)]
    public void Render_keeps_full_size_and_changes_only_marked_pixels(double dpi)
    {
        Sta.Run(() =>
        {
            var source = Stripes(dpi);
            var renderer = new AnnotationRenderer(source);
            var arrow = new Annotation(AnnotationTool.Arrow, [new ImagePoint(40, 200), new ImagePoint(360, 200)], AnnotationColors.Red, StrokeSize.Medium);

            var plain = renderer.Render([]);
            Assert.Equal((400, 300), (plain.PixelWidth, plain.PixelHeight));
            Assert.Equal(Pixels(source), Pixels(plain)); // без пометок — точная копия, полосы не размыты

            var result = renderer.Render([arrow]);
            Assert.Equal((source.PixelWidth, source.PixelHeight), (result.PixelWidth, result.PixelHeight));
            var before = Pixels(source);
            var after = Pixels(result);
            Assert.Equal((255, 255, 255), At(before, 400, 200, 200));
            var (b, g, r) = At(after, 400, 200, 200); // под древком — красный
            Assert.True(r > 200 && g < 60 && b < 80, $"{r},{g},{b}");
            Assert.Equal(At(before, 400, 200, 50), At(after, 400, 200, 50)); // полосы вдали не тронуты
            Assert.Equal(At(before, 400, 201, 50), At(after, 400, 201, 50));
        });
    }

    [Fact]
    public void Pixelate_hides_the_stripes_under_it()
    {
        Sta.Run(() =>
        {
            var source = Stripes();
            var renderer = new AnnotationRenderer(source);
            var blur = new Annotation(AnnotationTool.Blur, [new ImagePoint(100, 10), new ImagePoint(300, 90)], AnnotationColors.Red, StrokeSize.Medium);
            var after = Pixels(renderer.Render([blur]));
            // Клетка 8 px: внутри неё соседние пиксели одинаковые (полос нет), цвет — средний серый.
            var a = At(after, 400, 104, 20);
            Assert.Equal(a, At(after, 400, 105, 20));
            Assert.InRange(a.R, 100, 155);
            // Вне области полосы на месте.
            Assert.Equal((0, 0, 0), At(after, 400, 50, 20));
            Assert.Equal((255, 255, 255), At(after, 400, 51, 20));
        });
    }

    [Fact]
    public void Keys_undo_redo_and_unsaved_marks_ask_before_closing()
    {
        Sta.Run(() =>
        {
            var view = Make(light: true);
            var count = view.Document.Shapes.Count;
            Assert.True(view.HandleKey(Key.Z, ModifierKeys.Control));
            Assert.Equal(count - 1, view.Document.Shapes.Count);
            Assert.True(view.HandleKey(Key.Y, ModifierKeys.Control));
            Assert.Equal(count, view.Document.Shapes.Count);
            Assert.True(view.HandleKey(Key.Delete, ModifierKeys.None));
            Assert.Equal(count - 1, view.Document.Shapes.Count);
            Assert.True(view.HandleKey(Key.D7, ModifierKeys.None));
            Assert.Equal(AnnotationTool.Blur, view.Tool);

            var cancel = 0;
            view.CancelRequested += () => cancel++;
            Assert.True(view.HandleKey(Key.Escape, ModifierKeys.None));
            Assert.Equal(1, cancel);
            Assert.True(view.Document.HasUnsavedChanges);
            view.ShowConfirm();
            Assert.True(view.HandleKey(Key.Escape, ModifierKeys.None)); // Esc в вопросе — «Вернуться»
            Assert.False(view.IsConfirming);

            var saved = 0;
            view.SaveRequested += () => saved++;
            view.RequestSave();
            Assert.Equal(1, saved);

            var empty = new AnnotateView(SettingsPalette.Dark(), "x.png");
            Assert.False(empty.CanSave);
            empty.SaveRequested += () => Assert.Fail("картинки ещё нет");
            empty.RequestSave();
        });
    }
}

/// <summary>Окно «Пометки» по-английски (язык меняется — в коллекции языка).</summary>
[Collection(LanguageCollection.Name)]
public class AnnotateEnglishScreenshots
{
    [Fact]
    public void Annotate_window_in_english()
    {
        Sta.Run(() =>
        {
            using var _ = Loc.Use(UiLanguage.En);
            AnnotateScreenshots.Save(AnnotateScreenshots.Make(light: true), light: true, "annotate-light-en");
        });
    }
}
