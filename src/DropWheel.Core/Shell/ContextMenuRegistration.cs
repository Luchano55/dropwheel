using DropWheel.Conversions;

namespace DropWheel.Shell;

/// <summary>Значение реестра. KeyPath — относительно HKEY_CURRENT_USER; Name = null — значение «по умолчанию».</summary>
public sealed record RegistryValue(string KeyPath, string? Name, string Value);

/// <summary>
/// Пункт «Конвертировать…» в контекстном меню проводника (раздел 9 ТЗ): ключи в HKCU, без прав
/// администратора и без shell-расширения. Здесь только список ключей — запись в реестр делает приложение.
/// </summary>
public static class ContextMenuRegistration
{
    public const string VerbKey = "DropWheel.Convert";
    public const string MenuText = "Конвертировать…";

    /// <summary>
    /// Ключ для всех файлов («*»): «В ZIP» подходит к любому файлу, поэтому пункт есть везде.
    /// Папки (Directory) пока не регистрируем.
    /// </summary>
    public const string AllFilesKey = @"Software\Classes\*\shell\" + VerbKey;

    public static IReadOnlyList<RegistryValue> Entries(string exePath) =>
    [
        new(AllFilesKey, "MUIVerb", MenuText),
        new(AllFilesKey, "Icon", $"\"{exePath}\",0"),
        // Player: проводник отдаёт выделенные файлы без лимита в 15 штук (MultipleInvokePromptMinimum)
        // и запускает процесс на каждый файл. TODO: проверить на настоящей Windows 10/11.
        new(AllFilesKey, "MultiSelectModel", "Player"),
        new(AllFilesKey + @"\command", null, $"\"{exePath}\" --convert \"%1\""),
    ];

    /// <summary>
    /// Ключи прежних версий — по каждому расширению (...\SystemFileAssociations\.jpg\shell\DropWheel.Convert).
    /// Удаляются при установке, иначе после обновления пункт в меню двоится.
    /// </summary>
    public static IReadOnlyList<string> LegacyKeys() =>
        Extensions.All.Distinct()
            .Select(ext => $@"Software\Classes\SystemFileAssociations\.{ext}\shell\{VerbKey}")
            .ToList();

    /// <summary>Все ключи пункта меню (текущий и прежние) — удалять целиком, с подключами.</summary>
    public static IReadOnlyList<string> KeysToDelete() => [AllFilesKey, .. LegacyKeys()];
}
