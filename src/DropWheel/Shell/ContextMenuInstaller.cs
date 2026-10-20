using DropWheel.Logging;
using Microsoft.Win32;

namespace DropWheel.Shell;

/// <summary>Пишет/удаляет пункт «Конвертировать…» в HKCU — без прав администратора.</summary>
internal static class ContextMenuInstaller
{
    public static void TryInstall(FileLog log)
    {
        try
        {
            var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Не известен путь к exe");
            foreach (var entry in ContextMenuRegistration.Entries(exe))
            {
                using var key = Registry.CurrentUser.CreateSubKey(entry.KeyPath, writable: true);
                key.SetValue(entry.Name ?? "", entry.Value, RegistryValueKind.String);
            }
            log.Write($"Пункт меню проводника зарегистрирован для {exe}");
        }
        catch (Exception ex)
        {
            log.Write("Не удалось зарегистрировать пункт меню: " + ex);
        }
    }

    public static void TryUninstall(FileLog log)
    {
        try
        {
            foreach (var path in ContextMenuRegistration.KeysToDelete())
                Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            log.Write("Пункт меню проводника удалён");
        }
        catch (Exception ex)
        {
            log.Write("Не удалось удалить пункт меню: " + ex);
        }
    }
}
