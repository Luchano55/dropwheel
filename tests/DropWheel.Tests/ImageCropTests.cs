using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Wheel;

namespace DropWheel.Tests;

/// <summary>Обрезка картинки (D-CROP): каталог и колесо, аргументы ImageMagick, область WxH+X+Y, подрезка, рамка окна.</summary>
public class ImageCropTests
{
    private const string In = "/data/Мои фото/тест 1.jpg";
    private static readonly ConversionSettings S = ConversionSettings.Default;

    // ---------- каталог и колесо ----------

    [Fact]
    public void Crop_is_an_image_tool_for_one_file_only()
    {
        Assert.Contains(ImageCrop.Id, OperationCatalog.ForFile("photo.heic").Where(o => o.Mode == OperationMode.Tool).Select(o => o.Id));
        Assert.Contains(ImageCrop.Id, OperationCatalog.ForFiles(["Мои фото/тест 1.png"]).Select(o => o.Id));
        Assert.DoesNotContain(ImageCrop.Id, OperationCatalog.ForFiles(["a.jpg", "b.jpg"]).Select(o => o.Id));
        Assert.DoesNotContain(ImageCrop.Id, OperationCatalog.ForFile("clip.mp4").Select(o => o.Id));
        Assert.DoesNotContain(ImageCrop.Id, FolderPlan.Offer(["a.jpg", "b.png"]).Select(o => o.Id));
    }

    [Fact]
    public void Default_image_tools_wheel_shows_crop_and_beautify_and_keeps_everything()
    {
        var ops = OperationCatalog.ForFiles(["photo.jpg"]);
        var (_, tools) = WheelArrangement.Arrange(ops, WheelGroups.Photo, new WheelPreferences());
        // «Уменьшить ×2» скрыт по умолчанию (D-ANNOTATE), но остаётся в списке настроек колеса.
        Assert.Equal(["img-compress", "img-1920", "img-strip", ImageCrop.Id, ImageBeautify.Id, ImageAnnotate.Id, "zip-pack", "pack-7z"], tools.Select(t => t.Id));
        Assert.Contains(WheelArrangement.Ordered(ops, OperationMode.Tool, null), e => e.Operation.Id == "img-half" && e.Hidden);
    }

    [Fact]
    public void Crop_keeps_raster_format_and_makes_jpg_from_others()
    {
        var op = OperationCatalog.Find(ImageCrop.Id)!;
        Assert.Equal("png", op.Output.Resolve(".png"));
        Assert.Equal("jpg", op.Output.Resolve(".JPG"));
        Assert.Equal("webp", op.Output.Resolve(".webp"));
        Assert.Equal("jpg", op.Output.Resolve(".heic"));
        Assert.Equal(" (обрезано)", op.Suffix);
        Assert.True(op.NeedsValues);
        Assert.Equal(["x", "y", "w", "h"], op.MissingValues());
    }

    // ---------- аргументы ----------

    [Fact]
    public void Args_crop_after_auto_orient_and_reset_the_page()
    {
        var args = ArgumentBuilder.Build(ImageCrop.Bind(new CropRect(100, 50, 800, 600)), In, "/data/out.png", S with { JpegQuality = 88 }).ToList();
        Assert.Equal([In + "[0]", "-auto-orient", "-crop", "800x600+100+50", "+repage", "-quality", "88", "/data/out.png"], args);
    }

    [Fact]
    public void Unbound_crop_is_a_conversion_error()
    {
        var ex = Assert.Throws<ConversionException>(() => ArgumentBuilder.Build(OperationCatalog.Find(ImageCrop.Id)!, In, "/data/out.jpg", S));
        Assert.Contains("x, y, w, h", ex.Message);
    }

    // ---------- область ----------

