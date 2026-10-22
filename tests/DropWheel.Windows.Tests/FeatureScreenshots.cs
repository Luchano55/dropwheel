using System.Windows;
using System.Windows.Controls;
using DropWheel.App;
using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Settings;
using DropWheel.Ui;
using DropWheel.Updates;
using DropWheel.Wheel;

namespace DropWheel.Windows.Tests;

/// <summary>
/// Снимки новых возможностей 1.0 для README: колесо для папки (D-FOLDERS), колесо для картинки из браузера (D-BROWSER),
/// нижняя часть «Настройки → Основное» (картинки из буфера и браузера, автозапуск, канал обновлений).
/// Наборы лепестков — тем же кодом, что у приложения (FolderPlan.OfferFor, WheelArrangement.Arrange), файлы — выдуманные:
/// диск не нужен, картинки не зависят от машины CI.
/// </summary>
public class FeatureScreenshots
{
    /// <summary>Папка «Отпуск» (по-английски — «Vacation»): в основном HEIC с телефона, немного JPG, подпапка 2023.</summary>
    internal static FolderSelection Vacation(string folderName)
    {
        var root = $@"C:\Users\Roman\Pictures\{folderName}";
        var files = new List<string>();
        for (var i = 0; i < 18; i++) files.Add($@"{root}\IMG_{2041 + i}.HEIC");
        for (var i = 1; i <= 4; i++) files.Add($@"{root}\Sea {i}.jpg");
        files.Add($@"{root}\2023\IMG_0815.HEIC");
        files.Add($@"{root}\2023\IMG_0816.HEIC");
        return new FolderSelection([new FolderScan(root, files, TooMany: false)], []);
    }

    /// <summary>Общий размер файлов папки (то, что колесо пишет в «пилюле»).</summary>
    internal const long VacationBytes = 69_800_000;

    /// <summary>Картинка WebP с сайта: колесо — как для файла «image.webp» (DragWheelController.OnImageEntered).</summary>
    internal const long BrowserImageBytes = 184_320;

    /// <summary>Лепестки так же, как в приложении: предложение операций, затем раскладка с настройками колеса по умолчанию.</summary>
    internal static (IReadOnlyList<WheelItem> Convert, IReadOnlyList<WheelItem> Tools) Arrange(WheelOffer offer)
    {
        Assert.NotEmpty(offer.Operations);
        return WheelArrangement.Arrange(offer.Operations, offer.Group, new WheelPreferences());
    }

    /// <summary>
    /// Колесо для папки, как после «Конвертировать…» в проводнике (режим клика): операции по содержимому папки,
    /// в «пилюле» — размер всех файлов; в «Инструментах» — «Один PDF» и нет архивов.
    /// </summary>
    internal static void FolderWheel(bool light, OperationMode mode, int? hover, string folderName, string name)
    {
        var offer = FolderPlan.OfferFor(Vacation(folderName));
        Assert.Equal(WheelGroups.Photo, offer.Group);
        Assert.DoesNotContain(offer.Operations, o => o.Id == "zip-pack");
        Assert.Contains(offer.Operations, o => o.Id == "images-to-pdf");
        var (convert, tools) = Arrange(offer);
        Sta.Run(() =>
        {
            var wheel = WheelScreenshots.Make(light);
            wheel.Items = mode == OperationMode.Convert ? convert : tools;
            wheel.Mode = mode;
            wheel.ShowModeDots = convert.Count > 0 && tools.Count > 0;
            wheel.IdleText = Loc.FileSize(VacationBytes);
            wheel.HoverIndex = hover;
            WheelScreenshots.Save(wheel, light, name);
        });
    }

    /// <summary>Колесо при перетаскивании картинки WebP из браузера: операции как для файла её формата, в «пилюле» — её размер.</summary>
    internal static void BrowserWheel(bool light, int? hover, string name)
    {
        var offer = FolderPlan.OfferFor(["image.webp"]);
        var (convert, tools) = Arrange(offer);
        Assert.DoesNotContain(convert, i => i.Id == "to-webp");
        Sta.Run(() =>
        {
            var wheel = WheelScreenshots.Make(light);
            wheel.Items = convert;
            wheel.ShowModeDots = tools.Count > 0;
            wheel.IdleText = Loc.FileSize(BrowserImageBytes);
            wheel.HoverIndex = hover;
            WheelScreenshots.Save(wheel, light, name);
        });
    }

