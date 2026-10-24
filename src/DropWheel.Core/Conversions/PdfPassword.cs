namespace DropWheel.Conversions;

/// <summary>
/// Пароль на PDF (решение D-PDFPASS): «Защитить…» (qpdf, AES-256, пароль на открытие = пароль владельца)
/// и «Снять пароль…» (qpdf --decrypt). Пароль — параметр-секрет задачи: его спрашивает окно (или <c>--password</c>
/// в CLI), он живёт только в памяти, пока идёт задача, и не пишется ни в журнал, ни в настройки.
/// </summary>
public static class PdfPassword
{
    public const string ProtectId = "pdf-protect";
    public const string UnlockId = "pdf-unlock";

    public const string Key = "password";
    public static readonly IReadOnlyList<string> ParameterNames = [Key];

    public static bool IsPasswordOperation(string id) =>
        string.Equals(id, ProtectId, StringComparison.OrdinalIgnoreCase) || string.Equals(id, UnlockId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Пароль подходит: не пустой и не из одних пробелов, одной строкой (перевод строки не передать утилите).
    /// Пробелы внутри и по краям — часть пароля.
    /// </summary>
    public static bool IsValid(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.IndexOfAny(['\r', '\n', '\0']) < 0;

    /// <summary>Окно «Защитить»: «ОК» доступна, когда пароль подходит и повтор совпадает.</summary>
    public static bool CanProtect(string? password, string? repeat) =>
        IsValid(password) && string.Equals(password, repeat, StringComparison.Ordinal);

    /// <summary>Показать «Пароли не совпадают»: повтор уже начали вводить, и он другой.</summary>
    public static bool ShowMismatch(string? password, string? repeat) =>
        !string.IsNullOrEmpty(repeat) && !string.Equals(password, repeat, StringComparison.Ordinal);

    /// <summary>Операция с паролем для одной задачи. Неподходящий пароль — ArgumentException (текст без пароля).</summary>
    public static Operation Bind(bool protect, string password)
    {
        if (!IsValid(password)) throw new ArgumentException("Пустой или многострочный пароль", nameof(password));
        var op = OperationCatalog.Find(protect ? ProtectId : UnlockId)!;
        return op.WithValues(new Dictionary<string, string> { [Key] = password });
    }
}
