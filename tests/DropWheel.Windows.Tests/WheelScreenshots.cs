using System.Windows.Media;
using DropWheel.Conversions;
using DropWheel.Wheel;

namespace DropWheel.Windows.Tests;

/// <summary>Колесо в разных состояниях → PNG. Тест падает, только если рисование бросило исключение.</summary>
public class WheelScreenshots
{
    private static IReadOnlyList<WheelItem> ItemsFor(string file, OperationMode mode) =>
        WheelItems.For(OperationCatalog.ForFiles([file]), mode);

    [Theory]
    [InlineData("photo.heic", OperationMode.Convert, 2, "wheel-photo-convert")]
    [InlineData("photo.heic", OperationMode.Tool, 0, "wheel-photo-tools")]
    [InlineData("clip.mov", OperationMode.Convert, null, "wheel-video-convert")]
    [InlineData("song.flac", OperationMode.Convert, 1, "wheel-audio-convert")]
    [InlineData("report.pdf", OperationMode.Convert, null, "wheel-pdf-convert")]
    public void Wheel(string file, OperationMode mode, int? hover, string name)
    {
        Sta.Run(() =>
        {
            var wheel = new WheelControl { Items = ItemsFor(file, mode), Mode = mode, HoverIndex = hover, IsPending = false };
            var path = Screenshot.Save(wheel, 300, 300, name, new SolidColorBrush(Color.FromRgb(0x2B, 0x5D, 0x8C)));
            Assert.True(new FileInfo(path).Length > 1000);
        });
    }

    [Fact]
    public void Wheel_nothing_to_offer()
    {
        Sta.Run(() =>
        {
            var wheel = new WheelControl { Items = [], Mode = OperationMode.Convert, IsPending = false };
            Screenshot.Save(wheel, 300, 300, "wheel-empty", Brushes.WhiteSmoke);
        });
    }
}
