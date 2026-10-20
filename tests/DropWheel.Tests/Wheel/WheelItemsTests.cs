using DropWheel.Conversions;
using DropWheel.Wheel;

namespace DropWheel.Tests.Wheel;

public class WheelItemsTests
{
    private static Operation Op(string id, OperationMode mode) =>
        new(id, id.ToUpperInvariant(), mode, MediaKind.Image, EngineKind.ImageMagick, "", OutputRule.Fixed("jpg"));

    [Fact]
    public void Items_are_filtered_by_mode_in_catalog_order()
    {
        var ops = OperationCatalog.ForFiles(["photo.heic"]); // как в приложении: «Один PDF» нужен от 2 файлов

        var convert = WheelItems.For(ops, OperationMode.Convert);
        Assert.Equal(["to-jpg", "to-png", "to-webp", "to-avif", "to-pdf", "img-ico"], convert.Select(i => i.Id));
        Assert.Equal("JPG", convert[0].Label);

        var tools = WheelItems.For(ops, OperationMode.Tool);
        Assert.Equal(["img-compress", "img-half", "img-1920", "img-strip", "zip-pack", "pack-7z"], tools.Select(i => i.Id));
        Assert.Equal("Сжать", tools[0].Label);
    }

    [Fact]
    public void At_most_8_items()
    {
        var ops = Enumerable.Range(1, 11).Select(i => Op($"c{i}", OperationMode.Convert))
            .Append(Op("t1", OperationMode.Tool))
            .ToList();

        var items = WheelItems.For(ops, OperationMode.Convert);
        Assert.Equal(8, items.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"c{i}"), items.Select(i => i.Id));
    }

    [Fact]
    public void Empty_when_nothing_fits()
    {
        Assert.Empty(WheelItems.For([], OperationMode.Convert));
        Assert.Empty(WheelItems.For(OperationCatalog.ForFile("readme.txt"), OperationMode.Convert));
        Assert.Empty(WheelItems.For([Op("c1", OperationMode.Convert)], OperationMode.Tool));
    }
}
