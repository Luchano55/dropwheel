using System.Runtime.InteropServices;
using static DropWheel.Input.NativeMethods;

namespace DropWheel.Input;

/// <summary>
/// Сдвиг курсора на 1 px туда и обратно (решение D10). В OLE-перетаскивании DragEnter новому окну
/// приходит только после следующего движения мыши; если колесо появилось под неподвижным курсором,
/// сектора построятся с задержкой. Вызывать сразу после показа окна колеса.
/// <para>
/// НУЖНО ПРОВЕРИТЬ НА WINDOWS (этап 3): доходит ли такой сдвиг до цикла DoDragDrop и приходит ли
/// DragEnter сразу. Если <see cref="Nudge"/> не помогает — попробовать <see cref="NudgeWithInput"/>.
/// </para>
/// </summary>
public static class CursorNudge
{
    /// <summary>SetCursorPos: на 1 px вправо и обратно. Координаты физические (Per-Monitor V2).</summary>
    public static bool Nudge()
    {
        if (!GetCursorPos(out var p)) return false;
        SetCursorPos(p.X + 1, p.Y);
        // Упёрлись в правый край экрана — сдвигаемся влево, чтобы позиция точно сменилась.
        if (GetCursorPos(out var now) && now.X == p.X) SetCursorPos(p.X - 1, p.Y);
        return SetCursorPos(p.X, p.Y);
    }

    /// <summary>
    /// SendInput: относительный сдвиг на +1 и −1 — для системы неотличим от настоящей мыши.
    /// При включённой «повышенной точности указателя» фактический сдвиг может быть 0–2 px.
    /// </summary>
    public static bool NudgeWithInput()
    {
        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = 1, dwFlags = MOUSEEVENTF_MOVE } },
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dx = -1, dwFlags = MOUSEEVENTF_MOVE } },
        };
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }
}
