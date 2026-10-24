namespace DropWheel.Localization;

/// <summary>Строки записи экрана (решение D-RECORD). Английские — в Texts.En.cs, раздел «запись экрана».</summary>
public sealed partial class Texts
{
    public string TrayRecord { get; init; } = "Записать экран";
    public string TrayRecordStop { get; init; } = "Остановить запись";
    /// <summary>Запись экрана: «Запись 2026-10-24 15-30-05.mp4» ({0} — дата и время).</summary>
    public string RecordFile { get; init; } = "Запись {0}";
    public string RecordHint { get; init; } =
        "Выделите область для записи · щелчок — окно · Enter — весь экран · Esc — отмена";
    public string RecordStart { get; init; } = "Записать";
    public string RecordStop { get; init; } = "Стоп";
    /// <summary>Подсказка значка в трее во время записи ({0} — «00:12»).</summary>
    public string RecordTrayStatus { get; init; } = "идёт запись {0}";
    public string RecordSaving { get; init; } = "сохраняю запись…";
    public string RecordSaved { get; init; } = "Запись сохранена";
    public string RecordTrim { get; init; } = "Обрезать…";
    public string RecordFailed { get; init; } = "Не удалось записать экран";
    public string RecordNothing { get; init; } = "Ничего не записалось — ни одного кадра";
    /// <summary>Сработал предел длины ({0} — минут).</summary>
    public string RecordLimit { get; init; } = "Запись остановлена: прошло {0} мин — это предел одной записи";
    // настройки
    public string RecordHotkeyTitle { get; init; } = "{0} — запись экрана";
    public string RecordHotkeyHint { get; init; } =
        "Выделите область, окно или весь экран и нажмите «Записать» (MP4 или GIF). Остановить — то же сочетание или «Стоп».";
}
