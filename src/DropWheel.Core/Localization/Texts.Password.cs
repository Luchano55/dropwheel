namespace DropWheel.Localization;

/// <summary>
/// Строки пароля на PDF (решение D-PDFPASS) и GIF из куска видео (D-TRIM).
/// Английские — в Texts.En.cs, раздел «пароль PDF и GIF из куска».
/// </summary>
public sealed partial class Texts
{
    // ---------- GIF из куска видео ----------
    public string TrimResult { get; init; } = "Результат";
    public string TrimResultVideo { get; init; } = "Видео";
    public string TrimResultGif { get; init; } = "GIF";
    public string TrimButtonGif { get; init; } = "Сделать GIF";
    public string TrimGifHint { get; init; } = "GIF — 15 кадров в секунду, до 640 пикселей в ширину, без звука, по кругу.";
    public string TrimGifLong { get; init; } = "Кусок длиннее {0} секунд — GIF выйдет очень большим. Лучше выбрать кусок покороче.";
    public string CliTrimExactGif { get; init; } = "--exact и --gif вместе нельзя: GIF всегда пересжимается";

    // ---------- пароль на PDF ----------
    public string PdfProtectTitle { get; init; } = "Защитить PDF паролем";
    public string PdfUnlockTitle { get; init; } = "Снять пароль с PDF";
    public string PdfProtectIntro { get; init; } =
        "Без пароля PDF не откроется. Запомните его: восстановить пароль нельзя. Исходный файл не изменится.";
    public string PdfUnlockIntro { get; init; } = "Рядом появится копия без пароля. Исходный файл не изменится.";
    public string PdfPasswordLabel { get; init; } = "Пароль";
    public string PdfPasswordRepeat { get; init; } = "Повторите пароль";
    public string PdfPasswordShow { get; init; } = "Показать пароль";
    public string PdfPasswordMismatch { get; init; } = "Пароли не совпадают";
    public string PdfProtectButton { get; init; } = "Защитить";
    public string PdfUnlockButton { get; init; } = "Снять пароль";
    public string ErrWrongPassword { get; init; } = "Неверный пароль";
    public string ErrPdfAlreadyProtected { get; init; } = "PDF уже защищён паролем — сначала снимите пароль";
    public string CliPasswordUsage { get; init; } = """
        Пароль на PDF:
          dropwheel convert <файл.pdf> pdf-protect --password <пароль>
          dropwheel convert <файл.pdf> pdf-unlock --password <пароль>
          --password          пароль (в командной строке его видят другие программы — для своего компьютера)
        """;
    public string CliNeedPassword { get; init; } = "Для {0} нужен пароль: --password <пароль>";
    public string CliBadPassword { get; init; } = "Пароль не может быть пустым или в несколько строк";
    public string CliNoPassword { get; init; } = "Операция {0} не принимает --password";
}
