using DropWheel.Conversions;
using DropWheel.Jobs;
using DropWheel.Wheel;

namespace DropWheel.Tests.Folders;

/// <summary>«Папки целиком» (D-FOLDERS): что внутри папки, что предложить, куда класть результат.</summary>
public class FolderPlanTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static Operation Op(string id) => OperationCatalog.Find(id)!;

    /// <summary>Файл в подпапке «Мои фото/…» (папки создаются).</summary>
    private string Touch(string relative, string content = "x")
    {
        // GetFullPath: «/» в относительном пути → «\» на Windows, как в путях, которые возвращает обход папки.
        var path = Path.GetFullPath(Path.Combine(_dir.Path, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string Folder(string name)
    {
        var path = Path.GetFullPath(Path.Combine(_dir.Path, name));
        Directory.CreateDirectory(path);
        return path;
    }

    // ---------- что внутри ----------

    [Fact]
    public void Scan_goes_into_subfolders_in_explorer_order()
    {
        var root = Folder("Отпуск 2023");
        Touch("Отпуск 2023/фото 10.jpg");
        Touch("Отпуск 2023/фото 2.jpg");
        Touch("Отпуск 2023/Б день/тест 1.heic");
        Touch("Отпуск 2023/А день/IMG 1.png");

        var scan = FolderPlan.Scan(root);

        Assert.False(scan.TooMany);
        Assert.Equal(root, scan.Root);
        Assert.Equal(
        [
            Path.Combine(root, "фото 2.jpg"),
            Path.Combine(root, "фото 10.jpg"),
            Path.Combine(root, "А день", "IMG 1.png"),
            Path.Combine(root, "Б день", "тест 1.heic"),
        ], scan.Files);
    }

    [Fact]
    public void Scan_skips_hidden_files_and_folders_and_our_temp_folder()
    {
        var root = Folder("Фото");
        Touch("Фото/a.jpg");
        Touch("Фото/.hidden.jpg");                       // скрытый (на Windows — атрибут, здесь — точка)
        Touch("Фото/.git/config.jpg");                   // скрытая папка
        Touch($"Фото/{Converter.TempDirName}/x.jpg");     // временная папка DropWheel
        Touch("Фото/.dropwheel-123.jpg");                 // недописанный результат
        if (OperatingSystem.IsWindows())
        {
            var hidden = Touch("Фото/скрыт.jpg");
            File.SetAttributes(hidden, FileAttributes.Hidden);
            var system = Touch("Фото/desktop.ini");
            File.SetAttributes(system, FileAttributes.System | FileAttributes.Hidden);
        }

        Assert.Equal([Path.Combine(root, "a.jpg")], FolderPlan.Scan(root).Files);
    }

    [Fact]
    public void Scan_does_not_follow_folder_links()
    {
        var root = Folder("Фото");
        Touch("Фото/a.jpg");
        try
        {
            // Ссылка на саму папку — без защиты обход шёл бы по кругу.
            Directory.CreateSymbolicLink(Path.Combine(root, "петля"), root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Windows без режима разработчика не даёт создавать ссылки — проверяем на Linux
        }

        Assert.Equal([Path.Combine(root, "a.jpg")], FolderPlan.Scan(root).Files);
    }

    [Fact]
    public void Scan_stops_at_the_limit()
    {
        var root = Folder("Много");
        for (var i = 0; i < 6; i++) Touch($"Много/{i}.jpg");

        var scan = FolderPlan.Scan(root, maxFiles: 5);
        Assert.True(scan.TooMany);
        Assert.Equal(5, scan.Files.Count);

        Assert.False(FolderPlan.Scan(root, maxFiles: 6).TooMany);
    }

    [Fact]
    public void Expand_separates_folders_and_loose_files()
    {
        var root = Folder("Отпуск");
        Touch("Отпуск/a.jpg");
        var loose = Touch("тест 1.heic");

        var selection = FolderPlan.Expand([root, loose, Path.Combine(_dir.Path, "нет такого")]);

        Assert.Single(selection.Folders);
        Assert.Equal([loose], selection.Files);
        Assert.Equal([Path.Combine(root, "a.jpg"), loose], selection.AllFiles);
        Assert.True(FolderPlan.ContainsFolders([loose, root]));
        Assert.False(FolderPlan.ContainsFolders([loose]));
    }

    [Fact]
    public void Too_many_files_together()
    {
        var files = Enumerable.Range(0, FolderPlan.MaxFiles).Select(i => $"/x/{i}.jpg").ToList();
        Assert.False(new FolderSelection([new FolderScan("/x", files, false)], []).TooMany);
        Assert.True(new FolderSelection([new FolderScan("/x", files, false)], ["/y.jpg"]).TooMany);
        Assert.True(new FolderSelection([new FolderScan("/x", [], TooMany: true)], []).TooMany);
    }

    // ---------- что предложить ----------

    [Fact]
    public void Dominant_group_is_the_most_common_kind()
    {
        Assert.Equal(WheelGroups.Photo, FolderPlan.DominantGroup(["a.jpg", "b.heic", "c.mp4", "d.txt"]));
        Assert.Equal(WheelGroups.Video, FolderPlan.DominantGroup(["a.jpg", "c.mp4", "d.mov"]));
        // Поровну — тот тип, что раньше в списке (фото раньше видео).
        Assert.Equal(WheelGroups.Photo, FolderPlan.DominantGroup(["c.mp4", "a.jpg"]));
        // Неизвестные файлы не перевешивают известные.
        Assert.Equal(WheelGroups.Audio, FolderPlan.DominantGroup(["a.xyz", "b.xyz", "c.mp3"]));
        Assert.Equal(WheelGroups.Any, FolderPlan.DominantGroup(["a.xyz"]));
        Assert.Equal(WheelGroups.Any, FolderPlan.DominantGroup([]));
    }

    [Fact]
    public void Photos_with_some_jpgs_still_get_jpg()
    {
        var ops = FolderPlan.Offer(["a.heic", "b.jpg", "c.png", "clip.mp4", "notes.txt"]).Select(o => o.Id).ToList();

        // JPG: HEIC и PNG сконвертируются, JPG скопируется как есть.
        Assert.Contains("to-jpg", ops);
        Assert.Contains("to-png", ops);
        Assert.Contains("img-compress", ops);
        Assert.Contains("images-to-pdf", ops);
        // Видео в меньшинстве — его операций нет; упаковки всего в один архив для папок нет.
        Assert.DoesNotContain("to-mp4", ops);
        Assert.DoesNotContain(Engines.ZipEngine.PackId, ops);
        Assert.DoesNotContain(Engines.SevenZip.PackId, ops);
        // «MP4» из GIF подходит только к GIF — не всем фото.
        Assert.DoesNotContain("gif-to-mp4", ops);
    }

    [Fact]
    public void Folder_of_jpgs_is_not_offered_jpg()
    {
        var ops = FolderPlan.Offer(["a.jpg", "b.jpeg"]).Select(o => o.Id).ToList();
        Assert.DoesNotContain("to-jpg", ops);
        Assert.Contains("to-png", ops);
    }

    [Fact]
    public void Folder_of_videos_is_not_offered_trim()
    {
        var ops = FolderPlan.Offer(["a.mp4", "b.mov"]).Select(o => o.Id).ToList();
        Assert.DoesNotContain("video-trim", ops);
        Assert.DoesNotContain("video-trim-exact", ops);
        Assert.Contains("video-compress", ops);
    }

    [Fact]
    public void Combine_needs_enough_files()
    {
        Assert.DoesNotContain(Op("images-to-pdf"), FolderPlan.Offer(["a.jpg"]));
        Assert.DoesNotContain(Op("pdf-merge"), FolderPlan.Offer(["a.pdf"]));
        Assert.Contains(Op("pdf-merge"), FolderPlan.Offer(["a.pdf", "b.pdf"]));
    }

    [Fact]
    public void Offer_respects_what_this_computer_can_run()
    {
        var ops = FolderPlan.Offer(["a.heic", "b.png"], (op, _) => op.Id != "to-webp");
        Assert.DoesNotContain(Op("to-webp"), ops);
        Assert.Contains(Op("to-jpg"), ops);
    }

    [Fact]
    public void Offer_for_plain_files_is_the_usual_one()
    {
        var a = Touch("a.heic");
        var b = Touch("b.heic");
        var offer = FolderPlan.OfferFor([a, b]);
        Assert.Null(offer.Folders);
        Assert.Equal(OperationCatalog.ForFiles([a, b]), offer.Operations);
        Assert.Equal(WheelGroups.Photo, offer.Group);
    }

    [Fact]
    public void Offer_for_a_folder_looks_inside()
    {
        var root = Folder("Отпуск");
        Touch("Отпуск/a.heic");
        Touch("Отпуск/день 2/b.jpg");

        var offer = FolderPlan.OfferFor([root]);

        Assert.NotNull(offer.Folders);
        Assert.Equal(WheelGroups.Photo, offer.Group);
        Assert.Contains(Op("to-jpg"), offer.Operations);
    }

    [Fact]
    public void Offer_for_too_big_or_empty_folder_is_empty()
    {
        var empty = Folder("Пусто");
        Assert.Empty(FolderPlan.OfferFor([empty]).Operations);

        var big = Folder("Много");
        for (var i = 0; i < 3; i++) Touch($"Много/{i}.jpg");
        var offer = FolderPlan.OfferFor([big], maxFiles: 2);
        Assert.Empty(offer.Operations);
        Assert.True(offer.Folders!.TooMany);
    }

    // ---------- имя папки-результата ----------

    [Theory]
    [InlineData("to-jpg", "JPG")]
    [InlineData("to-webp", "WebP")]
    [InlineData("pdf-to-png", "PNG")]          // «PNG (страницы)» — без уточнения
    [InlineData("video-poster", "JPG")]        // «JPG (кадр)»
    [InlineData("img-compress", "сжато")]      // инструмент — его пометка
    [InlineData("img-1920", "1920")]
    [InlineData("video-rotate-cw", "повёрнуто")]
    [InlineData("video-subs", "Субтитры SRT")] // инструмент без пометки — название
    public void Tag_names_the_result(string id, string tag) => Assert.Equal(tag, FolderPlan.Tag(Op(id)));

    [Fact]
    public void Output_folder_sits_next_to_the_folder()
    {
        var root = Path.Combine(_dir.Path, "Отпуск");
        Assert.Equal("Отпуск (JPG)", FolderPlan.OutputFolderName(root, Op("to-jpg")));
        Assert.Equal("Отпуск (сжато)", FolderPlan.OutputFolderName(root + Path.DirectorySeparatorChar, Op("img-compress")));
        Assert.Equal(_dir.Path, FolderPlan.ResultParent(root, null));
        Assert.Equal("/общая", FolderPlan.ResultParent(root, "/общая"));
    }

    [Fact]
    public void Drive_root_gets_a_name()
    {
        Assert.Equal("D", FolderPlan.FolderName(@"D:\"));
        Assert.Equal("D", FolderPlan.FolderName("D:"));
    }

    // ---------- план ----------

    [Fact]
    public void Plan_mirrors_subfolders_copies_same_format_and_skips_the_rest()
    {
        var root = Folder("Отпуск");
        var heic = Touch("Отпуск/тест 1.heic");
        var jpg = Touch("Отпуск/день 2/b.jpg");
        Touch("Отпуск/clip.mp4");
        Touch("Отпуск/notes.txt");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-jpg"), sharedOutput: null);

        var outRoot = Path.Combine(_dir.Path, "Отпуск (JPG)");
        Assert.Equal([outRoot], plan.OutputFolders);
        Assert.True(Directory.Exists(outRoot));
        Assert.Equal(2, plan.Skipped);
        Assert.Equal(2, plan.Jobs.Count);

        var first = plan.Jobs.Single(j => j.Inputs[0] == heic);
        Assert.Equal(new OutputTarget(outRoot, KeepSuffix: false, CopySameFormat: true), first.Target);
        var second = plan.Jobs.Single(j => j.Inputs[0] == jpg);
        Assert.Equal(Path.Combine(outRoot, "день 2"), second.Target!.Directory);
    }

    [Fact]
    public void Plan_takes_a_free_name_if_the_result_folder_exists()
    {
        var root = Folder("Отпуск");
        Touch("Отпуск/a.heic");
        Folder("Отпуск (JPG)");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-jpg"), null);

        Assert.Equal([Path.Combine(_dir.Path, "Отпуск (JPG) (1)")], plan.OutputFolders);
        // Повторно — следующее имя: папка занимается сразу.
        var again = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-jpg"), null);
        Assert.Equal([Path.Combine(_dir.Path, "Отпуск (JPG) (2)")], again.OutputFolders);
    }

    [Fact]
    public void Plan_puts_the_result_folder_into_the_shared_folder()
    {
        var root = Folder("Отпуск");
        Touch("Отпуск/a.heic");
        var shared = Folder("Общая папка");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-jpg"), shared);

        Assert.Equal([Path.Combine(shared, "Отпуск (JPG)")], plan.OutputFolders);
    }

    [Fact]
    public void Plan_combine_makes_one_result_named_after_the_folder()
    {
        var root = Folder("Отпуск");
        var a = Touch("Отпуск/фото 2.jpg");
        var b = Touch("Отпуск/фото 10.png");
        var c = Touch("Отпуск/вложенная/фото 1.heic");
        Touch("Отпуск/clip.mp4");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("images-to-pdf"), null);

        var job = Assert.Single(plan.Jobs);
        Assert.Equal([a, b, c], job.Inputs);
        Assert.Equal(new OutputTarget(_dir.Path, Stem: "Отпуск"), job.Target);
        Assert.Empty(plan.OutputFolders);
        Assert.Equal(1, plan.Skipped);
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "Отпуск (Один PDF)")));
    }

    [Fact]
    public void Plan_combine_with_too_few_files_does_nothing()
    {
        var root = Folder("Отпуск");
        Touch("Отпуск/a.jpg");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("images-to-pdf"), null);

        Assert.Empty(plan.Jobs);
        Assert.Equal(1, plan.Skipped);
    }

    [Fact]
    public void Plan_without_suitable_files_creates_no_folder()
    {
        var root = Folder("Тексты");
        Touch("Тексты/a.txt");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-jpg"), null);

        Assert.Empty(plan.Jobs);
        Assert.Empty(plan.OutputFolders);
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "Тексты (JPG)")));
    }

    [Fact]
    public void Plan_handles_loose_files_as_usual()
    {
        var root = Folder("Отпуск");
        Touch("Отпуск/a.heic");
        var loose = Touch("тест 1.heic");
        var other = Touch("заметки.txt");

        var plan = FolderPlan.Plan(FolderPlan.Expand([root, loose, other]), Op("to-jpg"), null);

        Assert.Contains(plan.Jobs, j => j.Inputs[0] == loose && j.Target is null);
        Assert.Equal(2, plan.Jobs.Count);
        Assert.Equal(1, plan.Skipped);
    }

    [Fact]
    public void Plan_two_folders_with_the_same_name_into_shared_folder()
    {
        var one = Folder("1/Фото");
        var two = Folder("2/Фото");
        Touch("1/Фото/a.heic");
        Touch("2/Фото/b.heic");
        var shared = Folder("Общая");

        var plan = FolderPlan.Plan(FolderPlan.Expand([one, two]), Op("to-jpg"), shared);

        Assert.Equal([Path.Combine(shared, "Фото (JPG)"), Path.Combine(shared, "Фото (JPG) (1)")], plan.OutputFolders);
    }

    [Fact]
    public void Plan_never_lists_originals_as_outputs()
    {
        var root = Folder("Отпуск");
        Touch("Отпуск/a.heic");
        var plan = FolderPlan.Plan(FolderPlan.Expand([root]), Op("to-jpg"), null);
        Assert.All(plan.Jobs, j => Assert.NotEqual(root, j.Target!.Directory));
    }

    // ---------- уборка ----------

    [Fact]
    public void Remove_if_empty_keeps_folders_with_files()
    {
        var empty = Folder("Пусто (JPG)");
        Directory.CreateDirectory(Path.Combine(empty, "день 2", Converter.TempDirName));
        var full = Folder("Полно (JPG)");
        Touch("Полно (JPG)/день 2/a.jpg");

        Assert.True(FolderPlan.RemoveIfEmpty(empty));
        Assert.False(Directory.Exists(empty));
        Assert.False(FolderPlan.RemoveIfEmpty(full));
        Assert.True(File.Exists(Path.Combine(full, "день 2", "a.jpg")));
        Assert.False(FolderPlan.RemoveIfEmpty(Path.Combine(_dir.Path, "нет такой")));
    }

    [Theory]
    [InlineData("фото 2.jpg", "фото 10.jpg")]
    [InlineData("a.jpg", "B.jpg")]
    [InlineData("IMG_0009", "IMG_10")]
    [InlineData("x 1", "x 1 (1)")]
    public void Natural_order(string first, string second)
    {
        Assert.True(NaturalComparer.Instance.Compare(first, second) < 0);
        Assert.True(NaturalComparer.Instance.Compare(second, first) > 0);
        Assert.Equal(0, NaturalComparer.Instance.Compare(first, first));
    }
}
