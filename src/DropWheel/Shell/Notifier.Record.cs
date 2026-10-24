using DropWheel.Localization;
using Microsoft.Toolkit.Uwp.Notifications;

namespace DropWheel.Shell;

/// <summary>Уведомление «Запись сохранена» (решение D-RECORD): превью, «Открыть», «Показать в папке», у MP4 — «Обрезать…».</summary>
internal sealed partial class Notifier
{
    private const string TrimAction = "trim";

    /// <summary>Нажата «Обрезать…» у записи (путь к MP4). Приходит в фоновом потоке.</summary>
    public event Action<string?>? TrimRequested;

    /// <summary>
    /// Запись экрана готова. «Копировать результат в буфер обмена» действует и тут (файл — в буфер).
    /// Вызывать из потока интерфейса.
    /// </summary>
    public async Task RecordSavedAsync(string path, bool canTrim)
    {
        var copied = TryCopy([path]);
        string? preview = null;
        if (HasPreview(path))
        {
            try
            {
                var task = ShellThumbnails.SaveToastPreviewAsync(path);
                if (await Task.WhenAny(task, Task.Delay(PreviewTimeout)) == task) preview = await task;
            }
            catch (Exception ex)
            {
                _log.Write("Превью для уведомления не получилось: " + ex.Message);
            }
        }

        Show(() =>
        {
            var b = new ToastContentBuilder()
                .AddArgument(ActionKey, RevealAction)
                .AddArgument("path", path)
                .AddText(Loc.T.RecordSaved)
                .AddText(Path.GetFileName(path))
                .AddText(FolderLine([path], copied));
            if (preview is not null) b.AddHeroImage(new Uri(preview));
            b.AddButton(new ToastButton().SetContent(Loc.T.ActionOpen)
                    .AddArgument(ActionKey, OpenAction).AddArgument("path", path))
                .AddButton(new ToastButton().SetContent(Loc.T.ActionReveal)
                    .AddArgument(ActionKey, RevealAction).AddArgument("path", path));
            if (canTrim)
                b.AddButton(new ToastButton().SetContent(Loc.T.RecordTrim)
                    .AddArgument(ActionKey, TrimAction).AddArgument("path", path));
            return b;
        });
    }

    /// <summary>Запись не получилась: причина и «Подробнее» (журнал).</summary>
    public void RecordFailed(string reason) => Show(() => new ToastContentBuilder()
        .AddArgument(ActionKey, LogAction)
        .AddText(Loc.T.RecordFailed)
        .AddText(reason)
        .AddButton(new ToastButton().SetContent(Loc.T.ActionDetails).AddArgument(ActionKey, LogAction)));
}
