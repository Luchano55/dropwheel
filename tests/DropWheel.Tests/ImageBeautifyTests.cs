using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Wheel;

namespace DropWheel.Tests;

/// <summary>«Оформить…» (D-BEAUTIFY): каталог и колесо, стиль и его разбор, размеры, аргументы ImageMagick.</summary>
[Collection(LanguageCollection.Name)]
public class ImageBeautifyTests
{
    private const string In = "/data/Мои фото/тест 1.jpg";
    private static readonly ConversionSettings S = ConversionSettings.Default;

    // ---------- каталог и колесо ----------

    [Fact]
    public void Beautify_is_an_image_tool_for_one_file_only()
    {
        Assert.Contains(ImageBeautify.Id, OperationCatalog.ForFile("photo.heic").Where(o => o.Mode == OperationMode.Tool).Select(o => o.Id));
        Assert.Contains(ImageBeautify.Id, OperationCatalog.ForFiles(["Мои фото/тест 1.png"]).Select(o => o.Id));
        Assert.DoesNotContain(ImageBeautify.Id, OperationCatalog.ForFiles(["a.jpg", "b.jpg"]).Select(o => o.Id));
        Assert.DoesNotContain(ImageBeautify.Id, OperationCatalog.ForFile("clip.mp4").Select(o => o.Id));
        Assert.DoesNotContain(ImageBeautify.Id, FolderPlan.Offer(["a.jpg", "b.png"]).Select(o => o.Id));
    }

    [Fact]
    public void Default_image_tools_wheel_has_eight_petals_with_beautify_after_crop()
    {
        var (_, tools) = WheelArrangement.Arrange(OperationCatalog.ForFiles(["photo.jpg"]), WheelGroups.Photo, new WheelPreferences());
        // «Уменьшить ×2» по умолчанию скрыт — на его месте «Пометить…» (D-ANNOTATE).
        Assert.Equal(["img-compress", "img-1920", "img-strip", ImageCrop.Id, ImageBeautify.Id, ImageAnnotate.Id, "zip-pack", "pack-7z"],
            tools.Select(t => t.Id));
        Assert.Equal(WheelGeometry.MaxSectors, tools.Count);

        // Несколько картинок: «Обрезать…» и «Оформить…» уходят, появляется «Один PDF».
        var (_, many) = WheelArrangement.Arrange(OperationCatalog.ForFiles(["a.jpg", "b.jpg"]), WheelGroups.Photo, new WheelPreferences());
        Assert.Equal(["img-compress", "img-1920", "img-strip", "images-to-pdf", "zip-pack", "pack-7z"], many.Select(t => t.Id));
    }

    [Fact]
    public void Result_is_always_png_with_a_mark()
    {
        var op = OperationCatalog.Find(ImageBeautify.Id)!;
        Assert.Equal("png", op.Output.Resolve(".jpg"));
        Assert.Equal("png", op.Output.Resolve(".png"));
        Assert.Equal("png", op.Output.Resolve(".heic"));
        Assert.Equal(" (оформлено)", op.Suffix);
        Assert.Equal("Оформить…", op.Label);
        Assert.True(op.NeedsValues);
        Assert.Equal(["mask", "pad", "shadow", "canvas", "bg"], op.MissingValues());
        using (Loc.Use(UiLanguage.En))
        {
            Assert.Equal("Beautify…", Loc.Label(op));
            Assert.Equal(" (styled)", Loc.Suffix(op));
        }
    }

    // ---------- стиль ----------

