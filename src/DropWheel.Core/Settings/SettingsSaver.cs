namespace DropWheel.Settings;

/// <summary>
/// Отложенная запись settings.json: окно настроек меняет значения «на лету» (ползунок, порядок лепестков),
/// а на диск уходит только последнее значение — через Delay после последней правки.
/// Flush — записать сразу (выход из программы). Ошибки записи не бросает, а пишет в журнал.
/// </summary>
public sealed class SettingsSaver(string path, TimeSpan delay, Action<string>? log = null) : IDisposable
{
    private readonly object _gate = new();
    private Timer? _timer;
    private AppSettings? _pending;

    /// <summary>Запомнить настройки и записать их чуть позже (новая правка откладывает запись).</summary>
    public void Schedule(AppSettings settings)
    {
        lock (_gate)
        {
            _pending = settings;
            _timer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Есть незаписанные настройки.</summary>
    public bool HasPending
    {
        get { lock (_gate) return _pending is not null; }
    }

    /// <summary>Записать отложенное прямо сейчас (если есть).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            if (_pending is not { } settings) return;
            _pending = null;
            try
            {
                settings.Save(path);
            }
            catch (Exception ex)
            {
                log?.Invoke("Не удалось сохранить настройки: " + ex.Message);
            }
        }
    }

    public void Dispose()
    {
        Flush();
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }
}
