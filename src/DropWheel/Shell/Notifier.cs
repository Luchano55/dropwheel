using System.Diagnostics;
using DropWheel.Logging;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DropWheel.Shell;

/// <summary>Системные уведомления Windows: «Готово» с кнопкой «Показать в папке», ошибки.</summary>
internal sealed class Notifier
{
    private const string RevealAction = "reveal";
    private readonly FileLog _log;

    public Notifier(FileLog log)
    {
        _log = log;
        // Клик по уведомлению/кнопке. Приходит в фоновом потоке.
        ToastNotificationManagerCompat.OnActivated += e =>
        {
            var args = ToastArguments.Parse(e.Argument);
            if (args.TryGetValue("action", out var action) && action == RevealAction
                && args.TryGetValue("path", out var path))
                RevealInExplorer(path);
        };
    }

    public void Done(string outputPath) => Show(() => new ToastContentBuilder()
        .AddArgument("action", RevealAction)
        .AddArgument("path", outputPath)
        .AddText("Готово: " + Path.GetFileName(outputPath))
        .AddText(Path.GetDirectoryName(outputPath))
        .AddButton(new ToastButton()
            .SetContent("Показать в папке")
            .AddArgument("action", RevealAction)
            .AddArgument("path", outputPath)));

    public void Failed(string inputPath, string reason) => Show(() => new ToastContentBuilder()
        .AddText("Ошибка: " + Path.GetFileName(inputPath))
        .AddText(reason)
        .AddText("Подробности — в журнале DropWheel"));

    private void Show(Func<ToastContentBuilder> build)
    {
        try
        {
            build().Show();
        }
        catch (Exception ex)
        {
            _log.Write("Не удалось показать уведомление: " + ex.Message);
        }
    }

    public static void RevealInExplorer(string path)
    {
        // explorer разбирает командную строку сам, поэтому строкой, а не ArgumentList.
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        else if (Path.GetDirectoryName(path) is { } dir && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }
}