    [Fact]
    public void Style_round_trips_and_defaults_fill_the_gaps()
    {
        Assert.Equal("bg=sunset;pad=8;radius=12;shadow=1", BeautifyStyle.Default.Format());
        Assert.True(BeautifyStyle.TryParse("bg=sky;pad=10;radius=0;shadow=0", out var s));
        Assert.Equal(new BeautifyStyle("sky", 10, 0, false), s);
        Assert.True(BeautifyStyle.TryParse(s.Format(), out var again));
        Assert.Equal(s, again);

        Assert.True(BeautifyStyle.TryParse("", out var empty));
        Assert.Equal(BeautifyStyle.Default, empty);
        Assert.True(BeautifyStyle.TryParse(" BG = Transparent , pad=5% , shadow=off ", out var loose));
        Assert.Equal(new BeautifyStyle("none", 5, 12, false), loose);
        Assert.True(BeautifyStyle.TryParse("background=gray;padding=25;radius=100;shadow=yes", out var longKeys));
        Assert.Equal(new BeautifyStyle("grey", 25, 100, true), longKeys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("bg=pink")]
    [InlineData("pad=26")]
    [InlineData("pad=-1")]
    [InlineData("radius=101")]
    [InlineData("radius=abc")]
    [InlineData("shadow=maybe")]
    [InlineData("color=red")]
    [InlineData("sunset")]
    public void Bad_styles_are_rejected(string? text) => Assert.False(BeautifyStyle.TryParse(text, out _));

    [Fact]
    public void Every_background_has_a_canvas_and_labels_in_both_languages()
    {
        Assert.Equal(["sunset", "sky", "mint", "white", "grey", "dark", "none"], ImageBeautify.Backgrounds.Select(b => b.Key));
        Assert.Equal(3, ImageBeautify.Backgrounds.Count(b => b.IsGradient));
        Assert.Single(ImageBeautify.Backgrounds, b => b.IsTransparent);
        Assert.All(ImageBeautify.Backgrounds, b => Assert.Matches("^(gradient:#[0-9a-f]{6}-#[0-9a-f]{6}|xc:#[0-9a-f]{6}|xc:none)$", b.Canvas));
        var ru = ImageBeautify.Backgrounds.Select(ImageBeautify.Label).ToList();
        Assert.Equal(ru.Count, ru.Distinct().Count());
        using (Loc.Use(UiLanguage.En))
            Assert.All(ImageBeautify.Backgrounds, b => Assert.DoesNotMatch("[А-Яа-яЁё]", ImageBeautify.Label(b)));
    }

    // ---------- размеры ----------

    [Fact]
    public void Padding_is_a_share_of_the_shorter_side_and_output_grows_by_two_paddings()
    {
        var l = ImageBeautify.Measure(BeautifyStyle.Default, 1000, 500);
        Assert.Equal(40, l.Pad); // 8 % от 500
        Assert.Equal((1080, 580), (l.Width, l.Height));
        Assert.Equal(12, l.Radius);
        Assert.Equal(13, l.Blur); // 2,5 % от 500 = 12,5 → 13
        Assert.Equal(8, l.Offset);

        var none = ImageBeautify.Measure(BeautifyStyle.Default with { Padding = 0 }, 300, 200);
        Assert.Equal((300, 200), (none.Width, none.Height));
    }

    [Fact]
    public void Radius_and_shadow_are_clamped_for_tiny_and_huge_pictures()
    {
        var tiny = ImageBeautify.Measure(BeautifyStyle.Default with { Radius = 100 }, 30, 10);
        Assert.Equal(5, tiny.Radius);
        Assert.Equal(2, tiny.Blur);
        var huge = ImageBeautify.Measure(BeautifyStyle.Default, 20000, 10000);
        Assert.Equal(100, huge.Blur);
        Assert.Throws<ArgumentException>(() => ImageBeautify.Measure(BeautifyStyle.Default, 0, 10));
    }

    [Fact]
    public void Preview_style_scales_the_radius_only()
    {
        var full = BeautifyStyle.Default with { Radius = 40 };
        Assert.Equal(full with { Radius = 10 }, ImageBeautify.ForPreview(full, 4000, 3000, 1000, 750));
        Assert.Equal(full, ImageBeautify.ForPreview(full, 800, 600, 800, 600));
    }

    // ---------- аргументы ----------

    [Fact]
    public void Args_are_one_magick_call_with_mask_shadow_and_canvas()
    {
        var op = ImageBeautify.Bind(BeautifyStyle.Default, 1000, 500);
        var args = ArgumentBuilder.Build(op, In, "/data/out.png", S).ToList();
        Assert.Equal(
        [
            In + "[0]", "-auto-orient", "-colorspace", "sRGB", "+repage", "-alpha", "set",
            "(", "+clone", "-alpha", "transparent", "-fill", "white", "-draw", "roundrectangle 0,0 999,499 12,12", ")",
            "-compose", "DstIn", "-composite",
            "-repage", "+40+40",
            "(", "+clone", "-background", "black", "-shadow", "40x13+0+8", ")", "+swap",
            "(", "-size", "1080x580", "-define", "gradient:angle=135", "gradient:#ff9a8b-#a18cd1", ")", "-insert", "0",
            "-background", "none", "-compose", "Over", "-layers", "flatten", "-depth", "8",
            "/data/out.png",
        ], args);
    }

    [Fact]
    public void No_shadow_and_transparent_background_keep_the_same_command()
    {
        var op = ImageBeautify.Bind(new BeautifyStyle("none", 0, 0, false), 64, 48);
        var args = ArgumentBuilder.Build(op, In, "/data/out.png", S).ToList();
        Assert.Contains("0x1+0+0", args);
        Assert.Contains("xc:none", args);
        Assert.Contains("64x48", args);
        Assert.Contains("+0+0", args);
        Assert.Contains("rectangle 0,0 63,47", args); // радиус 0 — обычный прямоугольник
    }

    [Fact]
    public void Unbound_beautify_is_a_conversion_error()
    {
        var ex = Assert.Throws<ConversionException>(() =>
            ArgumentBuilder.Build(OperationCatalog.Find(ImageBeautify.Id)!, In, "/data/out.png", S));
        Assert.Contains("mask, pad, shadow, canvas, bg", ex.Message);
    }

    [Fact]
    public void Unknown_background_in_a_hand_made_style_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => ImageBeautify.Values(new BeautifyStyle("pink", 8, 12, true), 10, 10));
    }
}
