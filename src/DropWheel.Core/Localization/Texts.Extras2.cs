namespace DropWheel.Localization;

/// <summary>
/// Строки: смена сочетания снимка и снимок с задержкой (D-CAPTURE), «Сообщить о проблеме» (D-REPORT).
/// Английские — в Texts.En.cs, раздел «сочетание снимка, задержка, отчёт».
/// </summary>
public sealed partial class Texts
{
    // сочетание снимка в настройках
    public string CaptureChange { get; init; } = "Изменить…";
    public string CaptureChordPress { get; init; } = "Нажмите сочетание… (Esc — отмена)";
    public string CaptureChordNeedsModifier { get; init; } =
        "Добавьте Ctrl, Alt или Win: одна буква или цифра — это обычный набор текста";
    /// <summary>{0} — сочетание («Win+S»).</summary>
    public string CaptureChordWindows { get; init; } =
        "{0} занято Windows. Win подходит только с F1–F24 — выберите другое сочетание";
    public string CaptureChordKey { get; init; } = "Подходят буквы, цифры, F1–F24 и Print Screen";
    /// <summary>{0} — новое сочетание, {1} — оставшееся старое.</summary>
    public string CaptureChordBusy { get; init; } = "Сочетание {0} занято другой программой — осталось {1}";

    // снимок с задержкой
    /// <summary>Пункт трея: {0} — секунды.</summary>
    public string TrayCaptureDelay { get; init; } = "Снимок через {0} с";

    // сообщить о проблеме
    public string TrayReportProblem { get; init; } = "Сообщить о проблеме";
    /// <summary>Заголовок задачи на GitHub: {0} — версия.</summary>
    public string ReportTitle { get; init; } = "Проблема в версии {0}";
    public string ReportWhat { get; init; } = "Что случилось:";
    public string ReportSteps { get; init; } = "Что делали перед этим (какой файл, что нажимали):";
    public string ReportPrivacy { get; init; } =
        "Задача на GitHub видна всем. Имя пользователя в путях заменено на %USERPROFILE%; проверьте журнал ниже перед отправкой.";
    /// <summary>{0} — число строк.</summary>
    public string ReportLog { get; init; } = "Журнал программы: последние строки ({0})";
    /// <summary>{0} — как открыть папку журнала.</summary>
    public string ReportLogCut { get; init; } = "Журнал обрезан — полный можно приложить файлом: {0}";
    public string ReportCopied { get; init; } = "Отчёт скопирован в буфер обмена";
    public string ReportCopiedHint { get; init; } =
        "Открылась новая задача на GitHub. Если текст не подставился — вставьте его: Ctrl+V";
    public string ReportFailed { get; init; } = "Не удалось открыть страницу отчёта";
}
