using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

public class QueueAndCliTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [SkippableFact]
    public async Task Queue_runs_at_most_two_at_once()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var files = Enumerable.Range(1, 5)
            .Select(i => Tools.MakeAudio(Path.Combine(_dir.Path, $"звук {i}.wav")))
            .ToList();

        var queue = new JobQueue(new Converter(Tools.Locator, ConversionSettings.Default), maxParallel: 2);
        var maxRunning = 0;
        queue.Changed += _ =>
        {
            var running = queue.Jobs.Count(j => j.State == JobState.Running);
            lock (queue) maxRunning = Math.Max(maxRunning, running);
        };

        var jobs = files.Select(f => queue.Enqueue(f, OperationCatalog.Find("to-flac")!)).ToList();
        await Task.WhenAll(jobs.Select(j => j.Completion));

        Assert.All(jobs, j => Assert.Equal(JobState.Done, j.State));
        Assert.All(jobs, j => Assert.True(File.Exists(j.OutputPath)));
        Assert.InRange(maxRunning, 1, 2);
    }

    [SkippableFact]
    public async Task Queue_reports_failure_without_throwing()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var queue = new JobQueue(new Converter(Tools.Locator, ConversionSettings.Default));
        var job = queue.Enqueue(_dir.File("плохой.png", "мусор"), OperationCatalog.Find("to-jpg")!);
        await job.Completion;
        Assert.Equal(JobState.Failed, job.State);
        Assert.NotNull(job.Error);
    }

    [SkippableFact]
    public async Task Cli_convert_heic_to_jpg()
    {
        Skip.IfNot(Tools.HasMagick, "нет ImageMagick");
        var src = Path.Combine(_dir.Path, "тест 1.heic");
        File.Copy(Tools.Fixture("sample.heic"), src);

        var stdout = new StringWriter();
        var code = await Program.RunAsync(["convert", src, "to-jpg"], stdout, new StringWriter());

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "тест 1.jpg")));
        Assert.Contains("Готово", stdout.ToString());
    }

    [Fact]
    public async Task Cli_unknown_operation_is_usage_error()
    {
        await Assert.ThrowsAsync<UsageException>(() =>
            Program.RunAsync(["convert", "a.png", "to-nothing"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public async Task Cli_list_for_unknown_file_offers_zip()
    {
        var stdout = new StringWriter();
        var code = await Program.RunAsync(["list", "data.bin"], stdout, new StringWriter());
        Assert.Equal(0, code);
        Assert.Equal("zip-pack", stdout.ToString().Trim().Split(' ')[0]);
    }

    [Fact]
    public async Task Cli_zip_pack_makes_one_archive()
    {
        var a = _dir.File("a.txt", "первый");
        var b = _dir.File("b.txt", "второй");

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await Program.RunAsync(["convert", a, b, "zip-pack"], stdout, stderr);

        Assert.True(code == 0, stderr.ToString());
        var zip = Path.Combine(_dir.Path, "Мои фото.zip"); // несколько файлов — по имени папки
        Assert.Equal([zip], Directory.GetFiles(_dir.Path, "*.zip"));
        using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
        Assert.Equal(["a.txt", "b.txt"], archive.Entries.Select(e => e.FullName).Order());
        Assert.Single(stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Cli_missing_file_fails_with_message()
    {
        var stderr = new StringWriter();
        var code = await Program.RunAsync(["convert", Path.Combine(_dir.Path, "нет такого.png"), "to-jpg"],
            new StringWriter(), stderr);
        Assert.Equal(1, code);
        Assert.Contains("Файл не найден", stderr.ToString());
    }
}
