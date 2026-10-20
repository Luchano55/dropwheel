using System.Windows.Threading;
using DropWheel.Engines;
using DropWheel.Logging;
using DropWheel.Settings;

namespace DropWheel.AppEngines;

/// <summary>
/// Встроенные движки приложения (Windows): общий ZIP из ядра + то, что есть только в Windows
/// (Office через COM, печать в PDF через Edge, распознавание текста). Новый движок — одна строка.
/// </summary>
internal static class EngineRegistry
{
    /// <param name="dispatcher">Поток интерфейса: там живёт Edge для печати в PDF.</param>
    public static IReadOnlyList<IInternalEngine> Create(FileLog log, Dispatcher dispatcher) =>
    [
        new ZipEngine(),
        new TextEngine(),
        new OcrEngine(log.Write),
        new OfficeEngine(log.Write),
        new DocLibsEngine(),
        new WebPdfEngine(dispatcher, AppPaths.WebView2Dir, log.Write),
    ];
}
