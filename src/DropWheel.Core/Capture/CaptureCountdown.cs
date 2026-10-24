namespace DropWheel.Capture;

/// <summary>
/// Снимок с задержкой (D-CAPTURE): пункты трея «Снимок через 3 с» и «через 5 с». Раз в секунду — новая цифра
/// на плашке у курсора («3…», «2…», «1…»), затем плашка прячется и только после этого делается стоп-кадр,
/// чтобы она не попала в снимок.
/// </summary>
public static class CaptureCountdown
{
    /// <summary>Задержки пунктов трея, секунды.</summary>
    public static IReadOnlyList<int> TrayDelays { get; } = [3, 5];

    /// <summary>Спрятать плашку и подождать, пока окно исчезнет с экрана (DWM), — потом снимать.</summary>
    public static readonly TimeSpan HideBeforeGrab = TimeSpan.FromMilliseconds(200);

    /// <summary>Подписи по секундам: 3 → «3…», «2…», «1…». Меньше 1 — пусто (снимаем сразу); больше 60 — 60.</summary>
    public static IReadOnlyList<string> Steps(int seconds)
    {
        seconds = Math.Clamp(seconds, 0, 60);
        var steps = new string[seconds];
        for (var i = 0; i < seconds; i++) steps[i] = (seconds - i).ToString(System.Globalization.CultureInfo.InvariantCulture) + "…";
        return steps;
    }
}
