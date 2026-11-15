using System.Windows.Threading;
using DropWheel.Capture;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Input;
using DropWheel.Localization;

namespace DropWheel.App;

/// <summary>
/// Запись экрана (решение D-RECORD): сочетание (по умолчанию Ctrl+Alt+R) или пункт трея → то же выделение, что у снимка
/// (область / щелчок — окно / Enter — монитор) → «Записать» → ffmpeg (gdigrab) пишет MP4; красная рамка и пилюля
/// «● 00:12 Стоп» скрыты от захвата. Стоп — пилюля, пункт трея или то же сочетание; потом — MP4 (или GIF из него)
/// на свободное имя «Запись ….mp4» и уведомление.
/// </summary>
public partial class App
{
    private GlobalHotkey? _recordHotkey;
    private const int RecordHotkeyId = 3;
    private readonly RecordSession _recordSession = new();
    private ScreenRecorder? _recorder;
    private (RecordFormat Format, string Folder, string Stem)? _recordTarget;
    private RecordFrameWindow? _recordFrame;
    private RecordPillWindow? _recordPill;
    /// <summary>Что записываем сейчас — чтобы при сбое микрофона перезапустить ту же запись без звука.</summary>
    private (PixelRect Region, IReadOnlyList<CaptureMonitor> Monitors, string Folder, string? Microphone, DateTime Started)? _recordRun;
    /// <summary>ffmpeg с микрофоном вышел раньше этого — микрофон не открылся (занят, запрещён): пишем без звука.</summary>
    private static readonly TimeSpan MicFailureWindow = TimeSpan.FromSeconds(3);
    private DispatcherTimer? _recordTimer;

    /// <summary>Сколько ждать, пока ffmpeg допишет файл после «q», прежде чем убить его.</summary>
    private static readonly TimeSpan RecordStopTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Оверлей выделения ещё гаснет на экране — ffmpeg запускаем чуть позже, чтобы он не попал в первый кадр.</summary>
    private static readonly TimeSpan RecordStartDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>При запуске: сочетание и «Обрезать…» из уведомления о записи.</summary>
    private void StartRecordFeature()
    {
        SetRecordHotkey(Settings.RecordHotkey, Settings.RecordKeys);
        if (_notifier is not null)
            _notifier.TrimRequested += path => Dispatcher.BeginInvoke(() => OpenRecordTrim(path));
    }

    /// <summary>Зарегистрировать сочетание записи (или снять). Занято — запись в журнал, пункт трея работает всё равно.</summary>
    private void SetRecordHotkey(bool on, string keys)
    {
        try
        {
            if (!on)
            {
                _recordHotkey?.Dispose();
                _recordHotkey = null;
                return;
            }
            var chord = KeyChord.Parse(keys) ?? ScreenRecord.DefaultChord;
            _recordHotkey ??= new GlobalHotkey(RecordHotkeyId, () => Dispatcher.BeginInvoke(ToggleRecording));
            if (_recordHotkey.TryRegister(chord.Modifiers, (uint)chord.Key, out var error))
                Log.Write($"{chord} (запись экрана) включена");
            else
                Log.Write($"{chord} (запись экрана) занята другой программой или недоступна (код {error}) — работаю без неё");
        }
        catch (Exception ex)
        {
            Log.Write("Не удалось включить сочетание для записи экрана: " + ex);
        }
    }

    /// <summary>Сочетание: в покое — выделение, во время записи — стоп.</summary>
    private void ToggleRecording()
    {
        switch (_recordSession.Toggle())
        {
            case RecordCommand.Select:
                BeginRecordSelection();
                break;
            case RecordCommand.Stop:
                StopRecording();
                break;
        }
    }

