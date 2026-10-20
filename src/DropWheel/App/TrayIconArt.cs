using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace DropWheel.App;

/// <summary>
/// Картинки значка в трее: обычный — значок программы (Assets/DropWheel.ico, нужный размер),
/// «в работе» — тот же значок с оранжевым кружком-счётчиком задач в правом нижнем углу.
/// </summary>
public static class TrayIconArt
{
    private const string ResourceName = "DropWheel.ico";
    private static readonly Color Badge = Color.FromArgb(0xFF, 0xF2, 0x5C, 0x24);

    /// <summary>Значок программы нужного размера; null — ресурс не найден.</summary>
    internal static Icon? LoadIcon(Size size)
    {
        try
        {
            using var stream = typeof(TrayIconArt).Assembly.GetManifestResourceStream(ResourceName);
            return stream is null ? null : new Icon(stream, size);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Картинка значка: count = null — обычный, число — «в работе» со счётчиком.</summary>
    internal static Bitmap Render(int size, int? count)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        using (var icon = LoadIcon(new Size(size, size)))
        {
            // ToBitmap сохраняет прозрачность 32-битного значка (DrawIcon через GDI её теряет).
            if (icon is not null)
            {
                using var art = icon.ToBitmap();
                g.DrawImage(art, new Rectangle(0, 0, size, size));
            }
            else DrawFallback(g, size);
        }

        if (count is { } n && n > 0) DrawBadge(g, size, n);
        return bmp;
    }

    /// <summary>PNG значка — для скриншот-тестов.</summary>
    public static byte[] RenderPng(int size, int? count)
    {
        using var bmp = Render(size, count);
        using var stream = new MemoryStream();
        bmp.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Значок для NotifyIcon (вызывающий освобождает HICON через DestroyIcon).</summary>
    internal static Icon CreateIcon(int size, int? count)
    {
        using var bmp = Render(size, count);
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static void DrawBadge(Graphics g, int size, int count)
    {
        // Кружок ~60 % значка в правом нижнем углу, с белой обводкой — виден и на светлой, и на тёмной панели.
        var d = (float)Math.Round(size * 0.62);
        var rect = new RectangleF(size - d, size - d, d, d);
        using (var ring = new SolidBrush(Color.White)) g.FillEllipse(ring, RectangleF.Inflate(rect, 0.5f, 0.5f));
        using (var fill = new SolidBrush(Badge)) g.FillEllipse(fill, RectangleF.Inflate(rect, -0.75f, -0.75f));

        // На 16 px «10+» не прочитать — тогда просто точка-кружок без цифры.
        var text = count <= 9 ? count.ToString() : size >= 24 ? "9+" : null;
        if (text is null) return;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI", d * (text.Length > 1 ? 0.5f : 0.74f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
        };
        g.DrawString(text, font, brush, new RectangleF(rect.X, rect.Y + d * 0.03f, rect.Width, rect.Height), format);
    }

    /// <summary>Если значка в ресурсах нет: простое синее кольцо.</summary>
    private static void DrawFallback(Graphics g, int size)
    {
        using var pen = new Pen(Color.FromArgb(0x2F, 0x6C, 0xF5), size * 0.18f);
        var inset = size * 0.16f;
        g.DrawEllipse(pen, inset, inset, size - 2 * inset, size - 2 * inset);
    }
}
