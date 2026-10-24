using DropWheel.Annotate;
using DropWheel.Capture;
using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Wheel;

namespace DropWheel.Tests.Annotate;

/// <summary>«Пометки» (D-ANNOTATE): каталог и колесо, модель пометок, история, перевод координат, размытие, номера.</summary>
[Collection(LanguageCollection.Name)]
public class AnnotateTests
{
    private static Annotation Arrow(double x1, double y1, double x2, double y2) =>
        new(AnnotationTool.Arrow, [new ImagePoint(x1, y1), new ImagePoint(x2, y2)], AnnotationColors.Red, StrokeSize.Medium);

    private static Annotation Number(int n) =>
        new(AnnotationTool.Number, [new ImagePoint(10 * n, 10)], AnnotationColors.Blue, StrokeSize.Medium) { Number = n };

    // ---------- каталог и колесо ----------

    [Fact]
    public void Annotate_is_an_image_tool_for_one_file_with_png_result()
    {
        var op = OperationCatalog.Find(ImageAnnotate.Id)!;
        Assert.Equal((OperationMode.Tool, MediaKind.Image, 1), (op.Mode, op.Kind, op.MaxFiles));
        Assert.Equal("png", op.Output.Resolve(".jpg"));
        Assert.Equal("png", op.Output.Resolve(".heic"));
        Assert.Equal(" (пометки)", op.Suffix);
        Assert.Equal("Пометить…", op.Label);
        Assert.True(op.NeedsValues); // колесо открывает окно, а не ставит задачу
        Assert.Contains(ImageAnnotate.Id, OperationCatalog.ForFiles(["Мои фото/тест 1.heic"]).Select(o => o.Id));
        Assert.DoesNotContain(ImageAnnotate.Id, OperationCatalog.ForFiles(["a.jpg", "b.jpg"]).Select(o => o.Id));
        Assert.DoesNotContain(ImageAnnotate.Id, OperationCatalog.ForFile("clip.mp4").Select(o => o.Id));
        Assert.DoesNotContain(ImageAnnotate.Id, FolderPlan.Offer(["a.jpg", "b.png"]).Select(o => o.Id));
        using (Loc.Use(UiLanguage.En))
        {
            Assert.Equal("Annotate…", Loc.Label(op));
            Assert.Equal(" (annotated)", Loc.Suffix(op));
        }
    }

    [Fact]
    public void Half_size_is_hidden_by_default_and_annotate_fits_the_wheel()
    {
        var ops = OperationCatalog.ForFiles(["photo.png"]);
        Assert.True(OperationCatalog.Find("img-half")!.HiddenByDefault);
        var (_, tools) = WheelArrangement.Arrange(ops, WheelGroups.Photo, new WheelPreferences());
        Assert.Equal(WheelGeometry.MaxSectors, tools.Count);
        Assert.Equal(ImageAnnotate.Id, tools[5].Id); // сразу после «Оформить…»
        Assert.DoesNotContain(tools, t => t.Id == "img-half");

        // Пользователь показал «Уменьшить ×2» — он снова на колесе, девятый (последний) уходит за край.
        var prefs = new WheelPreferences().WithLayout(WheelGroups.Photo, OperationMode.Tool,
            WheelArrangement.Ordered(ops, OperationMode.Tool, null).Select(e => e.Operation.Id).ToList(), []);
        var (_, shown) = WheelArrangement.Arrange(ops, WheelGroups.Photo, prefs);
        Assert.Contains(shown, t => t.Id == "img-half");
        Assert.Equal(WheelGeometry.MaxSectors, shown.Count);
    }