    [Theory]
    [InlineData("800x600+100+50", 100, 50, 800, 600)]
    [InlineData("800X600", 0, 0, 800, 600)]
    [InlineData(" 1 x 1 + 0 + 0 ", 0, 0, 1, 1)]
    [InlineData("800х600+1+2", 1, 2, 800, 600)] // русская «х»
    [InlineData("800×600+1+2", 1, 2, 800, 600)]
    public void Geometry_parses(string text, int x, int y, int w, int h)
    {
        Assert.True(CropRect.TryParse(text, out var r));
        Assert.Equal(new CropRect(x, y, w, h), r);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("800")]
    [InlineData("0x600")]
    [InlineData("800x0+1+1")]
    [InlineData("800x600-10+0")]
    [InlineData("800x600+10")]
    [InlineData("80.5x600")]
    [InlineData("abc")]
    public void Geometry_rejects_garbage(string? text)
    {
        Assert.False(CropRect.TryParse(text, out _));
    }

    [Fact]
    public void Geometry_round_trips()
    {
        var r = new CropRect(3, 4, 50, 60);
        Assert.Equal("50x60+3+4", r.Geometry);
        Assert.True(CropRect.TryParse(r.Geometry, out var back));
        Assert.Equal(r, back);
    }

    [Fact]
    public void Bind_clamps_to_the_picture()
    {
        var op = ImageCrop.Bind(new CropRect(90, 40, 50, 50), 100, 80);
        Assert.Equal(new Dictionary<string, string> { ["x"] = "90", ["y"] = "40", ["w"] = "10", ["h"] = "40" }, op.Values);
        Assert.Throws<ArgumentException>(() => ImageCrop.Bind(new CropRect(100, 0, 10, 10), 100, 80));
        Assert.Throws<ArgumentException>(() => ImageCrop.Bind(new CropRect(0, 0, 10, 10), 0, 80));
        Assert.Throws<ArgumentException>(() => ImageCrop.Bind(new CropRect(0, 0, 0, 10)));
        Assert.Null(OperationCatalog.Find(ImageCrop.Id)!.Values); // каталог не меняется
    }

    [Fact]
    public void Whole_picture_is_whole()
    {
        Assert.True(CropRect.Whole(100, 80).IsWhole(100, 80));
        Assert.False(new CropRect(0, 0, 99, 80).IsWhole(100, 80));
    }

    [Fact]
    public void Size_is_parsed_from_magick_output()
    {
        Assert.Equal((4032, 3024), ImageProbe.ParseSize("4032 3024\n"));
        Assert.Equal((10, 20), ImageProbe.ParseSize("\r\n10 20\r\n"));
        Assert.Null(ImageProbe.ParseSize(""));
        Assert.Null(ImageProbe.ParseSize("0 20"));
        Assert.Null(ImageProbe.ParseSize("abc"));
    }

    [Fact]
    public void Preview_args_auto_orient_print_size_and_shrink()
    {
        Assert.Equal([In + "[0]", "-auto-orient", "-print", "%w %h\n", "-resize", "1600x1600>", "/tmp/p.png"],
            ImageProbe.PreviewArgs(In, "/tmp/p.png"));
    }

    // ---------- пропорции ----------

    [Fact]
    public void Aspect_presets_follow_the_picture_orientation()
    {
        Assert.Null(ImageCrop.Aspect(CropAspect.Free, 400, 300));
        Assert.Equal(1, ImageCrop.Aspect(CropAspect.Square, 400, 300));
        Assert.Equal(4.0 / 3, ImageCrop.Aspect(CropAspect.FourThree, 400, 300));
        Assert.Equal(3.0 / 4, ImageCrop.Aspect(CropAspect.FourThree, 300, 400));
        Assert.Equal(9.0 / 16, ImageCrop.Aspect(CropAspect.SixteenNine, 300, 400));
        Assert.Equal(2.5, ImageCrop.Aspect(CropAspect.Original, 500, 200));
    }

    [Fact]
    public void Largest_rect_is_centered()
    {
        Assert.Equal(new CropRect(50, 0, 300, 300), ImageCrop.Largest(1, 400, 300));
        Assert.Equal(new CropRect(0, 37, 400, 225), ImageCrop.Largest(16.0 / 9, 400, 300));
        Assert.Equal(CropRect.Whole(400, 300), ImageCrop.Largest(null, 400, 300));
        Assert.Equal(CropRect.Whole(400, 300), ImageCrop.Largest(4.0 / 3, 400, 300));
    }

    // ---------- перетаскивание ----------

    private static readonly CropRect Start = new(100, 100, 200, 100);

