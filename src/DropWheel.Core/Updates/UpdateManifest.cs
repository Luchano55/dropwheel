using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropWheel.Updates;

/// <summary>
/// version.json рядом с архивами в релизе: номер сборки, коммит, версия утилит и контрольные суммы архивов.
/// Пишет CI (.github/workflows/build.yml), читает программа при проверке обновлений.
/// Version и Channel появились в 0.3.0 (D-RELEASE): в старых файлах их нет — тогда null.
/// Сборки сравниваются только по Build (номер запуска CI растёт во всех каналах).
/// </summary>
public sealed record UpdateManifest(
    int Build,
    string Commit,
    string ToolsVersion,
    string AppZip,
    string AppSha256,
    string ToolsZip,
    string ToolsSha256,
    string? Version = null,
    string? Channel = null)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>null — файл не наш или битый.</summary>
    public static UpdateManifest? Parse(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<UpdateManifest>(json, Json);
            return m is { Build: > 0 } && !string.IsNullOrWhiteSpace(m.AppZip) && !string.IsNullOrWhiteSpace(m.ToolsZip)
                ? m
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>Что обновлять: программу (новее номер сборки) и/или утилиты (другая версия набора).</summary>
public sealed record UpdatePlan(bool App, bool Tools)
{
    public bool Any => App || Tools;

    /// <param name="currentBuild">Номер сборки этой программы; 0 — собрана локально, не обновляем.</param>
    /// <param name="localToolsVersion">Содержимое tools/tools-version.txt; null — файла нет.</param>
    public static UpdatePlan For(UpdateManifest remote, int currentBuild, string? localToolsVersion)
    {
        if (currentBuild <= 0) return new(false, false);
        var app = remote.Build > currentBuild;
        // Утилиты меняем вместе с новой программой, если их набор поменялся (или файла версии нет).
        var tools = app && !string.Equals(remote.ToolsVersion, localToolsVersion?.Trim(), StringComparison.OrdinalIgnoreCase);
        return new(app, tools);
    }
}

/// <summary>
/// Версия программы. Номер сборки — третья часть версии сборки (0.3.&lt;run_number&gt;), задаётся в CI:
/// по нему сравниваются сборки. Версия для людей (0.3.0, 0.3.0-dev.57) — InformationalVersion.
/// </summary>
public static class AppVersion
{
    public static int CurrentBuild => FromVersion(Assembly.GetEntryAssembly()?.GetName().Version);

    /// <summary>«0.3.0» (стабильная), «0.3.0-dev.57» (тестовая); null — сборка разработчика.</summary>
    public static string? CurrentDisplay => CurrentBuild > 0
        ? DisplayFrom(Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        : null;

    public static int FromVersion(Version? version) => version is { Build: > 0 } v ? v.Build : 0;

    /// <summary>InformationalVersion без «+коммит» в конце: «0.3.0+abc123» → «0.3.0».</summary>
    public static string? DisplayFrom(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;
        var plus = informationalVersion.IndexOf('+');
        var v = (plus >= 0 ? informationalVersion[..plus] : informationalVersion).Trim();
        return v.Length == 0 ? null : v;
    }
}
