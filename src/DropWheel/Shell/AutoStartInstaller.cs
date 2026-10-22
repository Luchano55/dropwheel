using DropWheel.Logging;
using Microsoft.Win32;

namespace DropWheel.Shell;

/// <summary>Автозапуск: значение DropWheel в HKCU\…\Run (без прав администратора). Ошибки — в журнал.</summary>
internal static class AutoStartInstaller
{
    private static string? Exe => Environment.ProcessPath;

    /// <summary>Записанная команда в реестре (null — автозапуска нет).</summary>
    public static string? ReadCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistration.RunKey);
            return key?.GetValue(AutoStartRegistration.ValueName) as string;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Автозапуск включён (значение есть — даже если указывает на старую папку программы).</summary>
    public static bool IsEnabled() => !string.IsNullOrWhiteSpace(ReadCommand());

    /// <returns>Удалось ли.</returns>
    public static bool TrySet(bool enabled, FileLog log)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(AutoStartRegistration.RunKey, writable: true);
            if (enabled)
            {
                var exe = Exe ?? throw new InvalidOperationException("Не известен путь к exe");
                key.SetValue(AutoStartRegistration.ValueName, AutoStartRegistration.Command(exe), RegistryValueKind.String);
                log.Write($"Автозапуск включён: {exe}");
            }
            else
            {
                key.DeleteValue(AutoStartRegistration.ValueName, throwOnMissingValue: false);
                log.Write("Автозапуск выключен");
            }
            return true;
        }
        catch (Exception ex)
        {
            log.Write("Не удалось изменить автозапуск: " + ex);
            return false;
        }
    }

    /// <summary>Программу перенесли в другую папку — автозапуск указывает на старый exe: переписать.</summary>
    public static void RepairIfMoved(FileLog log)
    {
        var command = ReadCommand();
        if (string.IsNullOrWhiteSpace(command) || Exe is not { } exe) return;
        if (AutoStartRegistration.PointsTo(command, exe)) return;
        // Старый exe на месте — это другая копия программы; её автозапуск не трогаем.
        if (AutoStartRegistration.ExeOf(command) is { } old && File.Exists(old)) return;
        log.Write($"Автозапуск указывал на {command} — переписываю на {exe}");
        TrySet(true, log);
    }
}
