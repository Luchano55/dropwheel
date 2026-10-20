namespace DropWheel.Engines;

/// <summary>Ошибка конвертации: Message — коротко для уведомления, Details — для лога.</summary>
public sealed class ConversionException(string message, string? details = null, Exception? inner = null)
    : Exception(message, inner)
{
    public string? Details { get; } = details;
}
