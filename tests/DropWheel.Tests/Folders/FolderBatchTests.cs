using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Localization;

namespace DropWheel.Tests.Folders;

/// <summary>Одно итоговое уведомление на папку: пачка считает каждую задачу один раз.</summary>
public class FolderBatchTests
{
    private static Job NewJob(string input) => new([input], OperationCatalog.Find("to-jpg")!);

    [Fact]
    public void Completes_once_after_every_job_finished()
    {
        var a = NewJob("a.heic");
        var b = NewJob("b.heic");
        var c = NewJob("c.heic");
        var batch = new FolderBatch([a, b, c], ["/Отпуск (JPG)"], skipped: 4);

        a.State = JobState.Running;
        Assert.False(batch.Record(a));
        a.State = JobState.Done;
        Assert.True(batch.Record(a));
        Assert.False(batch.Record(a)); // то же событие ещё раз
        Assert.False(batch.IsComplete);

        b.State = JobState.Failed;
        b.Error = "Не удалось обработать файл";
        Assert.True(batch.Record(b));
        c.State = JobState.Canceled;
        Assert.True(batch.Record(c));

        Assert.True(batch.IsComplete);
        Assert.Equal(3, batch.Total);
        Assert.Equal(1, batch.Done);
        Assert.Equal(1, batch.Canceled);
        Assert.Equal(4, batch.Skipped);
        Assert.Equal([("b.heic", "Не удалось обработать файл")], batch.Failures);
    }

    [Fact]
    public void Ignores_jobs_of_other_batches()
    {
        var mine = NewJob("a.heic");
        var other = NewJob("b.heic");
        other.State = JobState.Done;
        var batch = new FolderBatch([mine], ["/Отпуск (JPG)"], 0);

        Assert.False(batch.Contains(other));
        Assert.False(batch.Record(other));
        Assert.False(batch.IsComplete);
    }

    [Fact]
    public void Failure_without_reason_gets_the_fallback()
    {
        var job = NewJob("a.heic");
        job.State = JobState.Failed;
        var batch = new FolderBatch([job], [], 0);
        batch.Record(job, "Неизвестная ошибка");
        Assert.Equal("Неизвестная ошибка", batch.Failures[0].Reason);
    }
}

/// <summary>Имя папки-результата по-английски.</summary>
[Collection(LanguageCollection.Name)]
public class FolderPlanEnglishTests
{
    [Theory]
    [InlineData("to-jpg", "JPG")]
    [InlineData("pdf-to-png", "PNG")]
    [InlineData("img-compress", "compressed")]
    public void Tag_in_english(string id, string tag)
    {
        using var _ = Loc.Use(UiLanguage.En);
        Assert.Equal(tag, FolderPlan.Tag(OperationCatalog.Find(id)!));
        Assert.Equal($"Vacation ({tag})", FolderPlan.OutputFolderName("/photos/Vacation", OperationCatalog.Find(id)!));
    }
}
