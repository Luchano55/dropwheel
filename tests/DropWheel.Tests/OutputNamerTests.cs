using DropWheel.Jobs;

namespace DropWheel.Tests;

public class OutputNamerTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "Мои фото");

    [Fact]
    public void Next_to_source_with_new_extension()
    {
        Assert.Equal(Path.Combine(Dir, "тест 1.jpg"),
            OutputNamer.DesiredPath(Path.Combine(Dir, "тест 1.heic"), "jpg"));
    }

    [Fact]
    public void Suffix_goes_before_extension()
    {
        Assert.Equal(Path.Combine(Dir, "clip (720p).mp4"),
            OutputNamer.DesiredPath(Path.Combine(Dir, "clip.mov"), ".mp4", " (720p)"));
    }

    [Fact]
    public void Shared_output_folder()
    {
        var shared = Path.Combine(Path.GetTempPath(), "Загрузки", "DropWheel");
        Assert.Equal(Path.Combine(shared, "photo.jpg"),
            OutputNamer.DesiredPath(Path.Combine(Dir, "photo.heic"), "jpg", outputFolder: shared));
    }

    [Fact]
    public void Dots_in_name_are_kept()
    {
        Assert.Equal(Path.Combine(Dir, "my.photo.v2.png"),
            OutputNamer.DesiredPath(Path.Combine(Dir, "my.photo.v2.jpg"), "png"));
    }

    [Fact]
    public void Free_name_is_returned_as_is()
    {
        var p = Path.Combine(Dir, "photo.jpg");
        Assert.Equal(p, OutputNamer.FirstFree(p, _ => false));
    }

    [Fact]
    public void Busy_names_get_numbers()
    {
        var taken = new HashSet<string>
        {
            Path.Combine(Dir, "photo.jpg"),
            Path.Combine(Dir, "photo (1).jpg"),
        };
        Assert.Equal(Path.Combine(Dir, "photo (2).jpg"),
            OutputNamer.FirstFree(Path.Combine(Dir, "photo.jpg"), taken.Contains));
    }

    [Fact]
    public void Source_itself_is_never_the_target()
    {
        using var dir = new TempDir();
        var src = dir.File("photo.jpg", "исходник");
        var target = OutputNamer.FirstFree(OutputNamer.DesiredPath(src, "jpg", ""));
        Assert.NotEqual(src, target);
        Assert.Equal(Path.Combine(dir.Path, "photo (1).jpg"), target);
    }
}
