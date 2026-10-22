using System.Globalization;
using DropWheel.Conversions;
using DropWheel.Localization;

namespace DropWheel.Shell;

/// <summary>Что сделать по «Из буфера обмена» (пункт трея, Ctrl+Alt+V).</summary>
public enum ClipboardActionKind
{
    /// <summary>Скопированные файлы — колесо для них, как из меню проводника.</summary>
    Files,
    /// <summary>Картинка (снимок экрана, «Копировать изображение») — сохранить в PNG и открыть колесо для него.</summary>
    Image,
    /// <summary>Ничего подходящего (текст, пусто) — подсказка в трее.</summary>
    Nothing,
}

public sealed record ClipboardAction(ClipboardActionKind Kind, IReadOnlyList<string> Files);

/// <summary>
/// Логика «колеса для буфера обмена» без доступа к самому буферу (решение D-CLIPBOARD):
/// что делать по найденному в буфере, куда и под каким именем сохранить картинку.
/// Чтение буфера (CF_HDROP, PNG, Bitmap) — в приложении (Shell/ClipboardReader).
/// </summary>
public static class ClipboardPlan
{
    /// <summary>
    /// Файлы важнее картинки: проводник кладёт в буфер только список файлов, а если вместе с ним есть
    /// и картинка — пользователь всё равно копировал файлы. Берутся только существующие файлы (не папки):
    /// папку колесо сконвертировать не может. Нет файлов — картинка, если она есть; иначе — ничего.
    /// </summary>
    public static ClipboardAction Decide(IReadOnlyList<string>? files, bool hasImage, Func<string, bool>? fileExists = null)
    {
        var exists = fileExists ?? File.Exists;
        var existing = (files ?? [])
            .Where(f => !string.IsNullOrWhiteSpace(f) && exists(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existing.Count > 0) return new(ClipboardActionKind.Files, existing);
        return new(hasImage ? ClipboardActionKind.Image : ClipboardActionKind.Nothing, []);
    }

    /// <summary>
    /// Папка для картинки из буфера: своя папка из настроек («Картинки из буфера»), если выбрана;
    /// иначе «В одну папку», если выбрана; иначе «Изображения\DropWheel».
    /// Результат конвертации кладётся рядом с картинкой (или в «одну папку»).
    /// </summary>
    public static string ImageFolder(string? clipboardFolder, ConversionSettings settings) =>
        clipboardFolder is { Length: > 0 } own ? own
        : settings.OutputFolder is { Length: > 0 } shared ? shared
        : DefaultImageFolder;

    public static string ImageFolder(ConversionSettings settings) => ImageFolder(null, settings);

    /// <summary>
    /// Удалить ли PNG из буфера после задачи: только если так настроено, задача удалась
    /// и результат — другой файл (не сам PNG). Ошибка или отмена — картинка остаётся, чтобы не потерять её.
    /// </summary>
    public static bool ShouldDeleteImage(bool keepImage, bool done, string image, string? output) =>
        !keepImage && done && output is { Length: > 0 }
        && !string.Equals(Path.GetFullPath(output), Path.GetFullPath(image), StringComparison.OrdinalIgnoreCase);

    /// <summary>«Изображения\DropWheel» (в Windows — %USERPROFILE%\Pictures\DropWheel).</summary>
    public static string DefaultImageFolder
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrEmpty(pictures))
                pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            return Path.Combine(pictures, "DropWheel");
        }
    }

    /// <summary>«Буфер 2026-10-22 10-31-05.png» / «Clipboard 2026-10-22 10-31-05.png» — без двоеточий (их нет в именах Windows).</summary>
    public static string ImageFileName(DateTime now) =>
        Loc.F(Loc.T.ClipboardFile, now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture)) + ".png";

    /// <summary>Свободный путь для картинки: занято (две вставки за секунду) — «… (1).png».</summary>
    public static string ImagePath(string folder, DateTime now, Func<string, bool>? exists = null) =>
        Jobs.OutputNamer.FirstFree(Path.Combine(folder, ImageFileName(now)), exists);
}
