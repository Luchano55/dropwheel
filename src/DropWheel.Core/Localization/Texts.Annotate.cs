namespace DropWheel.Localization;

/// <summary>Строки окна «Пометки» (решение D-ANNOTATE). Английские — в Texts.En.cs, раздел «пометки».</summary>
public sealed partial class Texts
{
    public string AnnotateTitle { get; init; } = "Пометки";
    /// <summary>Кнопка панели снимка экрана.</summary>
    public string CaptureAnnotate { get; init; } = "Пометки";
    // инструменты
    public string AnnotateArrow { get; init; } = "Стрелка";
    public string AnnotateRectangle { get; init; } = "Рамка";
    public string AnnotateEllipse { get; init; } = "Эллипс";
    public string AnnotatePen { get; init; } = "Карандаш";
    public string AnnotateMarker { get; init; } = "Маркер";
    public string AnnotateText { get; init; } = "Текст";
    public string AnnotateBlur { get; init; } = "Размытие";
    public string AnnotateNumber { get; init; } = "Номер";
    // цвет и толщина
    public string AnnotateColor { get; init; } = "Цвет";
    public string AnnotateThin { get; init; } = "Тонкая линия";
    public string AnnotateMedium { get; init; } = "Средняя линия";
    public string AnnotateThick { get; init; } = "Толстая линия";
    public string ColorRed { get; init; } = "Красный";
    public string ColorOrange { get; init; } = "Оранжевый";
    public string ColorYellow { get; init; } = "Жёлтый";
    public string ColorGreen { get; init; } = "Зелёный";
    public string ColorBlue { get; init; } = "Синий";
    public string ColorPurple { get; init; } = "Фиолетовый";
    public string ColorBlack { get; init; } = "Чёрный";
    public string ColorWhite { get; init; } = "Белый";
    // правка
    public string AnnotateUndo { get; init; } = "Отменить (Ctrl+Z)";
    public string AnnotateRedo { get; init; } = "Повторить (Ctrl+Y)";
    public string AnnotateDeleteLast { get; init; } = "Удалить последнюю пометку (Delete)";
    // низ окна
    public string AnnotateCopy { get; init; } = "Копировать";
    public string AnnotateSave { get; init; } = "Сохранить";
    public string AnnotateSaveHint { get; init; } = "Сохранится PNG: «{0}»";
    public string AnnotateKeysHint { get; init; } = "Shift — ровная стрелка или квадрат · Ctrl+Z — отменить";
    public string AnnotateTextHint { get; init; } = "Enter — готово · Shift+Enter — новая строка · Esc — убрать текст";
    public string AnnotateCopyFailed { get; init; } = "Не удалось скопировать";
    public string AnnotateSaveFailed { get; init; } = "Не удалось сохранить картинку";
    public string AnnotateTooLarge { get; init; } = "Картинка слишком большая для пометок (больше {0} Мп)";
    // закрыть без сохранения
    public string AnnotateDiscardTitle { get; init; } = "Закрыть без сохранения?";
    public string AnnotateDiscardText { get; init; } = "Пометки пропадут: картинка с ними не сохранена и не скопирована.";
    public string AnnotateDiscard { get; init; } = "Закрыть";
    public string AnnotateKeep { get; init; } = "Вернуться";
    // командная строка
    public string CliAnnotateWindowOnly { get; init; } =
        "Операция {0} работает только в окне DropWheel: перетащите картинку на колесо → «Инструменты» → «Пометить…»";
}
