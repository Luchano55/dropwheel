using DropWheel.Conversions;

namespace DropWheel.Tests;

public class CatalogTests
{
    private static IEnumerable<string> Ids(IEnumerable<Operation> ops) => ops.Select(o => o.Id);

    [Fact]
    public void Ids_are_unique()
    {
        var ids = Ids(OperationCatalog.All).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Wheel_has_at_most_8_sectors_per_mode()
    {
        // Считаем по каждому расширению: операции «для любых файлов» (В ZIP) добавляются к операциям типа.
        foreach (var ext in Extensions.All.Append("txt"))
            foreach (var group in OperationCatalog.ForFile("file." + ext).GroupBy(o => o.Mode))
                Assert.True(group.Count() <= 8, $"{ext} {group.Key}: {group.Count()}");
    }

    [Fact]
    public void Heic_gets_all_image_conversions()
    {
        var ids = Ids(OperationCatalog.ForFile("photo.heic"));
        Assert.Equal(["to-jpg", "to-png", "to-webp", "to-avif", "to-pdf"],
            ids.Where(i => i.StartsWith("to-")));
        Assert.Contains("img-compress", ids);
    }

    [Theory]
    [InlineData("photo.jpg", "to-jpg")]
    [InlineData("photo.JPEG", "to-jpg")]
    [InlineData("song.mp3", "to-mp3")]
    [InlineData("clip.mp4", "to-mp4")]
    [InlineData("clip.webm", "to-webm")]
    public void Same_format_conversion_is_not_offered(string file, string hiddenOp)
    {
        Assert.DoesNotContain(hiddenOp, Ids(OperationCatalog.ForFile(file)));
    }

    [Fact]
    public void Same_format_tools_are_offered()
    {
        Assert.Contains("img-compress", Ids(OperationCatalog.ForFile("photo.jpg")));
        Assert.Contains("video-mute", Ids(OperationCatalog.ForFile("clip.mp4")));
    }

    [Theory]
    [InlineData("photo.HEIC", MediaKind.Image)]
    [InlineData("clip.MoV", MediaKind.Video)]
    [InlineData("song.opus", MediaKind.Audio)]
    public void Extension_case_does_not_matter(string file, MediaKind kind)
    {
        var ops = OperationCatalog.ForFile(file);
        Assert.Contains(ops, o => o.Kind == kind);
        Assert.All(ops, o => Assert.True(o.Kind == kind || o.Kind == MediaKind.Any, o.Id));
    }

    [Theory]
    [InlineData("readme.txt")]
    [InlineData("noextension")]
    [InlineData(".gitignore")]
    public void Unknown_file_gets_only_zip(string file)
    {
        Assert.Equal(["zip-pack"], Ids(OperationCatalog.ForFile(file)));
    }

    [Fact]
    public void Zip_archive_can_be_extracted()
    {
        var ids = Ids(OperationCatalog.ForFile("photos.ZIP")).ToList();
        Assert.Contains("zip-extract", ids);
        Assert.Contains("zip-pack", ids);
        Assert.DoesNotContain("zip-extract", Ids(OperationCatalog.ForFile("photo.jpg")));
    }

    [Fact]
    public void Zip_operations_shape()
    {
        var pack = OperationCatalog.Find("zip-pack")!;
        Assert.Equal((OperationMode.Tool, MediaKind.Any, InputMode.Combine, ResultKind.File, 1),
            (pack.Mode, pack.Kind, pack.Inputs, pack.Result, pack.MinFiles));
        var extract = OperationCatalog.Find("zip-extract")!;
        Assert.Equal((OperationMode.Tool, MediaKind.Archive, InputMode.PerFile, ResultKind.Folder, ""),
            (extract.Mode, extract.Kind, extract.Inputs, extract.Result, extract.Suffix));
    }

    [Fact]
    public void Several_images_get_common_operations()
    {
        var ids = Ids(OperationCatalog.ForFiles(["a.png", "b.heic"])).ToList();
        Assert.Contains("to-jpg", ids);
        Assert.DoesNotContain("to-png", ids); // a.png уже PNG

        Assert.DoesNotContain("to-jpg", Ids(OperationCatalog.ForFiles(["a.jpg", "b.heic"])));
    }

    [Fact]
    public void Mixed_types_have_only_zip_in_common()
    {
        Assert.Equal(["zip-pack"], Ids(OperationCatalog.ForFiles(["a.png", "b.mp4", "c.txt"])));
        Assert.Empty(OperationCatalog.ForFiles(Array.Empty<string>()));
    }

    [Fact]
    public void Find_is_case_insensitive()
    {
        Assert.Equal("to-jpg", OperationCatalog.Find("TO-JPG")?.Id);
        Assert.Null(OperationCatalog.Find("to-nothing"));
    }

    [Theory]
    [InlineData("img-compress", "photo.png", "jpg")]
    [InlineData("img-compress", "photo.webp", "webp")]
    [InlineData("img-half", "photo.png", "png")]
    [InlineData("img-half", "photo.heic", "jpg")]
    [InlineData("video-mute", "clip.mkv", "mkv")]
    [InlineData("video-compress", "clip.mov", "mp4")]
    [InlineData("audio-normalize", "song.flac", "flac")]
    [InlineData("to-avif", "photo.png", "avif")]
    public void Output_extension(string opId, string input, string expected)
    {
        var op = OperationCatalog.Find(opId)!;
        Assert.Equal(expected, op.Output.Resolve(Path.GetExtension(input)));
    }
}