    /// <summary>
    /// «Настройки → Основное», прокрученные вниз до раздела «Windows»: автозапуск, меню проводника, Ctrl+Alt+V,
    /// «Картинки из буфера и браузера» (своя папка), «Оставлять исходную картинку», обновления, канал обновлений.
    /// Окно на снимке ровно такой высоты, чтобы раздел поместился целиком от заголовка до конца страницы.
    /// </summary>
    internal static void SettingsLower(bool light, string clipboardFolder, string name)
    {
        Sta.Run(() =>
        {
            var settings = SettingsScreenshots.Sample() with
            {
                Language = Loc.Current == UiLanguage.En ? Loc.English : Loc.Russian,
                ClipboardFolder = clipboardFolder,
                UpdateChannel = UpdateChannels.Stable,
            };

            // Первый проход: где на странице заголовок «Windows» и какой она высоты.
            var probe = SettingsScreenshots.Make(light, SettingsPage.General, settings);
            probe.Width = SettingsView.DefaultWidth;
            probe.Measure(new Size(SettingsView.DefaultWidth, double.PositiveInfinity));
            probe.Arrange(new Rect(0, 0, SettingsView.DefaultWidth, probe.DesiredSize.Height));
            probe.UpdateLayout();
            var (page, title) = SystemSection(probe);
            var top = title.TransformToAncestor(page).Transform(new Point(0, 0)).Y - title.Margin.Top;
            var below = page.ActualHeight + page.Margin.Bottom - top;
            Assert.True(below > 300, $"Раздел «Windows» подозрительно короткий: {below}");

            // Второй проход: окно = заголовок окна + отступ страницы + раздел до конца; прокрутка до упора вниз.
            var view = SettingsScreenshots.Make(light, SettingsPage.General, settings);
            var height = Math.Ceiling(SettingsScreenshots.TitleHeight + page.Margin.Top + below);
            var (scene, w, h) = SettingsScreenshots.Window(view, light, height);
            scene.Measure(new Size(w, h));
            scene.Arrange(new Rect(0, 0, w, h));
            scene.UpdateLayout();
            var scroll = FindScroll(view);
            Assert.True(scroll.ScrollableHeight > 0, "Страница «Основное» поместилась без прокрутки — снимок ничего не покажет");
            scroll.ScrollToBottom();
            scene.UpdateLayout();
            if (scroll.VerticalOffset <= 0)
            {
                // Прокрутка у ScrollViewer — отложенная команда: дать ей выполниться.
                System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { },
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                scene.UpdateLayout();
            }
            Assert.True(scroll.VerticalOffset > 0, "Не прокрутилось");

            var path = Screenshot.Save(scene, w, h, name, scale: 1.5);
            Assert.True(new FileInfo(path).Length > 5000);
        });
    }

    private static ScrollViewer FindScroll(SettingsView view) =>
        LogicalTreeHelper.GetChildren(view).OfType<Panel>()
            .SelectMany(p => p.Children.OfType<ScrollViewer>())
            .Single();

    private static (StackPanel Page, TextBlock Title) SystemSection(SettingsView view)
    {
        var page = (StackPanel)FindScroll(view).Content;
        var title = page.Children.OfType<TextBlock>().Single(t => t.Text == Strings.Settings.SystemTitle);
        return (page, title);
    }

    [Theory]
    [InlineData(true, 0, "wheel-light-folder-convert")]
    [InlineData(false, 0, "wheel-dark-folder-convert")]
    public void Folder_convert(bool light, int? hover, string name) =>
        FolderWheel(light, OperationMode.Convert, hover, "Отпуск", name);

    [Theory]
    [InlineData(true, null, "wheel-light-folder-tools")]
    [InlineData(false, 4, "wheel-dark-folder-tools-hover")]
    public void Folder_tools(bool light, int? hover, string name) =>
        FolderWheel(light, OperationMode.Tool, hover, "Отпуск", name);

    [Theory]
    [InlineData(true, 1, "wheel-light-browser-webp")]
    [InlineData(false, 1, "wheel-dark-browser-webp")]
    public void Browser_image(bool light, int? hover, string name) => BrowserWheel(light, hover, name);

    [Theory]
    [InlineData(true, "settings-light-general-lower")]
    [InlineData(false, "settings-dark-general-lower")]
    public void Settings_general_lower(bool light, string name) =>
        SettingsLower(light, @"C:\Users\Роман\Pictures\Из браузера", name);
}
