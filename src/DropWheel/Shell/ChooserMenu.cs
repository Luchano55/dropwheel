using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using DropWheel.App;
using DropWheel.Conversions;
using DropWheel.Localization;

namespace DropWheel.Shell;

/// <summary>
/// Запасное компактное меню у курсора для пункта «Конвертировать…» (раздел 9 ТЗ).
/// Основной вариант — колесо в режиме клика (App.ShowChooser); это меню показывается,
/// только если колесо открыть не удалось (причина — в журнале).
/// Меню WPF нужен владелец с фокусом — создаём невидимое окно под курсором.
/// </summary>
internal static class ChooserMenu
{
    public static void Show(IReadOnlyList<string> paths, IReadOnlyList<Operation> ops, Action<Operation> chosen)
    {
        var host = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Width = 1,
            Height = 1,
            Left = -10000,
            Top = -10000,
        };
        host.Show();
        NativeMethods.SetForegroundWindow(new WindowInteropHelper(host).Handle);
        host.Activate();

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint, PlacementTarget = host };
        var title = paths.Count == 1 ? Path.GetFileName(paths[0]) : Loc.F(Loc.T.ChooserFiles, paths.Count);
        menu.Items.Add(new MenuItem { Header = title, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());

        if (ops.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = Loc.T.WheelNothing, IsEnabled = false });
        }
        else
        {
            AddGroup(menu, ops.Where(o => o.Mode == OperationMode.Convert), "→ ", chosen);
            var tools = ops.Where(o => o.Mode == OperationMode.Tool).ToList();
            if (tools.Count > 0 && menu.Items.Count > 2) menu.Items.Add(new Separator());
            AddGroup(menu, tools, "", chosen);
        }

        menu.Closed += (_, _) => host.Close();
        menu.IsOpen = true;
    }

    private static void AddGroup(ContextMenu menu, IEnumerable<Operation> ops, string prefix, Action<Operation> chosen)
    {
        foreach (var op in ops)
        {
            var item = new MenuItem { Header = prefix + Loc.Label(op) };
            item.Click += (_, _) => chosen(op);
            menu.Items.Add(item);
        }
    }
}
