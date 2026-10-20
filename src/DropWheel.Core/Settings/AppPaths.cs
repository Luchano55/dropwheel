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

    /// <summary>
    /// Профиль встроенного Edge (WebView2) для печати в PDF: %LocalAppData%\DropWheel\WebView2 —
    /// кэш браузера не место в перемещаемом профиле (%AppData%). С DROPWHEEL_DATA_DIR — внутри неё.
    /// </summary>
    public static string WebView2Dir =>
        Environment.GetEnvironmentVariable(DataDirVariable) is { Length: > 0 } custom
            ? Path.Combine(custom, "WebView2")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropWheel", "WebView2");
}
