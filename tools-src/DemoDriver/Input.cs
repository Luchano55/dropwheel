using System.Diagnostics;
using System.Runtime.InteropServices;
using static DemoDriver.Native;

namespace DemoDriver;

/// <summary>
/// Мышь через SendInput: для Windows неотличима от настоящей (проводник начинает перетаскивание,
/// хуки DropWheel её видят). Движение — по плавной кривой Безье с разгоном и торможением, ~60 шагов в секунду.
/// Все координаты — физические пиксели экрана.
/// </summary>
internal static class Mouse
{
    private const double FrameMs = 1000.0 / 60;
    // Фиксированное зерно: ролик от запуска к запуску одинаковый.
    private static readonly Random Rng = new(20261020);

    static Mouse()
    {
        // Точность Thread.Sleep 1 мс вместо 15,6 мс — иначе движение дёрганое.
        try { timeBeginPeriod(1); } catch { }
    }

    public static (int X, int Y) Position
    {
        get
        {
            GetCursorPos(out var p);
            return (p.X, p.Y);
        }
    }

    /// <summary>Мгновенно поставить курсор (абсолютное движение мыши).</summary>
    public static void MoveTo(double x, double y)
    {
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = Math.Max(2, GetSystemMetrics(SM_CXVIRTUALSCREEN));
        var vh = Math.Max(2, GetSystemMetrics(SM_CYVIRTUALSCREEN));
        // Нормированные координаты 0…65535 по всему виртуальному экрану.
        var nx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
        var ny = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
        Send(new MOUSEINPUT
        {
            dx = nx,
            dy = ny,
            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | MOUSEEVENTF_MOVE_NOCOALESCE,
        });
    }

    /// <summary>
    /// Плавно довести курсор до точки, как рука: кубическая кривая Безье с небольшим изгибом,
    /// разгон и торможение. Длительность по умолчанию растёт с расстоянием.
    /// </summary>
    public static void Glide(double x, double y, double? durationMs = null, double bend = 0.12)
    {
        var (sx, sy) = Position;
        var dx = x - sx;
        var dy = y - sy;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1)
        {
            MoveTo(x, y);
            return;
        }
        var duration = durationMs ?? Math.Clamp(280 + dist * 0.55, 300, 1300);

        // Контрольные точки — на трети и двух третях пути, сдвинуты поперёк на случайную долю расстояния.
        var px = -dy / dist;
        var py = dx / dist;
        var o1 = (Rng.NextDouble() * 2 - 1) * bend * dist;
        var o2 = (Rng.NextDouble() * 2 - 1) * bend * dist;
        var c1 = (X: sx + dx * 0.3 + px * o1, Y: sy + dy * 0.3 + py * o1);
        var c2 = (X: sx + dx * 0.7 + px * o2, Y: sy + dy * 0.7 + py * o2);

        Animate(duration, t =>
        {
            var e = EaseInOut(t);
            var u = 1 - e;
            var bx = u * u * u * sx + 3 * u * u * e * c1.X + 3 * u * e * e * c2.X + e * e * e * x;
            var by = u * u * u * sy + 3 * u * u * e * c1.Y + 3 * u * e * e * c2.Y + e * e * e * y;
            MoveTo(bx, by);
        });
        MoveTo(x, y);
    }

    /// <summary>Провести курсор по дуге окружности (по кольцу колеса) от угла до угла. Углы — от 12 часов по часовой.</summary>
    public static void Arc(double cx, double cy, double radius, double fromDeg, double toDeg, double durationMs)
    {
        Animate(durationMs, t =>
        {
            var a = (fromDeg + (toDeg - fromDeg) * EaseInOut(t)) * Math.PI / 180;
            MoveTo(cx + radius * Math.Sin(a), cy - radius * Math.Cos(a));
        });
    }

    public static void LeftDown() => Send(new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN });

    public static void LeftUp() => Send(new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP });

    public static void Click(int holdMs = 60)
    {
        LeftDown();
        Thread.Sleep(holdMs);
        LeftUp();
    }

    private static double EaseInOut(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    /// <summary>Вызывать step(t), t от 0 до 1, примерно 60 раз в секунду по реальному времени.</summary>
    private static void Animate(double durationMs, Action<double> step)
    {
        var sw = Stopwatch.StartNew();
        var next = 0.0;
        while (true)
        {
            var elapsed = sw.Elapsed.TotalMilliseconds;
            var t = Math.Min(1, elapsed / durationMs);
            step(t);
            if (t >= 1) break;
            next += FrameMs;
            var wait = next - sw.Elapsed.TotalMilliseconds;
            if (wait > 0) Thread.Sleep(TimeSpan.FromMilliseconds(wait));
        }
    }

    private static void Send(MOUSEINPUT input)
    {
        var inputs = new[] { new INPUT { type = INPUT_MOUSE, u = new InputUnion { mi = input } } };
        if (SendInput(1, inputs, Marshal.SizeOf<INPUT>()) != 1)
            Log.Write($"SendInput (мышь) не прошёл: ошибка {Marshal.GetLastWin32Error()}");
    }
}

/// <summary>Клавиатура через SendInput (виртуальные коды + скан-коды).</summary>
internal static class Keyboard
{
    public static void Down(ushort vk) => Send(vk, 0);

    public static void Up(ushort vk) => Send(vk, KEYEVENTF_KEYUP);

    /// <summary>Нажать и отпустить, как человек: клавиша зажата ~80 мс.</summary>
    public static void Tap(ushort vk, int holdMs = 80)
    {
        Down(vk);
        Thread.Sleep(holdMs);
        Up(vk);
    }

    /// <summary>Сочетание: модификаторы вниз, клавиша, модификаторы вверх (в обратном порядке).</summary>
    public static void Chord(ushort key, params ushort[] modifiers)
    {
        foreach (var m in modifiers) { Down(m); Thread.Sleep(30); }
        Tap(key, 60);
        foreach (var m in modifiers.Reverse()) { Thread.Sleep(30); Up(m); }
    }

    private static void Send(ushort vk, uint flags)
    {
        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKeyW(vk, 0), dwFlags = flags },
            },
        };
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1)
            Log.Write($"SendInput (клавиша 0x{vk:X2}) не прошёл: ошибка {Marshal.GetLastWin32Error()}");
    }
}
