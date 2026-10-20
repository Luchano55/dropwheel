using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>Движок без внешнего процесса: ZIP (.NET), Office (COM), библиотеки, Edge, OCR.</summary>
public interface IInternalEngine
{
    EngineKind Kind { get; }

    /// <summary>Проверка до запуска: есть ли всё нужное (например, установлен ли Office). Должна быть быстрой — кэшируйте.</summary>
    bool IsAvailable { get; }

    /// <summary>Может ли выполнить именно эту операцию (например, Excel есть, а PowerPoint нет).</summary>
    bool CanRun(Operation op) => IsAvailable;

    /// <summary>
    /// То же для конкретного входного расширения (без точки, в нижнем регистре). Нужно запасным движкам,
    /// которые умеют только часть входов операции: Edge печатает в PDF только DOCX, а не DOC/RTF.
    /// </summary>
    bool CanRun(Operation op, string extension) => CanRun(op);

    /// <summary>
    /// Подготовиться заранее: пользователь видит колесо/меню с этими операциями и сейчас выберет.
    /// Office здесь запускает Word в фоне («прогрев»). Вызывается в потоке UI — не блокировать.
    /// </summary>
    void Prepare(IReadOnlyList<Operation> offered) { }

    /// <param name="output">Временный путь результата (файл или уже созданная пустая папка для ResultKind.Folder).</param>
    Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct);
}
