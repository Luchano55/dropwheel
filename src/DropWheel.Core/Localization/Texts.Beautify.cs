namespace DropWheel.Localization;

/// <summary>
/// Строки «Оформить…» (D-BEAUTIFY) и «Копировать результат в буфер обмена» (D-COPYRESULT).
/// Английские — в Texts.En.cs, раздел «оформить, копировать результат».
/// </summary>
public sealed partial class Texts
{
    // ---------- оформить картинку (D-BEAUTIFY) ----------
    public string BeautifyTitle { get; init; } = "Оформить картинку";
    public string BeautifySave { get; init; } = "Сохранить";
    public string BeautifyCopy { get; init; } = "Копировать";
    public string BeautifyBackground { get; init; } = "Фон";
    public string BeautifyPadding { get; init; } = "Отступ";
    public string BeautifyRadius { get; init; } = "Скругление";
    public string BeautifyShadow { get; init; } = "Тень";
    public string BeautifyHint { get; init; } = "Результат — PNG рядом с исходником: «{0}».";
    public string BeautifyRendering { get; init; } = "Рисую…";
    public string BeautifyRenderFailed { get; init; } = "Предпросмотр не получился";
    public string BeautifyCopyFailed { get; init; } = "Не удалось скопировать";
    public string BgSunset { get; init; } = "Закат";
    public string BgSky { get; init; } = "Небо";
    public string BgMint { get; init; } = "Мята";
    public string BgWhite { get; init; } = "Белый";
    public string BgGrey { get; init; } = "Серый";
    public string BgDark { get; init; } = "Тёмный";
    public string BgNone { get; init; } = "Прозрачный";
    public string BeautifyCliUsage { get; init; } = """
        Оформить картинку (PNG со скруглёнными углами, тенью и фоном):
          dropwheel convert <картинка> img-beautify [--style "bg=sunset;pad=8;radius=12;shadow=1"]
          bg       фон: sunset, sky, mint, white, grey, dark, none (прозрачный)
          pad      отступ, % от короткой стороны (0–25)
          radius   скругление углов, px (0–100)
          shadow   тень: 1 или 0
        """;
    public string CliBadStyle { get; init; } = "Не понял стиль «{0}» (пример: bg=sunset;pad=8;radius=12;shadow=1)";
    public string CliNoStyle { get; init; } = "Операция {0} не принимает --style";
    public string CliPictureFailed { get; init; } = "Не удалось открыть картинку «{0}»";

    // ---------- копировать результат в буфер (D-COPYRESULT) ----------
    public string CopyResult { get; init; } = "Копировать результат в буфер обмена";
    public string CopyResultHint { get; init; } =
        "Готовая картинка вставится картинкой (Ctrl+V в мессенджере или редакторе), остальное — файлом (Ctrl+V в проводнике).";
    public string CopiedToClipboard { get; init; } = "Скопировано в буфер обмена";
}
