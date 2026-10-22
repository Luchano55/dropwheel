namespace DropWheel.Localization;

/// <summary>Строки «папок целиком» (решение D-FOLDERS). Английские — в Texts.En.cs, раздел «папки целиком».</summary>
public sealed partial class Texts
{
    public string FolderTooMany { get; init; } =
        "В папке больше {0} файлов — так много за раз DropWheel не берёт. Выберите папку поменьше.";
    public string FolderEmpty { get; init; } = "В папке нет файлов";
    public string FolderNothingToDo { get; init; } = "В папке нет файлов, к которым подходит «{0}»";
    public string FolderResultFiles { get; init; } = "Файлов в папке: {0}";
    public string FolderSkipped { get; init; } = "Пропущено (не подходят): {0}";
    public string FolderFailed { get; init; } = "Не получилось: {0} — подробности в журнале";
    public Noun Folders { get; init; } = new("папка", "папки", "папок");
}
