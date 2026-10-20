using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DropWheel.App;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Значок в трее: обычный и «в работе» (1, 3, 12 задач) на 16/20/24/32 px, на светлой и тёмной панели задач,
/// увеличено в 4 раза без сглаживания — чтобы видеть каждый пиксель. И логотип в окнах (векторный).
/// </summary>
public class TrayScreenshots
{
    private static readonly int[] Sizes = [16, 20, 24, 32];
    private static readonly int?[] Counts = [null, 1, 3, 12];

    private static BitmapSource Load(byte[] png)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = new MemoryStream(png);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    [Fact]
    public void Tray_icons_on_light_and_dark_taskbars()
    {
        Sta.Run(() =>
        {
            const int zoom = 4;
            var rows = new StackPanel();
            foreach (var dark in new[] { false, true })
            {
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Background = new SolidColorBrush(dark ? Color.FromRgb(0x1C, 0x1C, 0x1C) : Color.FromRgb(0xEE, 0xEE, 0xEE)),
                };
                foreach (var size in Sizes)
                foreach (var count in Counts)
                {
                    var png = TrayIconArt.RenderPng(size, count);
                    Assert.True(png.Length > 100);
                    var img = new Image
                    {
                        Source = Load(png),
                        Width = size * zoom,
                        Height = size * zoom,
                        Margin = new Thickness(12),
                        Stretch = Stretch.Fill,
                    };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                    row.Children.Add(img);
                }
                rows.Children.Add(row);
            }
            rows.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Screenshot.Save(rows, rows.DesiredSize.Width, rows.DesiredSize.Height, "tray-icons", scale: 1);
        });
    }

    [Fact]
    public void Logo_mark_sizes()
    {
        Sta.Run(() =>
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };
            foreach (var size in new double[] { 16, 20, 24, 32, 48, 96, 160 })
                row.Children.Add(new LogoMark { Size = size, Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Center });
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Screenshot.Save(row, row.DesiredSize.Width, row.DesiredSize.Height, "logo-mark");
        });
    }
}
