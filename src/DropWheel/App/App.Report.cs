using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using DropWheel.AppEngines;
using DropWheel.Diagnostics;
using DropWheel.Engines;
using DropWheel.Localization;
using DropWheel.Settings;
using DropWheel.Updates;

namespace DropWheel.App;

/// <summary>
/// «Сообщить о проблеме» (решение D-REPORT): трей и ссылка внизу настроек → страница новой задачи на GitHub
/// с заполненным текстом (сборка — ProblemReport в ядре) и тот же текст целиком — в буфер обмена.
/// </summary>
public partial class App
{
    internal void ReportProblem()
    {
        try
        {
            var office = _engines.OfType<OfficeEngine>().FirstOrDefault() is { } engine
                ? string.Join(", ", Enum.GetValues<OfficeApp>().Where(engine.IsInstalled))
                : "";
            var info = new ProblemReportInfo(
                AppVersion.CurrentDisplay,
                AppVersion.CurrentBuild,
                UpdateChannels.Resolve(Settings.UpdateChannel, UpdateChannels.Embedded).ToString().ToLowerInvariant(),
                ProblemReport.WindowsLine(Environment.OSVersion.Version, RuntimeInformation.OSDescription),
                $"{Loc.Current.ToString().ToLowerInvariant()} (Windows: {CultureInfo.InstalledUICulture.Name})",
                office.Length > 0 ? office : null);
            var log = ProblemReport.ReadTail(ProblemReport.LatestLog(AppPaths.LogsDir));
            var (url, full) = ProblemReport.Build(info, log,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);
            Log.Write($"Сообщить о проблеме: адрес {url.Length} символов, строк журнала {log.Count}");
            var copied = SetClipboard(() => System.Windows.Clipboard.SetText(full));
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            if (copied) _tray?.ShowInfo(Loc.T.ReportCopied, Loc.T.ReportCopiedHint);
        }
        catch (Exception ex)
        {
            Log.Write("Сообщить о проблеме: не удалось: " + ex);
            _tray?.ShowInfo(Loc.T.ReportFailed, ex.Message);
        }
    }
}
