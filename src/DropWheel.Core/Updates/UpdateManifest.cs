using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DropWheel.Updates;

/// <summary>
/// version.json рядом с архивами в релизе: номер сборки, коммит, версия утилит и контрольные суммы архивов.
/// Пишет CI (.github/workflows/build.yml), читает программа при проверке обновлений.
/// </summary>
public sealed record UpdateManifest(
    int Build,
    string Commit,
    string ToolsVersion,
    string AppZip,
    string AppSha256,
    string ToolsZip,
    string ToolsSha256)
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

/// <summary>Номер сборки программы: третья часть версии сборки (0.2.&lt;run_number&gt;), задаётся в CI.</summary>
public static class AppVersion
{
    public static int CurrentBuild => FromVersion(Assembly.GetEntryAssembly()?.GetName().Version);

    public static int FromVersion(Version? version) => version is { Build: > 0 } v ? v.Build : 0;
}
