using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>Движок без внешнего процесса: ZIP (.NET), Office (COM).</summary>
public interface IInternalEngine
{
    EngineKind Kind { get; }

    /// <summary>Проверка до запуска: есть ли всё нужное (например, установлен ли Office).</summary>
    bool IsAvailable { get; }

    /// <param name="output">Временный путь результата (файл или уже созданная пустая папка для ResultKind.Folder).</param>
    Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct);
}
