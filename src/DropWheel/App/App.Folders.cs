using System.Windows;
using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Localization;

namespace DropWheel.App;

/// <summary>
/// «Папки целиком» (решение D-FOLDERS): «Конвертировать…» по папке (или папка, перетащенная на колесо) →
/// задачи на файлы внутри → результаты в «Отпуск (JPG)» рядом с папкой → одно итоговое уведомление на папку.
/// Вся логика (что внутри, что предложить, куда класть) — в ядре, FolderPlan; здесь — очередь, трей и уведомления.
/// </summary>
public partial class App
{
    /// <summary>Пачки задач по папкам, которые ещё не закончились. Только поток интерфейса.</summary>
    private readonly List<FolderBatch> _folderBatches = [];

    /// <summary>Развернуть папки из выбора в файлы; папок нет — null. Можно звать из фонового потока.</summary>
    private static FolderSelection? ExpandFolders(IReadOnlyList<string> paths) =>
        FolderPlan.ContainsFolders(paths) ? FolderPlan.Expand(paths) : null;

    /// <summary>Заголовок подсказок в трее: имя папки (или «2 папки»).</summary>
    private static string FoldersTitle(FolderSelection folders) =>
        folders.Folders.Count == 1 ? FolderPlan.FolderName(folders.Folders[0].Root) : Loc.Count(folders.Folders.Count, Loc.T.Folders);

    /// <summary>Слишком много файлов или пусто — подсказка в трее вместо колеса.</summary>
    private bool CanOfferFolders(FolderSelection folders)
    {
        if (folders.TooMany)
        {
            Log.Write($"Папка: больше {FolderPlan.MaxFiles} файлов — не берём");
            _tray?.ShowInfo(FoldersTitle(folders), Loc.F(Loc.T.FolderTooMany, FolderPlan.MaxFiles));
            return false;
        }
        if (folders.AllFiles.Count == 0)
        {
            Log.Write("Папка: файлов нет");
            _tray?.ShowInfo(FoldersTitle(folders), Loc.T.FolderEmpty);
            return false;
        }
        return true;
    }

    /// <summary>Поставить в очередь задачи для папок. false — ничего не поставлено (подсказка уже в трее).</summary>
    private bool EnqueueFolders(FolderSelection folders, Operation op)
    {
        if (!CanOfferFolders(folders)) return false;
        FolderBatchPlan plan;
        try
        {
            plan = FolderPlan.Plan(folders, op, Queue.Converter.Settings.OutputFolder, Queue.Converter.CanRun);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Write("Папка-результат не создалась: " + ex);
            _tray?.ShowInfo(FoldersTitle(folders), ex.Message);
            return false;
        }
        Log.Write($"Папки: {op.Id}, задач {plan.Jobs.Count}, пропущено {plan.Skipped}, результат: {string.Join(" | ", plan.OutputFolders)}");
        if (plan.Jobs.Count == 0)
        {
            _tray?.ShowInfo(FoldersTitle(folders), Loc.F(Loc.T.FolderNothingToDo, Loc.Label(op)));
            return false;
        }

        var jobs = plan.Jobs.Select(j => Queue.Enqueue(j.Inputs, op, j.Target)).ToList();
        // Задачи уже идут, но их события приходят в поток интерфейса позже — пачка успевает их учесть.
        // Без папок-результатов (Combine: «Отпуск (все).pdf») — обычные уведомления о каждом файле.
        if (plan.OutputFolders.Count > 0) _folderBatches.Add(new FolderBatch(jobs, plan.OutputFolders, plan.Skipped));
        return true;
    }

    /// <summary>Задача из папки: своё уведомление не показываем, ждём конца пачки. true — задача из папки.</summary>
    private bool OnFolderJobChanged(Job job)
    {
        var batch = _folderBatches.FirstOrDefault(b => b.Contains(job));
        if (batch is null) return false;
        if (!batch.Record(job, Loc.T.UnknownError)) return true;
        if (job.State == JobState.Failed) Log.Write($"Ошибка {job.Operation.Id} {job.Input}: {job.Error}\n{job.ErrorDetails}");
        if (!batch.IsComplete) return true;

        _folderBatches.Remove(batch);
        foreach (var root in batch.OutputFolders)
            if (FolderPlan.RemoveIfEmpty(root)) Log.Write($"Папка-результат пуста — удалена: {root}");
        Log.Write($"Папки готовы: сделано {batch.Done}, ошибок {batch.Failures.Count}, отменено {batch.Canceled}, пропущено {batch.Skipped}");
        _notifier?.FolderDone(batch);
        return true;
    }
}
