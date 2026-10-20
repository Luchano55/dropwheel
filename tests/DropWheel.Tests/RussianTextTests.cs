using DropWheel.Localization;

namespace DropWheel.Tests;

public class RussianTextTests
{
    [Theory]
    [InlineData(1, "файл")]
    [InlineData(2, "файла")]
    [InlineData(4, "файла")]
    [InlineData(5, "файлов")]
    [InlineData(11, "файлов")]
    [InlineData(12, "файлов")]
    [InlineData(14, "файлов")]
    [InlineData(21, "файл")]
    [InlineData(22, "файла")]
    [InlineData(25, "файлов")]
    [InlineData(101, "файл")]
    [InlineData(111, "файлов")]
    [InlineData(0, "файлов")]
    public void Plural_forms(long n, string expected) =>
        Assert.Equal(expected, Russian.Plural(n, "файл", "файла", "файлов"));

    [Fact]
    public void Count_puts_number_before_word() =>
        Assert.Equal("3 файла", Russian.Count(3, "файл", "файла", "файлов"));

    [Theory]
    [InlineData(0, "0 Б")]
    [InlineData(812, "812 Б")]
    [InlineData(1024, "1 КБ")]
    [InlineData(65_536, "64 КБ")]
    [InlineData(1_153_434, "1,1 МБ")]
    [InlineData(10_380_902, "9,9 МБ")]
    [InlineData(12_582_912, "12 МБ")]
    [InlineData(3_650_722_202, "3,4 ГБ")]
    [InlineData(1_048_100, "1 МБ")] // 1023,5 КБ округляется до 1 МБ, а не «1 024 КБ»
    [InlineData(1_047_552, "1 023 КБ")]
    public void File_size_like_explorer(long bytes, string expected) =>
        Assert.Equal(expected, Russian.FileSize(bytes).Replace('\u00A0', ' '));
}
