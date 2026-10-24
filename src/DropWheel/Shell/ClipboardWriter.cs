using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using DropWheel.Logging;

namespace DropWheel.Shell;

/// <summary>
/// Запись в буфер обмена (D-COPYRESULT): готовый результат («Копировать результат в буфер обмена») и картинка
/// из окна «Оформить…» (кнопка «Копировать»). Только поток UI (STA). Что копировать — решает ядро
/// (<see cref="ResultClipboard"/>). Буфер может быть занят другой программой — несколько попыток через 100 мс,
/// как у <see cref="ClipboardReader"/>.
/// </summary>
internal static class ClipboardWriter
{
    private const int Attempts = 5;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(100);
    /// <summary>Больше — только файлом: картинка в буфере хранится несжатой (4 байта на точку).</summary>
    private const long MaxPixels = 40_000_000;
    /// <summary>Формат «PNG» — с прозрачностью (браузеры, редакторы, мессенджеры его понимают).</summary>
    private const string PngFormat = "PNG";

    /// <summary>
    /// Готовые результаты одной пачки → буфер. Картинка — картинкой (Bitmap + PNG) и файлом; не открылась —
    /// только файлом. true — что-то скопировано.
    /// </summary>
    public static bool CopyResults(IReadOnlyList<string> results, FileLog log)
    {
        var action = ResultClipboard.Decide(results);
        if (action.Kind == ResultClipboardKind.None) return false;
        try
        {
            var data = new DataObject();
            var files = new StringCollection();
            files.AddRange(action.Paths.ToArray());
            data.SetFileDropList(files);
            if (action.Kind == ResultClipboardKind.Image && !TryAddImage(data, File.ReadAllBytes(action.Paths[0]), log))
                log.Write("Буфер обмена: картинка не открылась — копирую только файл " + action.Paths[0]);
            Set(data);
            log.Write($"Буфер обмена: результат скопирован ({action.Kind}): {string.Join(", ", action.Paths)}");
            return true;
        }
        catch (Exception ex)
        {
            log.Write("Буфер обмена: результат не скопирован: " + ex.Message);
            return false;
        }
    }

    /// <summary>Картинка (PNG-байты) → буфер без файла: «Копировать» в окне «Оформить…». Ошибка — исключение.</summary>
    public static void CopyImage(byte[] png, FileLog log)
    {
        var data = new DataObject();
        if (!TryAddImage(data, png, log)) throw new InvalidOperationException("Картинка не открылась");
        Set(data);
    }

    /// <summary>Bitmap (вставка в Paint, Word, мессенджеры) и PNG (с прозрачностью). false — картинка не открылась.</summary>
    private static bool TryAddImage(DataObject data, byte[] encoded, FileLog log)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = new MemoryStream(encoded);
            bitmap.EndInit();
            bitmap.Freeze();
            if ((long)bitmap.PixelWidth * bitmap.PixelHeight > MaxPixels) return false;
            data.SetImage(bitmap);
            data.SetData(PngFormat, new MemoryStream(IsPng(encoded) ? encoded : EncodePng(bitmap)));
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or ArgumentException or InvalidOperationException
                                       or COMException or FileFormatException)
        {
            log.Write("Буфер обмена: картинку не удалось прочитать: " + ex.Message);
            return false;
        }
    }

    private static bool IsPng(byte[] bytes) =>
        bytes.Length > 8 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Буфер занят (CLIPBRD_E_CANT_OPEN, ExternalException) — подождать и повторить.</summary>
    private static void Set(DataObject data)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (ExternalException) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }
}
