using DropWheel.Engines;
using DropWheel.Logging;

namespace DropWheel.AppEngines;

/// <summary>
/// Встроенные движки приложения (Windows): общий ZIP из ядра + то, что есть только в Windows
/// (Office через COM, печать в PDF через Edge, распознавание текста). Новый движок — одна строка.
/// </summary>
internal static class EngineRegistry
{
    public static IReadOnlyList<IInternalEngine> Create(FileLog log) =>
    [
        new ZipEngine(),
        new OfficeEngine(log.Write),
    ];
}
