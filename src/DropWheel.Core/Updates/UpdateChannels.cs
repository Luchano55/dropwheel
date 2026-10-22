using System.Reflection;

namespace DropWheel.Updates;

/// <summary>Канал обновлений: стабильные версии (релизы vX.Y.Z) или тестовые сборки (релиз dev-build).</summary>
public enum UpdateChannel { Stable, Dev }

/// <summary>
/// Откуда брать обновления (решение D-RELEASE). Канал «зашит» в программу при сборке
/// (AssemblyMetadata «UpdateChannel»: тег vX.Y.Z → stable, main → dev); настройка UpdateChannel
/// может его переопределить: «auto» — как при сборке, «stable», «dev».
/// </summary>
public static class UpdateChannels
{
    /// <summary>Значения настройки UpdateChannel в settings.json.</summary>
    public const string Auto = "auto", Stable = "stable", Dev = "dev";

    /// <summary>Ключ AssemblyMetadata, в который CI пишет канал.</summary>
    public const string MetadataKey = "UpdateChannel";

    public const string Repository = "https://github.com/Luchano55/dropwheel";
    /// <summary>Тестовые сборки: релиз dev-build, перезаписывается каждой сборкой из main.</summary>
    public const string DevBase = Repository + "/releases/download/dev-build/";
    /// <summary>GitHub отдаёт по «latest» самый новый релиз, не помеченный как prerelease.</summary>
    public const string StableLatestBase = Repository + "/releases/latest/download/";
    public const string ManifestFile = "version.json";

    /// <summary>Значение настройки: «auto», «stable» или «dev» (неизвестное и пустое → «auto»).</summary>
    public static string Normalize(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        "stable" or "release" => Stable,
        "dev" or "beta" or "test" => Dev,
        _ => Auto,
    };

    /// <summary>Канал из метаданных сборки. Нет метаданных (старые и локальные сборки) — dev: так собирались все до 0.3.0.</summary>
    public static UpdateChannel FromMetadata(string? embedded) =>
        string.Equals(embedded?.Trim(), Stable, StringComparison.OrdinalIgnoreCase) ? UpdateChannel.Stable : UpdateChannel.Dev;

    /// <summary>Канал, зашитый в программу (entry assembly).</summary>
    public static UpdateChannel Embedded => FromMetadata(EmbeddedValue(Assembly.GetEntryAssembly()));

    public static string? EmbeddedValue(Assembly? assembly) =>
        assembly?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, MetadataKey, StringComparison.Ordinal))?.Value;

    /// <summary>Итоговый канал: настройка «stable»/«dev» — как сказано, «auto» — зашитый при сборке.</summary>
    public static UpdateChannel Resolve(string? setting, UpdateChannel embedded) => Normalize(setting) switch
    {
        Stable => UpdateChannel.Stable,
        Dev => UpdateChannel.Dev,
        _ => embedded,
    };

    /// <summary>Где лежит version.json канала.</summary>
    public static string ManifestUrl(UpdateChannel channel) =>
        (channel == UpdateChannel.Stable ? StableLatestBase : DevBase) + ManifestFile;

    /// <summary>
    /// Откуда качать архив из манифеста. Стабильный канал с известной версией — из релиза этой версии
    /// (vX.Y.Z): пока качаем, «latest» может смениться, а файлы конкретного релиза — нет.
    /// </summary>
    public static string AssetUrl(UpdateChannel channel, UpdateManifest manifest, string fileName)
    {
        var file = Uri.EscapeDataString(fileName);
        if (channel == UpdateChannel.Dev) return DevBase + file;
        var version = manifest.Version?.Trim();
        return IsPlainVersion(version)
            ? $"{Repository}/releases/download/v{version}/{file}"
            : StableLatestBase + file;
    }

    /// <summary>«1.2.3» или «1.2.3-rc.1»: цифры, точки, латиница и дефис — годится в имя тега.</summary>
    public static bool IsPlainVersion(string? version) =>
        !string.IsNullOrEmpty(version) && char.IsAsciiDigit(version[0])
        && version.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');
}
