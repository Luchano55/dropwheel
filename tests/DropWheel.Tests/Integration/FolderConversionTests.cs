using DropWheel.Cli;
using DropWheel.Conversions;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>
/// Папки целиком (D-FOLDERS) с настоящими утилитами: «Отпуск» → «Отпуск (FLAC)» с теми же подпапками,
/// файлы уже в нужном формате — копии, исходники не тронуты, временных файлов нет.
/// </summary>
public class FolderConversionTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static Operation Op(string id) => OperationCatalog.Find(id)!;

    private string Folder(string relative)
    {
        var path = Path.Combine(_dir.Path, relative);
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<IReadOnlyList<Job>> RunAsync(FolderBatchPlan plan, Operation op, ConversionSettings? settings = null)
    {
        var queue = new JobQueue(new Converter(Tools.Locator, settings ?? ConversionSettings.Default), maxParallel: 2);
        var jobs = plan.Jobs.Select(j => queue.Enqueue(j.Inputs, op, j.Target)).ToList();
        await Task.WhenAll(jobs.Select(j => j.Completion));
        return jobs;
    }

    private static void AssertNoTempLeft(string folder) =>
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder, ".dropwheel*", SearchOption.AllDirectories));

    [SkippableFact]
    public async Task Folder_to_flac_mirrors_subfolders_and_copies_flacs()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var root = Folder("Отпуск 2023");
        Folder("Отпуск 2023/день 2");
        var wav = Tools.MakeAudio(Path.Combine(root, "тест 1.wav"));
        var nested = Tools.MakeAudio(Path.Combine(root, "день 2", "звук 2.wav"));
        var flac = Tools.MakeAudio(Path.Combine(root, "уже.flac"));
        var notes = Path.Combine(root, "заметки.txt");
        File.WriteAllText(notes, "не звук");
        var hashes = new[] { wav, nested, flac, notes }.ToDictionary(p => p, Tools.Hash);

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-flac"), null);
        var jobs = await RunAsync(plan, Op("to-flac"));

        Assert.All(jobs, j => Assert.Equal(JobState.Done, j.State));
        var outRoot = Path.Combine(_dir.Path, "Отпуск 2023 (FLAC)");
        Assert.Equal(1, plan.Skipped);
        Assert.True(File.Exists(Path.Combine(outRoot, "тест 1.flac")));
        Assert.True(File.Exists(Path.Combine(outRoot, "день 2", "звук 2.flac")));
        Assert.Equal(hashes[flac], Tools.Hash(Path.Combine(outRoot, "уже.flac")));
        Assert.False(File.Exists(Path.Combine(outRoot, "заметки.txt")));
        Assert.Equal(3, Directory.GetFiles(outRoot, "*", SearchOption.AllDirectories).Length);
        Assert.Equal("flac", Tools.Streams(Path.Combine(outRoot, "тест 1.flac"))[0]);

        // Исходники — на месте и не изменились; в исходной папке ничего нового.
        Assert.All(hashes, kv => Assert.Equal(kv.Value, Tools.Hash(kv.Key)));
        Assert.Equal(4, Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length);
        AssertNoTempLeft(outRoot);
        AssertNoTempLeft(root);
    }

    [SkippableFact]
    public async Task Tool_results_in_folder_have_no_suffix()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var root = Folder("Звуки");
        Tools.MakeAudio(Path.Combine(root, "a.wav"));
        Tools.MakeAudio(Path.Combine(root, "b.mp3"));

        var op = Op("audio-normalize");
        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), op, null);
        var jobs = await RunAsync(plan, op);

        Assert.All(jobs, j => Assert.Equal(JobState.Done, j.State));
        var outRoot = Path.Combine(_dir.Path, "Звуки (норм)");
        Assert.True(File.Exists(Path.Combine(outRoot, "a.wav")));
        Assert.True(File.Exists(Path.Combine(outRoot, "b.mp3")));
    }

    [SkippableFact]
    public async Task Shared_output_folder_gets_the_result_folder()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var root = Folder("Отпуск");
        Tools.MakeAudio(Path.Combine(root, "a.wav"));
        var shared = Folder("Общая");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-mp3"), shared);
        // «В одну папку» в настройках не мешает: у задачи своя папка.
        var jobs = await RunAsync(plan, Op("to-mp3"), ConversionSettings.Default with { OutputFolder = shared });

        Assert.Equal(JobState.Done, Assert.Single(jobs).State);
        Assert.True(File.Exists(Path.Combine(shared, "Отпуск (MP3)", "a.mp3")));
    }

    [SkippableFact]
    public async Task Same_names_in_one_folder_get_numbers()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var root = Folder("Микс");
        Tools.MakeAudio(Path.Combine(root, "a.wav"));
        Tools.MakeAudio(Path.Combine(root, "a.ogg"));

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-mp3"), null);
        var jobs = await RunAsync(plan, Op("to-mp3"));

        Assert.All(jobs, j => Assert.Equal(JobState.Done, j.State));
        var outRoot = Path.Combine(_dir.Path, "Микс (MP3)");
        Assert.True(File.Exists(Path.Combine(outRoot, "a.mp3")));
        Assert.True(File.Exists(Path.Combine(outRoot, "a (1).mp3")));
    }

    [SkippableFact]
    public async Task Failed_folder_leaves_no_empty_result_folder()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var root = Folder("Битые");
        File.WriteAllText(Path.Combine(root, "плохой.wav"), "мусор");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-mp3"), null);
        var jobs = await RunAsync(plan, Op("to-mp3"));

        Assert.Equal(JobState.Failed, Assert.Single(jobs).State);
        var outRoot = Assert.Single(plan.OutputFolders);
        Assert.True(FolderPlan.RemoveIfEmpty(outRoot));
        Assert.False(Directory.Exists(outRoot));
        Assert.True(File.Exists(Path.Combine(root, "плохой.wav")));
    }

    [SkippableFact]
    public async Task Images_to_one_pdf_named_after_the_folder()
    {
        Skip.IfNot(Tools.HasMagick && Tools.HasFfmpeg, "нет ImageMagick или ffmpeg");
        var root = Folder("Сканы");
        Folder("Сканы/ещё");
        Tools.MakeImage(Path.Combine(root, "стр 2.png"), 64, 48);
        Tools.MakeImage(Path.Combine(root, "стр 10.png"), 64, 48);
        Tools.MakeImage(Path.Combine(root, "ещё", "стр 1.png"), 64, 48);

        var op = Op("images-to-pdf");
        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), op, null);
        var jobs = await RunAsync(plan, op);

        var job = Assert.Single(jobs);
        Assert.Equal(JobState.Done, job.State);
        Assert.Equal(Path.Combine(_dir.Path, "Сканы (все).pdf"), job.OutputPath);
        if (Tools.HasQpdf) Assert.Equal(3, Tools.PdfPages(job.OutputPath!));
    }

    [SkippableFact]
    public async Task Cli_converts_a_folder()
    {
        Skip.IfNot(Tools.HasFfmpeg, "нет ffmpeg");
        var root = Folder("Мои звуки");
        Tools.MakeAudio(Path.Combine(root, "тест 1.wav"));
        File.WriteAllText(Path.Combine(root, "заметки.txt"), "текст");

        var stdout = new StringWriter();
        var code = await Program.RunAsync(["convert", root, "to-mp3"], stdout, new StringWriter());

        Assert.Equal(0, code);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "Мои звуки (MP3)", "тест 1.mp3")));
        Assert.Contains("Пропущено (не подходят): 1", stdout.ToString());

        var list = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(["list", root], list, new StringWriter()));
        Assert.Contains("to-mp3", list.ToString());
    }
}
