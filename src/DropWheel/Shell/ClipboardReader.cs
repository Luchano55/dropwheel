using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace DropWheel.Shell;

/// <summary>
/// Чтение буфера обмена для «Из буфера обмена…» / Ctrl+Alt+V (решение D-CLIPBOARD). Только поток UI (STA).
/// Буфер может быть ненадолго занят другой программой (она как раз в него пишет) — несколько попыток
/// с паузой 100 мс; не вышло — исключение, приложение покажет «Не удалось прочитать буфер обмена».
/// Что делать с найденным — решает ядро (<see cref="ClipboardPlan"/>).
/// </summary>
internal static class ClipboardReader
{
    private const int Attempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Формат «PNG», который кладут браузеры, «Ножницы», Paint, Office: с прозрачностью и без потерь.</summary>
    private const string PngFormat = "PNG";

    /// <summary>Что лежит в буфере: файлы (CF_HDROP) и есть ли картинка.</summary>
    public sealed record Snapshot(IReadOnlyList<string>? Files, bool HasImage, Forms.IDataObject? Data);

    public static Snapshot Read() => Retry(() =>
    {
        var data = Forms.Clipboard.GetDataObject();
        if (data is null) return new Snapshot(null, false, null);
        var files = data.GetDataPresent(Forms.DataFormats.FileDrop) ? data.GetData(Forms.DataFormats.FileDrop) as string[] : null;
        var image = data.GetDataPresent(PngFormat) || data.GetDataPresent(Forms.DataFormats.Bitmap);
        return new Snapshot(files, image, data);
    });

    /// <summary>
    /// Сохранить картинку из буфера в PNG: готовый PNG — байт в байт (прозрачность сохраняется), иначе — Bitmap
    /// (снимок экрана PrintScreen), перекодированный в PNG. При ошибке недописанный файл удаляется.
    /// </summary>
    public static void SaveImage(Snapshot snapshot, string path)
    {
        var data = snapshot.Data ?? throw new InvalidOperationException("В буфере нет данных");
        try
        {
            Retry(() =>
            {
                if (data.GetDataPresent(PngFormat) && data.GetData(PngFormat) is Stream png && LooksLikePng(png))
                {
                    png.Position = 0;
                    using var file = File.Create(path);
                    png.CopyTo(file);
                    return true;
                }
                if (data.GetData(Forms.DataFormats.Bitmap, autoConvert: true) is System.Drawing.Image bitmap)
                {
                    using (bitmap) bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    return true;
                }
                throw new InvalidOperationException("Картинка из буфера не читается");
            });
        }
        catch
        {
            try { File.Delete(path); }
            catch (Exception) { }
            throw;
        }
    }

    private static bool LooksLikePng(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 8) return false;
        stream.Position = 0;
        Span<byte> head = stackalloc byte[8];
        return stream.Read(head) == 8 && head.SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    /// <summary>Буфер занят (CLIPBRD_E_CANT_OPEN, ExternalException) — подождать и повторить.</summary>
    private static T Retry<T>(Func<T> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return action(); }
            catch (ExternalException) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }
}
