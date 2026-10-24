using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DropWheel.App;
using DropWheel.Capture;
using DropWheel.Localization;
using DropWheel.Ui;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Сочетание снимка в настройках («Изменить…», D-CAPTURE): окно ждёт сочетание (после попытки Win+Shift+S — подсказка,
/// почему нельзя), проверки и «занято другой программой».
/// </summary>
public class CaptureKeysScreenshots
{
    private const uint Ctrl = KeyChord.Control, Alt = KeyChord.Alt, Shift = KeyChord.Shift, Win = KeyChord.Win;

    [Fact]
    public void Settings_capture_chord()
    {
        Sta.Run(() =>
        {
            var view = SettingsScreenshots.Make(true, SettingsPage.General,
                SettingsScreenshots.Sample() with { Language = Loc.Current == UiLanguage.En ? Loc.English : Loc.Russian });
            var (scene, w, h) = SettingsScreenshots.Window(view, light: true, SettingsView.DefaultHeight);
            scene.Measure(new Size(w, h));
            scene.Arrange(new Rect(0, 0, w, h));
            scene.UpdateLayout();
            view.BeginChordCapture();
            Assert.False(view.CaptureChord(Win | Shift, 0x53)); // Win+Shift+S — занято Windows
            scene.UpdateLayout();

            // Прокрутить так, чтобы строка «Нажмите сочетание…» была в верхней трети окна.
            var scroll = LogicalTreeHelper.GetChildren(view).OfType<Panel>().SelectMany(p => p.Children.OfType<ScrollViewer>()).Single();
            var page = (StackPanel)scroll.Content;
            var prompt = Find<TextBlock>(page, t => t.Text == Strings.Settings.CaptureChordPress);
            var top = prompt.TransformToAncestor(page).Transform(new Point(0, 0)).Y;
            scroll.ScrollToVerticalOffset(Math.Max(0, top - 220));
            scene.UpdateLayout();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            scene.UpdateLayout();
            Assert.True(scroll.VerticalOffset > 0, "Не прокрутилось");
            Find<TextBlock>(page, t => t.Text == Strings.Settings.CaptureChordWindows("Win+Shift+S"));

            var path = Screenshot.Save(scene, w, h, "settings-light-capture-chord", scale: 1.5);
            Assert.True(new FileInfo(path).Length > 5000);
        });
    }

    /// <summary>Неподходящее не принимается, Esc отменяет, занятое оставляет старое, свободное — сохраняется.</summary>
    [Fact]
    public void Capture_chord_flow()
    {
        Sta.Run(() =>
        {
            var view = SettingsScreenshots.Make(true, SettingsPage.General);
            var paused = new List<bool>();
            view.PauseCaptureHotkey = paused.Add;
            view.TryCaptureKeys = chord => chord.ToString() != "Ctrl+Alt+P"; // «занято другой программой»
            Assert.Equal(KeyChord.DefaultText, view.Settings.CaptureKeys);

            view.BeginChordCapture();
            Assert.Equal([true], paused);
            Assert.False(view.CaptureChord(Ctrl, 0xA2)); // только Ctrl — ждём дальше
            Assert.False(view.CaptureChord(0, 0x53));    // S
            Assert.False(view.CaptureChord(Win, 0x56));  // Win+V
            Assert.False(view.CaptureChord(Ctrl, 0x20)); // Ctrl+Space
            Assert.False(view.CaptureChord(0, 0x1B));    // Esc — отмена
            Assert.Equal([true, false], paused);
            Assert.False(view.CaptureChord(Ctrl | Alt, 0x51)); // уже не ждём
            Assert.Equal(KeyChord.DefaultText, view.Settings.CaptureKeys);

            view.BeginChordCapture();
            Assert.False(view.CaptureChord(Ctrl | Alt, 0x50)); // занято
            Assert.Equal(KeyChord.DefaultText, view.Settings.CaptureKeys);

            view.BeginChordCapture();
            Assert.True(view.CaptureChord(Ctrl | Shift, 0x2C));
            Assert.Equal("Ctrl+Shift+PrintScreen", view.Settings.CaptureKeys);
            view.BeginChordCapture();
            Assert.True(view.CaptureChord(0, 0x78));
            Assert.Equal("F9", view.Settings.CaptureKeys);
            Assert.Equal([true, false, true, false, true, false, true, false], paused);
        });
    }

    private static T Find<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject =>
        All(root).OfType<T>().First(match);

    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var d in All(VisualTreeHelper.GetChild(root, i)))
                yield return d;
    }
}
