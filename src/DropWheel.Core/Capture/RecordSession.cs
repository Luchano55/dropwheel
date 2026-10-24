namespace DropWheel.Capture;

/// <summary>Состояние записи экрана.</summary>
public enum RecordState
{
    /// <summary>Ничего не происходит.</summary>
    Idle,
    /// <summary>Открыт оверлей выделения в режиме записи.</summary>
    Selecting,
    /// <summary>ffmpeg пишет.</summary>
    Recording,
    /// <summary>Остановлено: ffmpeg дописывает файл, делается GIF, файл переносится на место.</summary>
    Finalizing,
}

/// <summary>Что сделать по сочетанию (или пункту трея) в текущем состоянии.</summary>
public enum RecordCommand
{
    None,
    /// <summary>Открыть выделение области.</summary>
    Select,
    /// <summary>Остановить запись.</summary>
    Stop,
}

/// <summary>
/// Машина состояний записи (решение D-RECORD): Idle → Selecting → Recording → Finalizing → Idle.
/// Одно и то же сочетание начинает выделение и останавливает запись; пока файл дописывается — ничего не делает.
/// Ни окон, ни процессов — только правила переходов (тесты).
/// </summary>
public sealed class RecordSession
{
    public RecordState State { get; private set; } = RecordState.Idle;

    /// <summary>Когда началась запись (для таймера на пилюле); null — не пишем.</summary>
    public DateTime? StartedAt { get; private set; }

    /// <summary>Сочетание или пункт трея: в покое — выделение, во время записи — стоп, иначе — ничего.</summary>
    public RecordCommand Toggle() => State switch
    {
        RecordState.Idle => RecordCommand.Select,
        RecordState.Recording => RecordCommand.Stop,
        _ => RecordCommand.None,
    };

    /// <summary>Открыть выделение: только из покоя.</summary>
    public bool BeginSelect()
    {
        if (State != RecordState.Idle) return false;
        State = RecordState.Selecting;
        return true;
    }

    /// <summary>Выделение закрыто без «Записать» (Esc, правый щелчок, ушли в другое окно).</summary>
    public void CancelSelect()
    {
        if (State == RecordState.Selecting) State = RecordState.Idle;
    }

    /// <summary>Нажали «Записать»: из выделения — в запись.</summary>
    public bool Start(DateTime now)
    {
        if (State != RecordState.Selecting) return false;
        State = RecordState.Recording;
        StartedAt = now;
        return true;
    }

    /// <summary>
    /// Запись закончилась: по «Стоп» или сама (предел длины, ffmpeg упал). true — только что перешли в
    /// <see cref="RecordState.Finalizing"/>; повторный вызов — false.
    /// </summary>
    public bool Stop()
    {
        if (State != RecordState.Recording) return false;
        State = RecordState.Finalizing;
        return true;
    }

    /// <summary>Файл готов (или не вышел): снова покой.</summary>
    public void Finished()
    {
        State = RecordState.Idle;
        StartedAt = null;
    }

    /// <summary>Сколько уже пишем (0 — не пишем).</summary>
    public TimeSpan Elapsed(DateTime now) => StartedAt is { } start && now > start ? now - start : TimeSpan.Zero;
}
