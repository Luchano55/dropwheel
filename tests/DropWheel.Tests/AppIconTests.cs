namespace DropWheel.Tests;

/// <summary>Значок программы (src/DropWheel/Assets/DropWheel.ico) содержит все нужные Windows размеры, 32 бита с прозрачностью.</summary>
public class AppIconTests
{
    [Fact]
    public void Icon_has_all_sizes_from_16_to_256()
    {
        var path = Path.Combine(RepoRoot(), "src", "DropWheel", "Assets", "DropWheel.ico");
        var data = File.ReadAllBytes(path);

        Assert.Equal(0, BitConverter.ToUInt16(data, 0));
        Assert.Equal(1, BitConverter.ToUInt16(data, 2)); // 1 = значок
        var count = BitConverter.ToUInt16(data, 4);

        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + 16 * i;
            var width = data[entry] == 0 ? 256 : data[entry];
            var bpp = BitConverter.ToUInt16(data, entry + 6);
            var length = BitConverter.ToInt32(data, entry + 8);
            var offset = BitConverter.ToInt32(data, entry + 12);
            Assert.Equal(32, bpp);
            Assert.True(offset + length <= data.Length, $"запись {width}px за пределами файла");
            sizes.Add(width);
        }

        foreach (var size in new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 })
            Assert.Contains(size, sizes);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "DropWheel.sln"))) return dir.FullName;
        throw new DirectoryNotFoundException("Не найден корень репозитория (DropWheel.sln)");
    }
}
