using System.Windows.Media;

namespace DropWheel.Ui;

/// <summary>
/// Анимации интерфейса: включены ли, кривые, покадровый таймер.
/// Если в Windows выключены «Эффекты анимации» — всё сразу встаёт в конечное состояние.
/// </summary>
public static class Motion
{
    /// <summary>Выключить анимации принудительно — для скриншот-тестов (кадр должен быть конечным).</summary>
    public static bool ForceDisabled { get; set; }

    /// <summary>Переменная окружения DROPWHEEL_NO_ANIMATIONS=1 тоже выключает анимации.</summary>
    public static bool Enabled =>
        !ForceDisabled && SystemTheme.AnimationsEnabled
        && Environment.GetEnvironmentVariable("DROPWHEEL_NO_ANIMATIONS") is not "1";

    public static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;

    public static double EaseOutCubic(double t)
    {
        t = 1 - Clamp01(t);
        return 1 - t * t * t;
    }

    public static double EaseInQuad(double t)
    {
        t = Clamp01(t);
        return t * t;
    }

    /// <summary>Шаг значения к цели за dt мс при полной длительности durationMs (линейно).</summary>
    public static double Approach(double value, double target, double dtMs, double durationMs)
    {
        var step = dtMs / durationMs;
        return value < target ? Math.Min(target, value + step) : Math.Max(target, value - step);
    }
}

/// <summary>
/// Вызов tick на каждом кадре отрисовки WPF (CompositionTarget.Rendering), пока tick возвращает true.
/// Аргумент — миллисекунды с прошлого кадра (не больше 50, чтобы рывок не «съел» анимацию).
/// </summary>
public sealed class FrameTicker(Func<double, bool> tick)
{
    private bool _running;
    private int _generation;
    private TimeSpan _last;

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _generation++;
        _last = TimeSpan.MinValue;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = e is RenderingEventArgs r ? r.RenderingTime : TimeSpan.FromTicks(DateTime.UtcNow.Ticks);
        double dt;
        if (_last == TimeSpan.MinValue) dt = 16.7;
        else
        {
            dt = (now - _last).TotalMilliseconds;
            // Rendering бывает несколько раз за кадр с тем же временем — пропускаем.
            if (dt <= 0) return;
        }
        _last = now;
        // tick мог перезапустить таймер (новая анимация в done) — тогда не останавливаем.
        var generation = _generation;
        bool more;
        try { more = tick(Math.Min(dt, 50)); }
        catch (Exception) { more = false; }
        if (!more && generation == _generation) Stop();
    }
}

/// <summary>
/// Анимация от 0 до 1 за заданное время: update(t) с уже применённой кривой, затем done.
/// Без анимаций (Motion.Enabled = false) — сразу update(1) и done.
/// </summary>
public sealed class Tween
{
    private readonly FrameTicker _ticker;
    private double _elapsed;
    private double _duration;
    private Func<double, double> _ease = Motion.EaseOutCubic;
    private Action<double>? _update;
    private Action? _done;

    public Tween() => _ticker = new FrameTicker(Tick);

    public bool IsRunning => _ticker.IsRunning;

    public void Run(double durationMs, Func<double, double> ease, Action<double> update, Action? done = null)
    {
        _ticker.Stop();
        _duration = durationMs;
        _ease = ease;
        _update = update;
        _done = done;
        _elapsed = 0;
        if (!Motion.Enabled || durationMs <= 0)
        {
            Finish();
            return;
        }
        update(ease(0));
        _ticker.Start();
    }

    /// <summary>Остановить; complete = true — сразу в конечное состояние (с done).</summary>
    public void Stop(bool complete)
    {
        if (!_ticker.IsRunning) return;
        _ticker.Stop();
        if (complete) Finish();
    }

    private bool Tick(double dt)
    {
        _elapsed += dt;
        if (_elapsed >= _duration)
        {
            Finish();
            return false;
        }
        _update?.Invoke(_ease(_elapsed / _duration));
        return true;
    }

    private void Finish()
    {
        var update = _update;
        var done = _done;
        _update = null;
        _done = null;
        update?.Invoke(_ease(1));
        done?.Invoke();
    }
}