    [Fact]
    public void Move_stays_inside()
    {
        Assert.Equal(new CropRect(110, 90, 200, 100), ImageCrop.Drag(Start, CropHandle.Move, 10, -10, 400, 300));
        Assert.Equal(new CropRect(200, 0, 200, 100), ImageCrop.Drag(Start, CropHandle.Move, 500, -500, 400, 300));
    }

    [Fact]
    public void Free_edges_and_corners()
    {
        Assert.Equal(new CropRect(80, 100, 220, 100), ImageCrop.Drag(Start, CropHandle.Left, -20, 99, 400, 300));
        Assert.Equal(new CropRect(100, 100, 300, 100), ImageCrop.Drag(Start, CropHandle.Right, 1000, 0, 400, 300));
        Assert.Equal(new CropRect(100, 0, 200, 200), ImageCrop.Drag(Start, CropHandle.Top, 0, -150, 400, 300));
        Assert.Equal(new CropRect(100, 100, 210, 120), ImageCrop.Drag(Start, CropHandle.BottomRight, 10, 20, 400, 300));
        Assert.Equal(new CropRect(0, 0, 300, 200), ImageCrop.Drag(Start, CropHandle.TopLeft, -999, -999, 400, 300));
    }

    [Fact]
    public void Edges_do_not_cross_and_keep_min_size()
    {
        Assert.Equal(new CropRect(299, 100, 1, 100), ImageCrop.Drag(Start, CropHandle.Left, 500, 0, 400, 300));
        Assert.Equal(new CropRect(276, 100, 24, 100), ImageCrop.Drag(Start, CropHandle.Left, 500, 0, 400, 300, min: 24));
        Assert.Equal(new CropRect(100, 100, 200, 24), ImageCrop.Drag(Start, CropHandle.Bottom, 0, -500, 400, 300, min: 24));
    }

    [Fact]
    public void Corner_with_aspect_keeps_ratio_and_opposite_corner()
    {
        var square = new CropRect(100, 100, 100, 100);
        var r = ImageCrop.Drag(square, CropHandle.BottomRight, 50, 10, 400, 300, aspect: 1);
        Assert.Equal(new CropRect(100, 100, 150, 150), r);
        // Упирается в низ картинки: 300 − 100 = 200.
        r = ImageCrop.Drag(square, CropHandle.BottomRight, 500, 500, 400, 300, aspect: 1);
        Assert.Equal(new CropRect(100, 100, 200, 200), r);
        r = ImageCrop.Drag(square, CropHandle.TopLeft, -500, -20, 400, 300, aspect: 1);
        Assert.Equal(new CropRect(0, 0, 200, 200), r);
    }

    [Fact]
    public void Edge_with_aspect_grows_around_the_center()
    {
        var r = ImageCrop.Drag(new CropRect(100, 100, 160, 90), CropHandle.Right, 32, 0, 400, 300, aspect: 16.0 / 9);
        Assert.Equal(new CropRect(100, 91, 192, 108), r);
        Assert.InRange(r.W / (double)r.H, 16.0 / 9 - 0.02, 16.0 / 9 + 0.02);
        // Сверху: ширина по пропорции, центр по горизонтали на месте.
        r = ImageCrop.Drag(new CropRect(100, 100, 100, 100), CropHandle.Top, 0, -50, 400, 300, aspect: 1);
        Assert.Equal(new CropRect(75, 50, 150, 150), r);
    }

    [Fact]
    public void Drag_never_leaves_the_picture()
    {
        var rnd = new Random(7);
        foreach (var handle in Enum.GetValues<CropHandle>())
            for (var i = 0; i < 200; i++)
            {
                double? aspect = (i % 3) switch { 0 => null, 1 => 1, _ => 16.0 / 9 };
                var r = ImageCrop.Drag(Start, handle, rnd.Next(-600, 600), rnd.Next(-600, 600), 400, 300, aspect, min: 5);
                Assert.True(r.X >= 0 && r.Y >= 0 && r.W >= 1 && r.H >= 1 && r.Right <= 400 && r.Bottom <= 300, $"{handle} {aspect}: {r}");
            }
    }
}
