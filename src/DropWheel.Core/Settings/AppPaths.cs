namespace DropWheel.Settings;

/// <summary>
/// Где лежат данные приложения: %AppData%\DropWheel (настройки, логи).
/// Переменная окружения DROPWHEEL_DATA_DIR подменяет папку — для тестов.
/// </summary>
public static class AppPaths
{
    public const string DataDirVariable = "DROPWHEEL_DATA_DIR";

    // Вычисляется при каждом обращении, чтобы тесты могли менять переменную на лету.
    public static string DataDir =>
        Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropWheel");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");

    public static string LogsDir => Path.Combine(DataDir, "logs");
}
