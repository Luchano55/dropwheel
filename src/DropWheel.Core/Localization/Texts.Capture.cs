namespace DropWheel.Localization;

/// <summary>Строки снимка экрана (решение D-CAPTURE). Английские — в Texts.En.cs, раздел «снимок экрана».</summary>
public sealed partial class Texts
{
    public string TrayCapture { get; init; } = "Снимок экрана";
    /// <summary>Снимок экрана: «Снимок 2026-10-24 15-30-05.png» ({0} — дата и время).</summary>
    public string CaptureFile { get; init; } = "Снимок {0}";
    public string CaptureHint { get; init; } =
        "Выделите область мышью · щелчок — окно · Enter — весь экран · Esc — отмена";
    public string CaptureCopy { get; init; } = "Копировать";
    public string CaptureSave { get; init; } = "Сохранить";
    public string CaptureWheel { get; init; } = "Колесо";
    public string CaptureText { get; init; } = "Текст";
    public string CaptureClose { get; init; } = "Закрыть (Esc)";
    public string CaptureCopied { get; init; } = "Снимок скопирован";
    public string CaptureCopiedHint { get; init; } = "Вставьте его куда нужно: Ctrl+V";
    public string CaptureTextCopied { get; init; } = "Текст скопирован";
    public string CaptureTextLines { get; init; } = "Строк: {0}. Вставьте его куда нужно: Ctrl+V";
    public string CaptureNoText { get; init; } = "На снимке не найден текст";
    public string CaptureFailed { get; init; } = "Не удалось сделать снимок экрана";
    public string CaptureClipboardFailed { get; init; } = "Не удалось положить в буфер обмена — он занят другой программой";
    // настройки
    public string CaptureHotkeyTitle { get; init; } = "{0} — снимок экрана";
    public string CaptureHotkeyHint { get; init; } =
        "Выделите область, щёлкните по окну или нажмите Enter — весь экран. Затем: копировать, сохранить или открыть колесо.";
}
