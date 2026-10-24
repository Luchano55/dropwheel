using System.Windows.Threading;
using DropWheel.Localization;
using DropWheel.Updates;

namespace DropWheel.App;

/// <summary>
/// «Что нового» (D-WHATSNEW): после смены версии — окно с разделами CHANGELOG от прежней версии до текущей.
/// Прежняя версия — AppSettings.LastSeenVersion (пишется при каждом запуске новой версии).
/// </summary>
public partial class App
{
    private static readonly TimeSpan WhatsNewDelay = TimeSpan.FromSeconds(2.5);
    private WhatsNewWindow? _whatsNewWindow;

    /// <summary>При запуске: версия сменилась — через пару секунд окно (без фокуса); и запомнить текущую версию.</summary>
    private void CheckWhatsNew(bool afterUpdate)
    {
        var current = AppVersion.CurrentDisplay;
        if (current is null) return; // сборка разработчика
        var previous = Settings.LastSeenVersion;
        if (WhatsNew.ShouldShow(previous, current, afterUpdate))
        {
            Log.Write($"Что нового: {previous ?? "(нет)"} → {current}");
            var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = WhatsNewDelay };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ShowWhatsNew(previous, activate: false);
            };
            timer.Start();
        }
        if (previous != current) UpdateSettings(s => s with { LastSeenVersion = current });
    }

    /// <summary>Пункт трея «Что нового» (previous = null — раздел текущей версии) и показ после обновления.</summary>
    internal void ShowWhatsNew(string? previous = null, bool activate = true)
    {
        try
        {
            if (_whatsNewWindow is not null)
            {
                _whatsNewWindow.Activate();
                return;
            }
            var current = AppVersion.CurrentDisplay;
            var notes = ChangelogNotes.Select(WhatsNewWindow.ReadChangelog(), current, previous, Loc.Current);
            var version = notes.FirstOrDefault()?.Version ?? ChangelogNotes.BaseVersion(current)?.ToString(3) ?? current ?? "";
            _whatsNewWindow = new WhatsNewWindow(version, notes, Log, activate);
            _whatsNewWindow.Closed += (_, _) => _whatsNewWindow = null;
            _whatsNewWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Write("Что нового: окно не открылось: " + ex);
        }
    }
}
