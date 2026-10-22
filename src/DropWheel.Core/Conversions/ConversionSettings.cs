namespace DropWheel.Conversions;

/// <summary>Параметры качества и место для результатов (раздел 10 ТЗ).</summary>
public sealed record ConversionSettings
{
    public int JpegQuality { get; init; } = 90;
    public int WebpQuality { get; init; } = 85;
    /// <summary>CRF для обычной конвертации видео в H.264 (меньше — лучше качество).</summary>
    public int VideoCrf { get; init; } = 23;
    /// <summary>CRF для инструмента «Сжать».</summary>
    public int CompressCrf { get; init; } = 28;
    public int Mp3BitrateKbps { get; init; } = 192;
    /// <summary>null — класть результат рядом с исходником.</summary>
    public string? OutputFolder { get; init; }

    public static ConversionSettings Default { get; } = new();

    // Что можно выбрать в окне настроек.
    public const int MinImageQuality = 50, MaxImageQuality = 100;
    public const int MinVideoCrf = 18, MaxVideoCrf = 32;
    public static IReadOnlyList<int> Mp3Bitrates { get; } = [128, 160, 192, 256, 320];

    /// <summary>Папка «В одну папку» по умолчанию: «Загрузки\DropWheel».</summary>
    public static string DefaultSharedFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DropWheel");
}
