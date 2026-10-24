using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DropWheel.AppEngines;
using DropWheel.Capture;
using DropWheel.Input;
using DropWheel.Localization;
using DropWheel.Shell;

namespace DropWheel.App;

/// <summary>
/// Снимок экрана (решение D-CAPTURE): сочетание (по умолчанию Ctrl+Alt+S) или пункт трея → стоп-кадр всех мониторов →
/// выделение → «Копировать» / «Сохранить» / «Колесо» / «Текст». Ничего не работает в фоне: снимок живёт только
/// пока открыт оверлей, после действия память возвращается.
/// </summary>
public partial class App
{
    private GlobalHotkey? _captureHotkey;
    private const int CaptureHotkeyId = 2;
    private CaptureOverlay? _capture;

    /// <summary>Пункт трея: меню ещё гаснет на экране — снимаем чуть позже, чтобы оно не попало в кадр.</summary>
    private static readonly TimeSpan TrayCaptureDelay = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// Зарегистрировать сочетание снимка (или снять). Занято другой программой или Windows — запись в журнал,
    /// пункт трея «Снимок экрана» работает всё равно.
    /// </summary>
    private void SetCaptureHotkey(bool on, string keys)
    {
        try
        {
            if (!on)
            {
                _captureHotkey?.Dispose();
                _captureHotkey = null;
                return;
            }
            var chord = KeyChord.Parse(keys) ?? KeyChord.Default;
            // Обработчик WM_HOTKEY только ставит снимок в очередь — сообщение отпускаем сразу.
            _captureHotkey ??= new GlobalHotkey(CaptureHotkeyId, () => Dispatcher.BeginInvoke(BeginCapture));
            if (_captureHotkey.TryRegister(chord.Modifiers, (uint)chord.Key, out var error))
                Log.Write($"{chord} (снимок экрана) включена");
            else
                Log.Write($"{chord} (снимок экрана) занята другой программой или недоступна (код {error}) — работаю без неё");
        }
        catch (Exception ex)
        {
            Log.Write("Не удалось включить сочетание для снимка экрана: " + ex);
        }
    }

    /// <summary>Пункт трея «Снимок экрана».</summary>
    internal void CaptureFromTray()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TrayCaptureDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            BeginCapture();
        };
        timer.Start();
    }

    /// <summary>Стоп-кадр (до появления оверлея) и оверлей. Уже открыт — второй не открываем.</summary>
    private void BeginCapture()
    {
        if (_capture is not null) return;
        try
        {
            var frame = ScreenCapture.Take();
            var ocr = _engines.OfType<OcrEngine>().FirstOrDefault()?.IsAvailable == true;
            var overlay = new CaptureOverlay(frame, ocr, Log);
            overlay.Finished += OnCaptured;
            overlay.Closed += () =>
            {
                _capture = null;
                ReleaseCaptureMemory();
            };
            _capture = overlay;
            overlay.Show();
        }
        catch (Exception ex)
        {
            Log.Write("Снимок экрана не удался: " + ex);
            _capture?.Close();
            _capture = null;
            _tray?.ShowInfo(Loc.T.CaptureFailed, ex.Message);
        }
    }

    private void OnCaptured(BitmapSource image, CaptureAction action)
    {
        try
        {
            switch (action)
            {
                case CaptureAction.Copy:
                    if (CopyImage(image)) _tray?.ShowInfo(Loc.T.CaptureCopied, Loc.T.CaptureCopiedHint);
                    break;
                case CaptureAction.Save:
                    if (SaveCapture(image) is { } saved) _notifier?.Done(saved);
                    break;
                case CaptureAction.Wheel:
                    if (SaveCapture(image) is { } path)
                    {
                        // Как картинка из буфера: «Оставлять исходную картинку» действует и на снимок (D-CLIPBOARD).
                        _clipboardImages.Add(path);
                        ShowChooser([path]);
                    }
                    break;
                case CaptureAction.Text:
                    _ = RecognizeCaptureAsync(image);
                    break;
                case CaptureAction.Annotate:
                    AnnotateCapture(image);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write($"Снимок экрана: {action} не удалось: " + ex);
            _tray?.ShowInfo(Loc.T.CaptureFailed, ex.Message);
        }
    }

    /// <summary>PNG в папку картинок из буфера (своя папка → «В одну папку» → «Изображения\DropWheel»); null — не вышло.</summary>
    private string? SaveCapture(BitmapSource image)
    {
        try
        {
            var folder = ClipboardPlan.ImageFolder(Settings.ClipboardFolder, Settings.Conversion);
            Directory.CreateDirectory(folder);
            var path = CaptureNaming.FilePath(folder, DateTime.Now);
            ScreenCapture.SavePng(image, path);
            Log.Write($"Снимок экрана сохранён: {path}");
            return path;
        }
        catch (Exception ex)
        {
            Log.Write("Снимок экрана не сохранился: " + ex);
            _tray?.ShowInfo(Loc.T.CaptureFailed, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Картинку — в буфер обмена: Bitmap (для всех программ) и PNG (браузеры, мессенджеры, Office берут его без потерь).
    /// Буфер занят — несколько попыток.
    /// </summary>
    private bool CopyImage(BitmapSource image)
    {
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);
        var data = new System.Windows.DataObject();
        data.SetImage(image);
        data.SetData("PNG", png);
        return SetClipboard(() => System.Windows.Clipboard.SetDataObject(data, copy: true));
    }

    /// <summary>«Текст»: OCR Windows по снимку (временный PNG в %TEMP%\DropWheel, сразу удаляется) → текст в буфер.</summary>
    private async Task RecognizeCaptureAsync(BitmapSource image)
    {
        var dir = Path.Combine(Path.GetTempPath(), "DropWheel");
        var path = Path.Combine(dir, "capture-" + Guid.NewGuid().ToString("N")[..12] + ".png");
        try
        {
            Directory.CreateDirectory(dir);
            ScreenCapture.SavePng(image, path);
            var lines = await Task.Run(() => OcrEngine.RecognizeLinesAsync(path, Log.Write, CancellationToken.None));
            if (lines.Count == 0)
            {
                _tray?.ShowInfo(Loc.T.CaptureText, Loc.T.CaptureNoText);
                return;
            }
            if (SetClipboard(() => System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, lines))))
                _tray?.ShowInfo(Loc.T.CaptureTextCopied, Loc.F(Loc.T.CaptureTextLines, lines.Count));
        }
        catch (Exception ex)
        {
            Log.Write("Снимок экрана: текст не распознан: " + ex);
            _tray?.ShowInfo(Loc.T.CaptureFailed, ex.Message);
        }
        finally
        {
            try { File.Delete(path); }
            catch (Exception) { }
            ReleaseCaptureMemory();
        }
    }

    private bool SetClipboard(Action set)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                set();
                return true;
            }
            catch (ExternalException ex)
            {
                if (attempt >= 5)
                {
                    Log.Write("Буфер обмена занят: " + ex.Message);
                    _tray?.ShowInfo(Loc.T.CaptureFailed, Loc.T.CaptureClipboardFailed);
                    return false;
                }
                Thread.Sleep(100);
            }
        }
    }

    /// <summary>
    /// Снимок всех мониторов — десятки мегабайт. После закрытия оверлея (и после OCR) — одна сборка мусора
    /// с уплотнением больших объектов, когда программа простаивает, чтобы память вернулась сразу, а не «когда-нибудь».
    /// </summary>
    private void ReleaseCaptureMemory() => Dispatcher.BeginInvoke(() =>
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }, DispatcherPriority.ApplicationIdle);
}
