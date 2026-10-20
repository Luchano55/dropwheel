using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DropWheel.App;

/// <summary>
/// Меню значка в трее в цветах темы Windows (светлая/тёмная, как меню панели задач Windows 11):
/// ровный фон, скруглённая подсветка пункта, тонкие разделители, галочка из шрифта значков.
/// </summary>
internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private readonly Color _back;
    private readonly Color _fore;
    private readonly Color _disabled;
    private readonly Color _hover;
    private readonly Color _separator;
    private readonly Color _border;
    private readonly bool _drawBorder;

    /// <param name="drawBorder">Рисовать рамку самим (на Windows 11 скруглённую рамку рисует система).</param>
    public TrayMenuRenderer(bool dark, bool drawBorder)
    {
        _drawBorder = drawBorder;
        if (dark)
        {
            _back = Color.FromArgb(0x2C, 0x2C, 0x2C);
            _fore = Color.White;
            _disabled = Color.FromArgb(0x78, 0x78, 0x78);
            _hover = Color.FromArgb(0x3D, 0x3D, 0x3D);
            _separator = Color.FromArgb(0x45, 0x45, 0x45);
            _border = Color.FromArgb(0x48, 0x48, 0x48);
        }
        else
        {
            _back = Color.FromArgb(0xF9, 0xF9, 0xF9);
            _fore = Color.FromArgb(0x1A, 0x1A, 0x1A);
            _disabled = Color.FromArgb(0xA0, 0xA0, 0xA0);
            _hover = Color.FromArgb(0xEA, 0xEA, 0xEA);
            _separator = Color.FromArgb(0xE0, 0xE0, 0xE0);
            _border = Color.FromArgb(0xD6, 0xD6, 0xD6);
        }
        RoundedEdges = false;
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(_back);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // Поле под галочки — того же цвета, что и меню.
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (!_drawBorder) return;
        using var pen = new Pen(_border);
        var r = e.ToolStrip.ClientRectangle;
        e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
        using var path = RoundedRect(r, 4);
        using var brush = new SolidBrush(_hover);
        g.FillPath(brush, path);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? _fore : _disabled;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_separator);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        // Галочка — значок из Segoe Fluent Icons / MDL2 (\uE73E), а не старая картинка WinForms.
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe MDL2 Assets", 9f, FontStyle.Regular, GraphicsUnit.Point);
        using var brush = new SolidBrush(e.Item.Enabled ? _fore : _disabled);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("\uE73E", font, brush, e.ImageRectangle, format);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _fore;
        base.OnRenderArrow(e);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
