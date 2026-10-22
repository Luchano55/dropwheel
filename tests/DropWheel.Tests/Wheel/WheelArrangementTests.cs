using DropWheel.Conversions;
using DropWheel.Wheel;

namespace DropWheel.Tests.Wheel;

public class WheelArrangementTests
{
    private const string G = WheelGroups.Photo;

    private static Operation Op(string id, OperationMode mode = OperationMode.Convert) =>
        new(id, id.ToUpperInvariant(), mode, MediaKind.Image, EngineKind.ImageMagick, "", OutputRule.Fixed("jpg"));

    private static readonly IReadOnlyList<Operation> Ops =
        [Op("a"), Op("b"), Op("c"), Op("d"), Op("t1", OperationMode.Tool), Op("t2", OperationMode.Tool)];

    private static IEnumerable<string> Ids(IReadOnlyList<WheelItem> items) => items.Select(i => i.Id);

    [Fact]
    public void No_preferences_gives_catalog_order()
    {
        var (convert, tools) = WheelArrangement.Arrange(Ops, G, new WheelPreferences());
        Assert.Equal(["a", "b", "c", "d"], Ids(convert));
        Assert.Equal(["t1", "t2"], Ids(tools));
        Assert.Equal(WheelItems.For(Ops, OperationMode.Convert), convert);
    }

    [Fact]
    public void Saved_order_is_applied_and_new_ops_go_to_the_end()
    {
        // «d» в сохранённом порядке нет (появилась позже) — в конец; «zzz» больше нет в каталоге — пропускается.
        var prefs = new WheelPreferences().WithLayout(G, OperationMode.Convert, ["c", "zzz", "a", "b"], []);
        var (convert, tools) = WheelArrangement.Arrange(Ops, G, prefs);
        Assert.Equal(["c", "a", "b", "d"], Ids(convert));
        Assert.Equal(["t1", "t2"], Ids(tools));
    }

    [Fact]
    public void Order_is_per_group()
    {
        var prefs = new WheelPreferences().WithLayout(WheelGroups.Video, OperationMode.Convert, ["d", "c"], []);
        Assert.Equal(["a", "b", "c", "d"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Convert));
        Assert.Equal(["d", "c", "a", "b"], Ids(WheelArrangement.Arrange(Ops, WheelGroups.Video, prefs).Convert));
    }

    [Fact]
    public void Hidden_ops_are_excluded_but_kept_in_settings_list()
    {
        var prefs = new WheelPreferences().WithLayout(G, OperationMode.Convert, [], ["b", "D"]);
        Assert.Equal(["a", "c"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Convert));

        var entries = WheelArrangement.Ordered(Ops, OperationMode.Convert, prefs.For(G, OperationMode.Convert));
        Assert.Equal(["a", "b", "c", "d"], entries.Select(e => e.Operation.Id));
        Assert.Equal([false, true, false, true], entries.Select(e => e.Hidden));
    }

    [Fact]
    public void Hiding_a_whole_mode_is_allowed()
    {
        var prefs = new WheelPreferences().WithLayout(G, OperationMode.Tool, [], ["t1", "t2"]);
        var (convert, tools) = WheelArrangement.Arrange(Ops, G, prefs);
        Assert.Equal(4, convert.Count);
        Assert.Empty(tools);
    }

    [Fact]
    public void Everything_hidden_falls_back_to_defaults()
    {
        var prefs = new WheelPreferences()
            .WithLayout(G, OperationMode.Convert, ["d"], ["a", "b", "c", "d"])
            .WithLayout(G, OperationMode.Tool, [], ["t1", "t2"]);
        var (convert, tools) = WheelArrangement.Arrange(Ops, G, prefs);
        Assert.Equal(["a", "b", "c", "d"], Ids(convert));
        Assert.Equal(["t1", "t2"], Ids(tools));
    }

    [Fact]
    public void No_ops_stays_empty()
    {
        var (convert, tools) = WheelArrangement.Arrange([], G, new WheelPreferences());
        Assert.Empty(convert);
        Assert.Empty(tools);
    }

    [Fact]
    public void Recent_goes_first_only_when_enabled()
    {
        var prefs = new WheelPreferences().WithRecent(G, OperationMode.Convert, "c");
        Assert.Equal(["a", "b", "c", "d"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Convert));

        prefs = prefs with { MruFirst = true };
        Assert.Equal(["c", "a", "b", "d"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Convert));
        // У инструментов своё «последнее» — его нет, порядок прежний.
        Assert.Equal(["t1", "t2"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Tools));
    }

    [Fact]
    public void Recent_is_applied_after_custom_order_and_ignores_hidden()
    {
        var prefs = new WheelPreferences { MruFirst = true }
            .WithLayout(G, OperationMode.Convert, ["d", "c", "b", "a"], ["b"])
            .WithRecent(G, OperationMode.Convert, "a");
        Assert.Equal(["a", "d", "c"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Convert));

        prefs = prefs.WithRecent(G, OperationMode.Convert, "b"); // скрытая — не показываем
        Assert.Equal(["d", "c", "a"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Convert));

        prefs = prefs.WithRecent(G, OperationMode.Convert, "gone"); // нет среди доступных
        Assert.Equal(["d", "c", "a"], Ids(WheelArrangement.Arrange(Ops, G, prefs).Convert));
    }