    [Fact]
    public async Task Cli_says_annotate_works_only_in_the_window()
    {
        var ex = await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "Мои фото/тест 1.png", ImageAnnotate.Id], new StringWriter(), new StringWriter()));
        Assert.Contains(ImageAnnotate.Id, ex.Message);
    }

    [Fact]
    public void Load_args_keep_full_size_and_orientation()
    {
        var args = ImageAnnotate.LoadArgs("/data/Мои фото/тест 1.heic", "/tmp/x.png");
        Assert.Equal(["/data/Мои фото/тест 1.heic[0]", "-auto-orient", "-print", "%w %h\n", "-depth", "8", "/tmp/x.png"], args);
        Assert.DoesNotContain("-resize", args);
    }

    // ---------- история ----------

    [Fact]
    public void Undo_redo_and_delete_last()
    {
        var doc = new AnnotationDocument();
        var changes = 0;
        doc.Changed += () => changes++;
        Assert.False(doc.CanUndo);
        Assert.False(doc.Undo());

        var a = Arrow(0, 0, 50, 50);
        var b = Arrow(10, 10, 80, 20);
        doc.Add(a);
        doc.Add(b);
        Assert.Equal([a, b], doc.Shapes);
        Assert.True(doc.Undo());
        Assert.Equal([a], doc.Shapes);
        Assert.True(doc.CanRedo);
        Assert.True(doc.Redo());
        Assert.Equal([a, b], doc.Shapes);
        Assert.False(doc.Redo());

        Assert.True(doc.RemoveLast());
        Assert.Equal([a], doc.Shapes);
        Assert.True(doc.Undo()); // удаление тоже отменяется
        Assert.Equal([a, b], doc.Shapes);

        doc.Undo();
        doc.Add(Number(1)); // новое изменение после отмены стирает «повторы»
        Assert.False(doc.CanRedo);
        Assert.Equal(8, changes);

        var empty = new AnnotationDocument();
        Assert.False(empty.RemoveLast());
    }

    [Fact]
    public void Replace_moves_a_shape_as_one_step()
    {
        var doc = new AnnotationDocument();
        var a = Arrow(0, 0, 50, 50);
        doc.Add(a);
        doc.Replace(0, a.Offset(5, -5));
        Assert.Equal(new ImagePoint(5, -5), doc.Shapes[0].Start);
        Assert.Equal(new ImagePoint(55, 45), doc.Shapes[0].End);
        doc.Undo();
        Assert.Equal(a, doc.Shapes[0]);
        doc.Replace(3, a); // нет такой — ничего
        Assert.Single(doc.Shapes);
    }

    [Fact]
    public void History_is_limited()
    {
        var doc = new AnnotationDocument();
        for (var i = 0; i < AnnotationDocument.MaxHistory + 50; i++) doc.Add(Number(i + 1));
        var undone = 0;
        while (doc.Undo()) undone++;
        Assert.Equal(AnnotationDocument.MaxHistory, undone);
        Assert.Equal(50, doc.Shapes.Count);
    }

    [Fact]
    public void Unsaved_changes_ask_before_closing()
    {
        var doc = new AnnotationDocument();
        Assert.False(doc.HasUnsavedChanges); // пусто — закрывается без вопроса
        doc.Add(Arrow(0, 0, 50, 50));
        Assert.True(doc.HasUnsavedChanges);
        doc.MarkSaved();
        Assert.False(doc.HasUnsavedChanges);
        doc.Add(Number(1));
        Assert.True(doc.HasUnsavedChanges);
        doc.Undo(); // вернулись к сохранённому
        Assert.False(doc.HasUnsavedChanges);
        doc.Undo(); // всё отменили — пусто
        Assert.False(doc.HasUnsavedChanges);
    }

    [Fact]
    public void Number_counter_follows_the_shapes()
    {
        var doc = new AnnotationDocument();
        Assert.Equal(1, doc.NextNumber);
        doc.Add(Number(doc.NextNumber));
        doc.Add(Arrow(0, 0, 9, 9));
        doc.Add(Number(doc.NextNumber));
        Assert.Equal(3, doc.NextNumber);
        doc.Undo(); // убрали «2» — следующий снова 2
        Assert.Equal(2, doc.NextNumber);
        doc.Undo();
        doc.Undo();
        Assert.Equal(1, doc.NextNumber);
    }

    // ---------- фигуры ----------

    [Fact]
    public void Click_without_drag_makes_nothing()
    {
        Assert.False(Arrow(10, 10, 11, 11).IsMeaningful());
        Assert.True(Arrow(10, 10, 30, 10).IsMeaningful());
        var rect = new Annotation(AnnotationTool.Rectangle, [new(10, 10), new(60, 11)], AnnotationColors.Red, StrokeSize.Thin);
        Assert.False(rect.IsMeaningful()); // линия в 1 px — не рамка
        Assert.True((rect with { Points = [new(10, 10), new(60, 40)] }).IsMeaningful());
        var text = new Annotation(AnnotationTool.Text, [new(5, 5)], AnnotationColors.Black, StrokeSize.Medium) { Text = "  " };
        Assert.False(text.IsMeaningful());
        Assert.True((text with { Text = "Нажмите сюда" }).IsMeaningful());
        Assert.True(new Annotation(AnnotationTool.Pen, [new(1, 1)], AnnotationColors.Green, StrokeSize.Thick).IsMeaningful());
        Assert.True(Number(1).IsMeaningful());
        Assert.False(Number(0).IsMeaningful());
    }

    [Theory]
    [InlineData(100, 3, 100, 0)]     // почти горизонтально → ровно
    [InlineData(50, 48, 49.0, 49.0)] // почти 45°
    [InlineData(-4, 80, 0, 80)]      // почти вертикально вниз
    public void Shift_snaps_arrow_to_45_degrees(double dx, double dy, double ex, double ey)
    {
        var end = AnnotationGeometry.Constrain(AnnotationTool.Arrow, new ImagePoint(0, 0), new ImagePoint(dx, dy));
        Assert.Equal(ex, end.X, 0);
        Assert.Equal(ey, end.Y, 0);
        Assert.Equal(Math.Sqrt(dx * dx + dy * dy), Math.Sqrt(end.X * end.X + end.Y * end.Y), 3); // длина та же
    }

    [Fact]
    public void Shift_makes_squares_in_the_drag_direction()
    {
        var start = new ImagePoint(100, 100);
        Assert.Equal(new ImagePoint(150, 150), AnnotationGeometry.Constrain(AnnotationTool.Rectangle, start, new ImagePoint(150, 120)));
        Assert.Equal(new ImagePoint(40, 160), AnnotationGeometry.Constrain(AnnotationTool.Ellipse, start, new ImagePoint(70, 160)));
        Assert.Equal(new ImagePoint(30, 30), AnnotationGeometry.Constrain(AnnotationTool.Blur, start, new ImagePoint(30, 90)));
        Assert.Equal(new ImagePoint(7, 9), AnnotationGeometry.Constrain(AnnotationTool.Pen, start, new ImagePoint(7, 9)));
    }

    [Fact]
    public void Arrow_head_points_to_the_end()
    {
        var (shaft, left, right) = AnnotationGeometry.Arrow(new ImagePoint(0, 0), new ImagePoint(100, 0), stroke: 4);
        // Наконечник 16 px (4 × толщина): основание на x = 84, крылья симметричны.
        Assert.Equal(84, left.X, 6);
        Assert.Equal(84, right.X, 6);
        Assert.Equal(-left.Y, right.Y, 6);
        Assert.True(Math.Abs(left.Y) > 6);
        Assert.InRange(shaft.X, 84, 100);
        // Короткая стрелка: наконечник не длиннее 40 % её длины.
        var (_, l2, _) = AnnotationGeometry.Arrow(new ImagePoint(0, 0), new ImagePoint(20, 0), stroke: 10);
        Assert.Equal(12, l2.X, 6);
    }

    [Fact]
    public void Sizes_scale_with_the_picture()
    {
        Assert.Equal(4, AnnotationMetrics.Stroke(StrokeSize.Medium, 1920, 1000));
        Assert.Equal(2.4, AnnotationMetrics.Stroke(StrokeSize.Thin, 1920, 1000), 6);
        Assert.Equal(7.2, AnnotationMetrics.Stroke(StrokeSize.Thick, 1920, 1000), 6);
        Assert.Equal(2, AnnotationMetrics.Base(300, 200)); // не меньше 2
        Assert.Equal(40, AnnotationMetrics.Base(20000, 20000)); // не больше 40
        Assert.True(AnnotationMetrics.Stroke(StrokeSize.Thin, 100, 100) >= 1.5);
        Assert.True(AnnotationMetrics.FontSize(StrokeSize.Thin, 100, 100) >= 12);
        Assert.True(AnnotationMetrics.Marker(StrokeSize.Medium, 1920, 1080) > AnnotationMetrics.Stroke(StrokeSize.Thick, 1920, 1080));
        Assert.True(AnnotationMetrics.NumberRadius(StrokeSize.Medium, 1920, 1080) > AnnotationMetrics.Stroke(StrokeSize.Thick, 1920, 1080));
    }

    [Fact]
    public void Colors_parse_and_light_ones_get_dark_outline()
    {
        Assert.Equal(8, AnnotationColors.All.Count);
        Assert.Equal(AnnotationColors.All.Count, AnnotationColors.All.Distinct().Count());
        Assert.Equal(((byte)0xE8, (byte)0x11, (byte)0x23), AnnotationColors.Parse(AnnotationColors.Red));
        Assert.Equal(AnnotationColors.Parse(AnnotationColors.Red), AnnotationColors.Parse("pink"));
        Assert.True(AnnotationColors.IsLight(AnnotationColors.Yellow));
        Assert.True(AnnotationColors.IsLight(AnnotationColors.White));
        Assert.False(AnnotationColors.IsLight(AnnotationColors.Red));
        Assert.False(AnnotationColors.IsLight(AnnotationColors.Blue));
        Assert.False(AnnotationColors.IsLight(AnnotationColors.Black));
    }

    // ---------- окно ↔ картинка ----------

    [Fact]
    public void Picture_is_fitted_and_centered()
    {
        var m = ViewMapping.Fit(4000, 2000, 800, 600);
        Assert.Equal(0.2, m.Scale, 9);
        Assert.Equal((0.0, 100.0, 800.0, 400.0), m.Shown);
        Assert.Equal(new ImagePoint(2000, 1000), m.ToImage(400, 300));
        Assert.Equal((0.0, 100.0), m.ToView(new ImagePoint(0, 0)));
        var p = new ImagePoint(1234.5, 678.25);
        var (vx, vy) = m.ToView(p);
        Assert.Equal(p.X, m.ToImage(vx, vy).X, 6);
        Assert.Equal(p.Y, m.ToImage(vx, vy).Y, 6);
    }

    [Fact]
    public void Small_picture_is_not_stretched_beyond_screen_pixels()
    {
        // Снимок 300 × 200 на экране 150 %: не больше 1/1.5 DIP на пиксель — пиксель в пиксель.
        var m = ViewMapping.Fit(300, 200, 900, 600, maxScale: 1 / 1.5);
        Assert.Equal(1 / 1.5, m.Scale, 9);
        Assert.Equal((350.0, 233.33), (Math.Round(m.Shown.X, 2), Math.Round(m.Shown.Y, 2)));
        Assert.Equal(1, ViewMapping.Fit(300, 200, 900, 600).Scale);
    }

    [Fact]
    public void Points_outside_are_clamped_to_the_picture()
    {
        var m = ViewMapping.Fit(1000, 500, 1000, 500);
        Assert.False(m.Contains(m.ToImage(-5, 10)));
        Assert.Equal(new ImagePoint(0, 10), m.Clamp(m.ToImage(-5, 10)));
        Assert.Equal(new ImagePoint(1000, 500), m.Clamp(new ImagePoint(4000, 900)));
        Assert.True(m.Contains(new ImagePoint(1000, 500)));
        Assert.Equal(1, ViewMapping.Fit(0, 0, 100, 100).Scale); // картинки ещё нет — без деления на ноль
    }

    // ---------- размытие ----------

    [Fact]
    public void Blur_region_is_whole_pixels_inside_the_picture()
    {
        Assert.Equal(new PixelRect(10, 20, 31, 11), AnnotationGeometry.Region(new ImagePoint(40.2, 30.5), new ImagePoint(10.7, 20.1), 100, 100));
        Assert.Equal(new PixelRect(90, 0, 10, 15), AnnotationGeometry.Region(new ImagePoint(90, -10), new ImagePoint(130, 15), 100, 100));
        Assert.Null(AnnotationGeometry.Region(new ImagePoint(10, 10), new ImagePoint(11, 50), 100, 100));
        Assert.Null(AnnotationGeometry.Region(new ImagePoint(150, 150), new ImagePoint(200, 200), 100, 100)); // за краем
    }

    [Fact]
    public void Block_size_depends_on_picture_and_region()
    {
        Assert.Equal(14, Pixelate.BlockSize(1920, 1080, 500, 300));
        Assert.Equal(8, Pixelate.BlockSize(400, 300, 200, 100));
        Assert.Equal(64, Pixelate.BlockSize(10000, 8000, 3000, 3000));
        Assert.Equal(5, Pixelate.BlockSize(1920, 1080, 10, 40)); // узкая полоса — хотя бы две клетки
        Assert.Equal((4, 3), Pixelate.Grid(50, 30, 14));
    }

    [Fact]
    public void Pixelate_fills_cells_with_their_average()
    {
        // 4 × 2, клетка 2: левая клетка — чёрный и белый пополам → серый; правая — красная.
        const int w = 4, h = 2, stride = w * 4 + 8; // строка с запасом, как у настоящих картинок
        var src = new byte[stride * h];
        void Set(int x, int y, byte b, byte g, byte r, byte a = 255)
        {
            var i = y * stride + x * 4;
            (src[i], src[i + 1], src[i + 2], src[i + 3]) = (b, g, r, a);
        }
        Set(0, 0, 0, 0, 0); Set(1, 0, 255, 255, 255); Set(0, 1, 0, 0, 0); Set(1, 1, 255, 255, 255);
        Set(2, 0, 0, 0, 255); Set(3, 0, 0, 0, 255); Set(2, 1, 0, 0, 255); Set(3, 1, 0, 0, 255);

        var output = Pixelate.Apply(src, w, h, stride, 2);
        Assert.Equal(w * h * 4, output.Length);
        for (var y = 0; y < h; y++)
        {
            Assert.Equal(new byte[] { 128, 128, 128, 255 }, output[(y * w * 4)..(y * w * 4 + 4)]);
            Assert.Equal(new byte[] { 128, 128, 128, 255 }, output[(y * w * 4 + 4)..(y * w * 4 + 8)]);
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, output[(y * w * 4 + 8)..(y * w * 4 + 12)]);
        }
    }

    [Fact]
    public void Pixelate_handles_partial_cells_and_transparency()
    {
        // 3 × 1, клетка 2: последняя клетка — неполная (1 px). Прозрачный пиксель не тянет цвет к чёрному.
        var src = new byte[] { 0, 0, 255, 255, 0, 0, 0, 0, 10, 20, 30, 255 };
        var output = Pixelate.Apply(src, 3, 1, 12, 2);
        Assert.Equal(new byte[] { 0, 0, 255, 128, 0, 0, 255, 128, 10, 20, 30, 255 }, output);
        Assert.Empty(Pixelate.Apply([], 0, 0, 0, 2));
        Assert.Throws<ArgumentException>(() => Pixelate.Apply(new byte[8], 3, 1, 12, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Pixelate.Apply(src, 3, 1, 12, 0));
    }
}
