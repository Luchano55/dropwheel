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

    public static IReadOnlyList<RegistryValue> Entries(string exePath)
    {
        var list = new List<RegistryValue>();
        foreach (var key in KeysToDelete())
        {
            list.Add(new(key, "MUIVerb", MenuText));
            list.Add(new(key, "Icon", $"\"{exePath}\",0"));
            // Player: проводник отдаёт выделенные файлы без лимита в 15 штук (MultipleInvokePromptMinimum)
            // и запускает процесс на каждый файл. TODO: проверить на настоящей Windows 10/11.
            list.Add(new(key, "MultiSelectModel", "Player"));
            list.Add(new(key + @"\command", null, $"\"{exePath}\" --convert \"%1\""));
        }
        return list;
    }

    /// <summary>Ключи ...\shell\DropWheel.Convert по каждому расширению — удалять целиком, с подключами.</summary>
    public static IReadOnlyList<string> KeysToDelete() =>
        Extensions.All.Distinct()
            .Select(ext => $@"Software\Classes\SystemFileAssociations\.{ext}\shell\{VerbKey}")
            .ToList();
}