    /// <summary>Пункт трея «Записать экран» / «Остановить запись». Выделение — чуть позже, чтобы меню не попало в стоп-кадр.</summary>
    internal void RecordFromTray()
    {
        if (_recordSession.Toggle() == RecordCommand.Stop)
        {
            StopRecording();
            return;
        }
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TrayCaptureDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (_recordSession.Toggle() == RecordCommand.Select) BeginRecordSelection();
        };
        timer.Start();
    }

    /// <summary>Оверлей снимка в режиме записи. Уже открыт оверлей (снимок или запись) — второй не открываем.</summary>
    private void BeginRecordSelection()
    {
        if (_capture is not null || !_recordSession.BeginSelect()) return;
        try
        {
            var frame = ScreenCapture.Take();
            var overlay = new CaptureOverlay(frame, ocr: false, Log, Settings.RecordGif ? RecordFormat.Gif : RecordFormat.Mp4,
                Settings.RecordMicrophone);
            overlay.RecordRequested += OnRecordRequested;
            overlay.Closed += () =>
            {
                _capture = null;
                // «Записать» приходит сразу после закрытия — отмену проверяем позже, когда она уже переключила состояние.
                Dispatcher.BeginInvoke(_recordSession.CancelSelect);
                ReleaseCaptureMemory();
            };
            _capture = overlay;
            overlay.Show();
        }
        catch (Exception ex)
        {
            Log.Write("Запись экрана: выделение не открылось: " + ex);
            _capture?.Close();
            _capture = null;
            _recordSession.CancelSelect();
            _tray?.ShowInfo(Loc.T.RecordFailed, ex.Message);
        }
    }

    private void OnRecordRequested(PixelRect region, IReadOnlyList<CaptureMonitor> monitors, RecordFormat format, bool microphone)
    {
        if (!_recordSession.Start(DateTime.Now)) return;
        UpdateSettings(s => s with { RecordGif = format == RecordFormat.Gif, RecordMicrophone = microphone });
        var folder = ScreenRecord.Folder(Settings.ClipboardFolder, Settings.Conversion);
        _recordTarget = (format, folder, ScreenRecord.FileStem(DateTime.Now));
        // Микрофон ищем, пока оверлей исчезает (запуск ffmpeg со списком устройств — доли секунды). У GIF звука нет.
        var withSound = microphone && format == RecordFormat.Mp4;
        var tools = new ToolLocator { FfmpegPath = Settings.FfmpegPath, MagickPath = Settings.MagickPath };
        var preferred = Settings.RecordMicrophoneDevice;
        var device = withSound
            ? Task.Run(() => Microphones.Find(tools, preferred, Log.Write))
            : Task.FromResult<string?>(null);
        var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = RecordStartDelay };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            var mic = await device;
            if (withSound && mic is null) _tray?.ShowInfo(Loc.T.TrayRecord, Loc.T.RecordMicMissing);
            StartRecorder(region, monitors, folder, mic);
        };
        timer.Start();
    }

    private void StartRecorder(PixelRect region, IReadOnlyList<CaptureMonitor> monitors, string folder, string? microphone)
    {
        var area = ScreenRecord.EvenSize(region);
        ScreenRecorder recorder;
        try
        {
            var tools = new ToolLocator { FfmpegPath = Settings.FfmpegPath, MagickPath = Settings.MagickPath };
            recorder = ScreenRecorder.Start(tools, folder, temp => ScreenRecord.Arguments(area, temp, microphone), Log.Write);
            _recordRun = (region, monitors, folder, microphone, DateTime.Now);
        }
        catch (Exception ex)
        {
            Log.Write("Запись экрана не началась: " + ex);
            _notifier?.RecordFailed(ex.Message);
            _recordTarget = null;
            _recordSession.Finished();
            return;
        }
        _recorder = recorder;
        Log.Write($"Запись экрана: {area} → {folder}");
        try
        {
            _recordFrame = new RecordFrameWindow(area);
            _recordFrame.ShowAround();
            _recordPill = new RecordPillWindow(area, monitors);
            _recordPill.View.StopRequested += StopRecording;
            _recordPill.ShowNearRegion();
            Log.Write($"Запись экрана: рамка и пилюля скрыты от захвата — {_recordFrame.Excluded && _recordPill.Excluded}");
        }
        catch (Exception ex)
        {
            // Без рамки запись всё равно идёт; остановить можно из трея или сочетанием.
            Log.Write("Запись экрана: рамка или пилюля не показались: " + ex);
        }
        _recordTimer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = TimeSpan.FromMilliseconds(250) };
        _recordTimer.Tick += (_, _) => TickRecording();
        _recordTimer.Start();
        TickRecording();
        recorder.Exited.ContinueWith(_ => Dispatcher.BeginInvoke(() => OnRecorderExited(recorder)), TaskScheduler.Default);
    }

    private void TickRecording()
    {
        if (_recordSession.State != RecordState.Recording) return;
        var elapsed = _recordSession.Elapsed(DateTime.Now);
        if (_recordPill is not null) _recordPill.View.Elapsed = elapsed;
        _tray?.SetRecording(Loc.F(Loc.T.RecordTrayStatus, ScreenRecord.FormatElapsed(elapsed)));
    }

    /// <summary>«Стоп»: «q» ffmpeg; файл сохраняется, когда он выйдет (<see cref="OnRecorderExited"/>).</summary>
    private void StopRecording()
    {
        if (!_recordSession.Stop()) return;
        Log.Write($"Запись экрана: стоп ({ScreenRecord.FormatElapsed(_recordSession.Elapsed(DateTime.Now))})");
        CloseRecordWindows();
        _tray?.SetRecording(Loc.T.RecordSaving);
        if (_recorder is { } recorder) _ = recorder.StopAsync(RecordStopTimeout);
    }

    private void OnRecorderExited(ScreenRecorder recorder)
    {
        if (!ReferenceEquals(recorder, _recorder)) return;
        // С микрофоном ffmpeg вышел сразу, а «Стоп» не нажимали: микрофон не открылся — та же запись без звука.
        if (_recordRun is { Microphone: not null } run && _recordSession.State == RecordState.Recording
            && DateTime.Now - run.Started < MicFailureWindow)
        {
            Log.Write($"Запись экрана: с микрофоном «{run.Microphone}» не началась — пишу без звука\n{recorder.StderrTail}");
            CloseRecordWindows();
            _recorder = null;
            recorder.Dispose();
            _tray?.ShowInfo(Loc.T.TrayRecord, Loc.T.RecordMicFailed);
            StartRecorder(run.Region, run.Monitors, run.Folder, null);
            return;
        }
        var elapsed = _recordSession.Elapsed(DateTime.Now);
        // ffmpeg вышел сам: сработал предел длины или ошибка (монитор отключили, захват недоступен).
        if (_recordSession.Stop())
        {
            Log.Write($"Запись экрана: ffmpeg завершился сам через {ScreenRecord.FormatElapsed(elapsed)}");
            CloseRecordWindows();
            _tray?.SetRecording(Loc.T.RecordSaving);
            if (elapsed >= ScreenRecord.MaxDuration - TimeSpan.FromSeconds(5))
                _tray?.ShowInfo(Loc.T.RecordSaved, Loc.F(Loc.T.RecordLimit, (int)ScreenRecord.MaxDuration.TotalMinutes));
        }
        _ = SaveRecordingAsync(recorder);
    }

    private async Task SaveRecordingAsync(ScreenRecorder recorder)
    {
        try
        {
            if (_recordTarget is not { } target) return;
            var path = await recorder.SaveAsync(target.Format, target.Folder, target.Stem, Queue.Converter);
            Log.Write($"Запись экрана сохранена: {path}");
            if (_notifier is not null) await _notifier.RecordSavedAsync(path, canTrim: target.Format == RecordFormat.Mp4);
        }
        catch (Exception ex)
        {
            Log.Write("Запись экрана не сохранилась: " + (ex is ConversionException { Details: { } d } ? ex.Message + "\n" + d : ex.ToString()));
            _notifier?.RecordFailed(ex.Message);
        }
        finally
        {
            recorder.Dispose();
            if (ReferenceEquals(_recorder, recorder))
            {
                _recorder = null;
                _recordTarget = null;
                _recordRun = null;
                _recordSession.Finished();
                _tray?.SetRecording(null);
            }
        }
    }

    private void CloseRecordWindows()
    {
        _recordTimer?.Stop();
        _recordTimer = null;
        _recordFrame?.Close();
        _recordFrame = null;
        _recordPill?.Close();
        _recordPill = null;
    }

    /// <summary>«Обрезать…» в уведомлении о записи — окно обрезки видео, как с колеса.</summary>
    private void OpenRecordTrim(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            var tools = new ToolLocator { FfmpegPath = Settings.FfmpegPath, MagickPath = Settings.MagickPath };
            new TrimWindow(path, tools, Log, bound => Queue.EnqueueSelection([path], bound)).ShowAndActivate();
        }
        catch (Exception ex)
        {
            Log.Write("Окно обрезки записи не открылось: " + ex);
        }
    }

    /// <summary>
    /// Выход из программы во время записи: «q» и подождать, MP4 — на место (GIF делать некогда). Файл, который уже
    /// сохраняется, не трогаем.
    /// </summary>
    private void ShutdownRecording()
    {
        _recordHotkey?.Dispose();
        _recordHotkey = null;
        CloseRecordWindows();
        if (_recorder is not { } recorder || _recordTarget is not { } target) return;
        _recorder = null;
        if (recorder.HasExited && _recordSession.State == RecordState.Finalizing) return;
        try
        {
            recorder.Stop(RecordStopTimeout);
            if (recorder.SaveMp4Now(target.Folder, target.Stem) is { } path) Log.Write($"Запись экрана сохранена при выходе: {path}");
        }
        catch (Exception ex)
        {
            Log.Write("Запись экрана при выходе: " + ex.Message);
        }
        finally
        {
            recorder.Dispose();
        }
    }
}
