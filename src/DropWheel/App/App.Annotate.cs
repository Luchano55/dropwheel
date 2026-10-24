using System.Windows.Media.Imaging;
using DropWheel.Capture;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;
using DropWheel.Localization;
using DropWheel.Shell;

namespace DropWheel.App;

/// <summary>
/// «Пометки» (решение D-ANNOTATE): окно со стрелками, рамками, текстом, размытием и номерами поверх картинки.
/// Два входа: кнопка «Пометки» на панели снимка экрана и лепесток «Пометить…» у картинки (через AskValues, как окна
/// обрезки). Результат — PNG: у файла — «фото (пометки).png» по обычным правилам (папка результата, занятое имя → (1)),
/// у снимка — в папку снимков, как «Сохранить» на панели снимка.
/// </summary>
public partial class App
{
    /// <summary>Снимок экрана → окно пометок. После закрытия — память снимка возвращается (как после оверлея).</summary>
    private void AnnotateCapture(BitmapSource image)
    {
        var folder = ClipboardPlan.ImageFolder(Settings.ClipboardFolder, Settings.Conversion);
        var window = new AnnotateWindow(image, CaptureNaming.FileName(DateTime.Now), folder, saved =>
        {
            if (SaveCapture(saved) is not { } path) return null;
            _notifier?.Done(path);
            return path;
        }, Log);
        window.Closed += (_, _) => ReleaseCaptureMemory();
        window.ShowAndActivate();
    }

    /// <summary>Картинка-файл → окно пометок (ImageMagick открывает её в полном размере).</summary>
    private void AnnotateFile(string file, ToolLocator tools)
    {
        var op = OperationCatalog.Find(ImageAnnotate.Id)!;
        string Desired() => OutputNamer.DesiredPath(file, "png", Loc.Suffix(op), Settings.Conversion.OutputFolder);
        var window = new AnnotateWindow(file, tools, Path.GetFileName(Desired()), image =>
        {
            try
            {
                var path = OutputNamer.FirstFree(Desired());
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                ScreenCapture.SavePng(image, path); // новый файл: исходник и чужие файлы не перезаписываются
                Log.Write($"Пометки сохранены: {file} → {path}");
                _notifier?.Done(path);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Log.Write("Пометки не сохранились: " + ex);
                return null;
            }
        }, Log);
        window.Closed += (_, _) => ReleaseCaptureMemory();
        window.ShowAndActivate();
    }
}