    [Fact]
    public void Recent_beyond_eighth_still_comes_first_and_limit_is_8()
    {
        var many = Enumerable.Range(1, 11).Select(i => Op($"c{i}")).ToList();
        var prefs = new WheelPreferences { MruFirst = true }.WithRecent(G, OperationMode.Convert, "c10");
        var convert = WheelArrangement.Arrange(many, G, prefs).Convert;
        Assert.Equal(8, convert.Count);
        Assert.Equal(["c10", "c1", "c2", "c3", "c4", "c5", "c6", "c7"], Ids(convert));
    }

    [Fact]
    public void Same_recent_returns_same_object_and_reset_keeps_recent()
    {
        var prefs = new WheelPreferences().WithRecent(G, OperationMode.Convert, "c");
        Assert.Same(prefs, prefs.WithRecent(G, OperationMode.Convert, "c"));

        var custom = prefs.WithLayout(G, OperationMode.Convert, ["d"], ["a"]).WithLayout(G, OperationMode.Tool, ["t2"], []);
        var reset = custom.Reset(G);
        Assert.Equal("c", reset.For(G, OperationMode.Convert)!.Recent);
        Assert.False(reset.For(G, OperationMode.Convert)!.IsCustomized);
        Assert.Null(reset.For(G, OperationMode.Tool));
        Assert.Equal(prefs, reset);
        Assert.Equal(new WheelPreferences(), new WheelPreferences().WithLayout(G, OperationMode.Tool, ["t2"], []).Reset(G));
    }

    [Fact]
    public void Real_catalog_photo_wheel_with_preferences()
    {
        var ops = OperationCatalog.ForFiles(["Мои фото/тест 1.heic"]);
        var group = WheelGroups.ForFiles(["Мои фото/тест 1.heic"]);
        Assert.Equal(WheelGroups.Photo, group);
        var prefs = new WheelPreferences { MruFirst = true }
            .WithLayout(group, OperationMode.Convert, ["to-png", "to-jpg"], ["to-avif"])
            .WithRecent(group, OperationMode.Convert, "to-webp");
        var (convert, _) = WheelArrangement.Arrange(ops, group, prefs);
        Assert.Equal(["to-webp", "to-png", "to-jpg", "to-pdf", "img-ico"], Ids(convert));
    }
}

public class WheelGroupsTests
{
    [Theory]
    [InlineData("a.HEIC", WheelGroups.Photo)]
    [InlineData("a.mkv", WheelGroups.Video)]
    [InlineData("a.flac", WheelGroups.Audio)]
    [InlineData("a.pdf", WheelGroups.Pdf)]
    [InlineData("a.docx", WheelGroups.Word)]
    [InlineData("a.csv", WheelGroups.Excel)]
    [InlineData("a.pptx", WheelGroups.PowerPoint)]
    [InlineData("a.md", WheelGroups.Text)]
    [InlineData("a.7z", WheelGroups.Archive)]
    [InlineData("a.bin", WheelGroups.Any)]
    [InlineData("без расширения", WheelGroups.Any)]
    public void Group_by_extension(string file, string group) =>
        Assert.Equal(group, WheelGroups.ForFiles([file]));

    [Fact]
    public void Mixed_files_are_any()
    {
        Assert.Equal(WheelGroups.Photo, WheelGroups.ForFiles(["a.jpg", "b.png"]));
        Assert.Equal(WheelGroups.Any, WheelGroups.ForFiles(["a.jpg", "b.mp4"]));
        Assert.Equal(WheelGroups.Any, WheelGroups.ForFiles([]));
    }

    [Fact]
    public void Every_group_has_operations_and_every_operation_has_a_group()
    {
        foreach (var group in WheelGroups.All)
            Assert.NotEmpty(WheelGroups.Operations(group));
        var covered = WheelGroups.All.SelectMany(WheelGroups.Operations).Select(o => o.Id).ToHashSet();
        Assert.Equal(OperationCatalog.All.Select(o => o.Id).ToHashSet(), covered);
    }

    [Fact]
    public void Group_operations_are_a_superset_of_any_file_of_that_type()
    {
        foreach (var file in new[] { "a.jpg", "a.gif", "a.heic", "a.mov", "a.docx", "a.doc", "a.csv", "a.xlsx", "a.md", "a.zip", "a.bin" })
        {
            var group = WheelGroups.ForFiles([file]);
            var all = WheelGroups.Operations(group).Select(o => o.Id).ToHashSet();
            Assert.Subset(all, OperationCatalog.ForFile(file).Select(o => o.Id).ToHashSet());
        }
        Assert.Equal(["zip-pack", "pack-7z"], WheelGroups.Operations(WheelGroups.Any).Select(o => o.Id));
    }

    [Fact]
    public void Only_for_names_the_narrowed_extensions()
    {
        Assert.Null(WheelGroups.OnlyFor(OperationCatalog.Find("to-jpg")!, WheelGroups.Photo));
        Assert.Equal(["gif"], WheelGroups.OnlyFor(OperationCatalog.Find("gif-to-mp4")!, WheelGroups.Photo));
        Assert.Equal(["csv"], WheelGroups.OnlyFor(OperationCatalog.Find("csv-to-xlsx")!, WheelGroups.Excel));
        Assert.Null(WheelGroups.OnlyFor(OperationCatalog.Find("word-to-pdf")!, WheelGroups.Word));
        Assert.Null(WheelGroups.OnlyFor(OperationCatalog.Find("zip-pack")!, WheelGroups.Any));
    }
}
