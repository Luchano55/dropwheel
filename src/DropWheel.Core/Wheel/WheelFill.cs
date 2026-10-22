namespace DropWheel.Wheel;

/// <summary>
/// Прозрачность колеса (настройка «Прозрачность колеса», AppSettings.WheelOpacity, решение D-OPACITY):
/// плотность заливки диска, лепестков, центра и кольца в процентах, 40–100.
/// 88 % — исходный вид (плотный диск — альфа 0xE0 ≈ 88 %): альфа каждой заливки умножается на percent / 88,
/// так что при 88 всё как было, при 100 диск непрозрачен, при 40 — заметно просвечивает.
/// Подписи, значки и акцентная заливка выбранного лепестка не меняются — их всегда должно быть хорошо видно.
/// </summary>
public static class WheelFill
{
    public const int MinPercent = 40, MaxPercent = 100, DefaultPercent = 88;

    public static int Clamp(int percent) => Math.Clamp(percent, MinPercent, MaxPercent);

    /// <summary>Альфа заливки при этой плотности (percent вне 40–100 приводится к границе).</summary>
    public static byte ScaleAlpha(byte alpha, int percent) =>
        (byte)Math.Clamp(Math.Round(alpha * (double)Clamp(percent) / DefaultPercent), 0, 255);
}
