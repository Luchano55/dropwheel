namespace DropWheel.Shell;

/// <summary>
/// Автозапуск с Windows: значение DropWheel в HKCU\…\Run (без прав администратора).
/// Здесь только что писать и как понять, что запись наша, — работу с реестром делает приложение.
/// </summary>
public static class AutoStartRegistration
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "DropWheel";
    /// <summary>Ключ командной строки: запуск при входе в Windows — без подсказки «DropWheel работает».</summary>
    public const string Argument = "--autostart";

    /// <summary>Команда автозапуска: "C:\…\DropWheel.exe" --autostart.</summary>
    public static string Command(string exePath) => $"\"{exePath}\" {Argument}";

    /// <summary>Записанная команда запускает именно этот exe (регистр букв и кавычки не важны).</summary>
    public static bool PointsTo(string? command, string exePath)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;
        var exe = ExeOf(command);
        return exe is not null && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Путь к exe из командной строки: в кавычках или до первого пробела.</summary>
    public static string? ExeOf(string command)
    {
        var c = command.Trim();
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            return end > 1 ? c[1..end] : null;
        }
        var space = c.IndexOf(' ');
        return space < 0 ? c : c[..space];
    }
}
