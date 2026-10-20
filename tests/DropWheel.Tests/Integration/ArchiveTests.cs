using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests.Integration;

/// <summary>
/// Архивы через настоящий 7-Zip (7z / 7zz / 7za; на Windows — tools\7zip\7z.exe) в папке «Мои фото».
/// Архивы для распаковки создаются здесь же: tar — средствами .NET (TarWriter), 7z/bz2/xz — самим 7-Zip.
/// RAR создать нечем (rar — платный), его распаковка здесь не проверяется.
/// </summary>
public class ArchiveTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(Tools.Locator, ConversionSettings.Default);
    private static readonly Operation Extract = OperationCatalog.Find("archive-extract")!;
    private static readonly Operation Pack = OperationCatalog.Find("pack-7z")!;

    public void Dispose() => _dir.Dispose();

    private static bool Has7z => Tools.Locator.IsAvailable(EngineKind.SevenZip);
    private static string SevenZipExe => Tools.Locator.Resolve(EngineKind.SevenZip);

    /// <summary>Папка над «Мои фото» — сюда «выпрыгивают» файлы при Zip Slip.</summary>
    private string Root => Path.GetDirectoryName(_dir.Path)!;

    private async Task<string> Run(Operation op, params string[] inputs)
    {
        var hashes = inputs.Select(Tools.Hash).ToList();
        var output = await _converter.ConvertAsync(inputs, op);
        Assert.Equal(hashes, inputs.Select(Tools.Hash)); // исходники не тронуты
        AssertNoTempLeft();
        return output;
    }

    private void AssertNoTempLeft() =>
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*"));

    /// <summary>Все файлы папки: относительный путь (через «/») → содержимое.</summary>
    private static Dictionary<string, string> Files(string folder) =>
        Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(folder, f).Replace('\\', '/'),
            f => File.ReadAllText(f));

    private static readonly (string Name, string Text)[] Sample =
    [
        ("тест 1.txt", "привет"),
        ("папка с пробелом/вложенный файл.txt", "вложенный"),
        ("b.txt", "b"),
    ];

    private static void AssertSample(string folder) =>
        Assert.Equal(Sample.ToDictionary(e => e.Name, e => e.Text), Files(folder));

    private string MakeTar(string name, IEnumerable<(string Name, string Text)> entries)
    {
        var path = Path.Combine(_dir.Path, name);
        using var stream = File.Create(path);
        using var writer = new TarWriter(stream, TarEntryFormat.Pax);
        foreach (var (entry, text) in entries)
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entry)
                { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
        return path;
    }

    /// <summary>Архив 7-Zip'ом (не нашим кодом): 7z из папки с файлами Sample или сжатие одного файла (gzip/bzip2/xz).</summary>
    private void SevenZipAdd(string type, string archive, params string[] inputs) =>
        Tools.Run(SevenZipExe, ["a", "-t" + type, "-bso0", "-bsp0", "-sccUTF-8", archive, "--", .. inputs]);

    private string SampleFolder()
    {
        var folder = Path.Combine(Root, "исходные файлы");
        foreach (var (name, text) in Sample)
        {
            var path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        return folder;
    }

    // ---------- Распаковка ----------

    [SkippableFact]
    public async Task Extract_7z_with_cyrillic_and_subfolders()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var archive = Path.Combine(_dir.Path, "фото архив.7z");
        SevenZipAdd("7z", archive, Path.Combine(SampleFolder(), "*"));

        var output = await Run(Extract, archive);

        Assert.Equal(Path.Combine(_dir.Path, "фото архив"), output);
        AssertSample(output);
    }

    [SkippableFact]
    public async Task Extract_tar()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var output = await Run(Extract, MakeTar("фото архив.tar", Sample));
        Assert.Equal(Path.Combine(_dir.Path, "фото архив"), output);
        AssertSample(output);
    }

    [SkippableTheory]
    [InlineData("фото архив.tar.gz")]
    [InlineData("фото архив.tgz")]
    public async Task Extract_tar_gz_gives_files_not_tar(string name)
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var archive = Path.Combine(_dir.Path, name);
        // GZipStream не пишет имя внутреннего файла — 7-Zip называет его по архиву (…tar / …tgz → .tar).
        using (var tar = new MemoryStream())
        {
            using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
                foreach (var (entry, text) in Sample)
                    writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entry)
                        { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(text)) });
            using var gz = new GZipStream(File.Create(archive), CompressionLevel.Optimal);
            tar.Position = 0;
            tar.CopyTo(gz);
        }

        var progress = new List<double>();
        var output = await _converter.ConvertAsync([archive], Extract, new SyncProgress(progress.Add));

        Assert.Equal(Path.Combine(_dir.Path, "фото архив"), output); // без «.tar» в имени
        AssertSample(output);
        AssertNoTempLeft();
        Assert.NotEmpty(progress);
        Assert.All(progress, p => Assert.InRange(p, 0, 1));
        Assert.Equal(1.0, progress[^1]);
    }

    [SkippableTheory]
    [InlineData("gzip", "фото архив.tar.gz")]
    [InlineData("bzip2", "фото архив.tar.bz2")]
    [InlineData("xz", "фото архив.tar.xz")]
    public async Task Extract_compressed_tar_made_by_7zip(string type, string name)
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        // Внутренний .tar — с тем же именем, что у архива без последнего расширения (так делают tar и gzip).
        var tar = MakeTar(Path.GetFileNameWithoutExtension(name), Sample);
        var archive = Path.Combine(_dir.Path, name);
        SevenZipAdd(type, archive, tar);
        File.Delete(tar);

        var output = await Run(Extract, archive);

        Assert.Equal(Path.Combine(_dir.Path, "фото архив"), output);
        AssertSample(output);
    }

    [SkippableFact]
    public async Task Gz_of_a_plain_file_is_just_decompressed()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var archive = Path.Combine(_dir.Path, "заметка.txt.gz");
        using (var gz = new GZipStream(File.Create(archive), CompressionLevel.Optimal))
            gz.Write(Encoding.UTF8.GetBytes("просто текст"));

        var output = await Run(Extract, archive);

        Assert.Equal(Path.Combine(_dir.Path, "заметка.txt"), output);
        Assert.Equal(new Dictionary<string, string> { ["заметка.txt"] = "просто текст" }, Files(output));
    }

    [SkippableFact]
    public async Task Extract_twice_gets_numbered_folder()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var archive = MakeTar("архив.tar", Sample);
        await Run(Extract, archive);
        Assert.Equal(Path.Combine(_dir.Path, "архив (1)"), await Run(Extract, archive));
    }

    [SkippableFact]
    public async Task Tar_with_dotdot_and_absolute_paths_stays_inside_folder()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        // Абсолютный путь без буквы диска: на Windows «/Users/…» — тоже от корня текущего диска.
        var absolute = Path.Combine(Root, "абсолютный.txt");
        var rooted = absolute[Path.GetPathRoot(absolute)!.Length..].Replace('\\', '/');
        var archive = MakeTar("злой архив.tar",
        [
            ("хороший.txt", "ok"),
            ("../выпрыгнул.txt", "evil"),
            ("папка/../../выпрыгнул 2.txt", "evil"),
            ("/" + rooted, "evil"),
        ]);

        string? output = null;
        try { output = await _converter.ConvertAsync([archive], Extract); }
        catch (ConversionException) { /* отказ целиком — тоже безопасно */ }

        // Главное: за пределы папки результата ничего не записано.
        Assert.False(File.Exists(absolute));
        Assert.False(File.Exists(Path.Combine(Root, "выпрыгнул.txt")));
        Assert.False(File.Exists(Path.Combine(Root, "выпрыгнул 2.txt")));
        var outside = Directory.GetFiles(Root, "*", SearchOption.AllDirectories)
            .Where(f => output is null || !f.StartsWith(output + Path.DirectorySeparatorChar))
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'));
        Assert.Equal(["Мои фото/злой архив.tar"], outside);
        AssertNoTempLeft();

        if (output is not null)
        {
            Assert.Equal(Path.Combine(_dir.Path, "злой архив"), output);
            Assert.Equal("ok", File.ReadAllText(Path.Combine(output, "хороший.txt")));
        }
        // 7-Zip 23.01 (Linux) отрезает «..» и корень: все 4 файла — внутри папки, архив не отклонён.
        if (!OperatingSystem.IsWindows())
            Assert.Equal(4, Directory.GetFiles(Assert.IsType<string>(output), "*", SearchOption.AllDirectories).Length);
    }

    [SkippableFact]
    public async Task Links_out_of_folder_do_not_touch_outside_files()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var victim = Path.Combine(Root, "жертва.txt");
        File.WriteAllText(victim, "original");
        var archive = Path.Combine(_dir.Path, "ссылки.tar");
        using (var stream = File.Create(archive))
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax))
        {
            // Ссылка на папку выше, потом файл «через» неё; ссылка на чужой файл, потом запись в неё.
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "вверх") { LinkName = ".." });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "вверх/выпрыгнул.txt")
                { DataStream = new MemoryStream("evil"u8.ToArray()) });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "жертва.txt") { LinkName = "../жертва.txt" });
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "жертва.txt")
                { DataStream = new MemoryStream("pwned"u8.ToArray()) });
        }

        var ex = await Record.ExceptionAsync(() => _converter.ConvertAsync([archive], Extract));

        Assert.Equal("original", File.ReadAllText(victim));
        Assert.False(File.Exists(Path.Combine(Root, "выпрыгнул.txt")));
        AssertNoTempLeft();
        if (!OperatingSystem.IsWindows()) // проверено на 7-Zip 23.01 (Linux): опасные ссылки — ошибка
            Assert.Equal("Архив небезопасен: в нём ссылки за пределы папки", Assert.IsType<ConversionException>(ex).Message);
    }

    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)] // зашифрованы и имена файлов (-mhe)
    public async Task Password_archive_fails_clearly(bool encryptHeaders)
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var archive = Path.Combine(_dir.Path, "секрет.7z");
        var file = _dir.File("секрет.txt", "тайна");
        Tools.Run(SevenZipExe, ["a", "-t7z", "-bso0", "-bsp0", "-pпароль", encryptHeaders ? "-mhe=on" : "-mhe=off", archive, "--", file]);
        File.Delete(file);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([archive], Extract));

        Assert.Equal("Архив с паролем не поддерживается", ex.Message);
        Assert.Equal([archive], Directory.GetFileSystemEntries(_dir.Path)); // ни папки, ни временных файлов
    }

    [SkippableFact]
    public async Task Not_an_archive_fails_clearly_without_leftovers()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var broken = _dir.File("сломанный.7z", "это не архив");
        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([broken], Extract));
        Assert.Equal("Файл повреждён или это не архив", ex.Message);
        Assert.Equal([broken], Directory.GetFileSystemEntries(_dir.Path));
    }

    // ---------- Упаковка ----------

    /// <summary>Записи архива по `7z l -slt`: путь → размер.</summary>
    private static Dictionary<string, long> List7z(string archive)
    {
        var entries = new Dictionary<string, long>();
        string? path = null;
        // Первый «Path =» — сам архив (до строки «----------»).
        var lines = Tools.Run(SevenZipExe, "l", "-slt", "-sccUTF-8", "--", archive).Split('\n').Select(l => l.TrimEnd('\r'));
        foreach (var line in lines.SkipWhile(l => !l.StartsWith("----------")))
        {
            if (line.StartsWith("Path = ")) path = line[7..];
            else if (line.StartsWith("Size = ") && path is not null) entries[path] = long.Parse(line[7..]);
        }
        return entries;
    }

    [SkippableFact]
    public async Task Pack_several_files_by_name_only_named_after_folder()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var a = _dir.File("тест 1.txt", "привет");
        var b = _dir.File("второй файл.txt", "второй");
        Directory.CreateDirectory(Path.Combine(_dir.Path, "подпапка"));
        var c = Path.Combine(_dir.Path, "подпапка", "ёлка [1].txt");
        File.WriteAllText(c, "ёлка");

        var output = await Run(Pack, a, b, c);

        // Как у «В ZIP»: несколько файлов — по папке первого («Мои фото»), рядом с ним.
        Assert.Equal(Path.Combine(_dir.Path, "Мои фото.7z"), output);
        var entries = List7z(output);
        Assert.Equal(["второй файл.txt", "тест 1.txt", "ёлка [1].txt"], entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(Encoding.UTF8.GetByteCount("привет"), entries["тест 1.txt"]);

        // И обратно — нашей же распаковкой.
        var folder = await Run(Extract, output);
        Assert.Equal(new Dictionary<string, string>
        {
            ["тест 1.txt"] = "привет", ["второй файл.txt"] = "второй", ["ёлка [1].txt"] = "ёлка",
        }, Files(folder));
    }

    [SkippableFact]
    public async Task Pack_one_file_named_like_the_file()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var output = await Run(Pack, _dir.File("тест 1.txt", "привет"));
        Assert.Equal(Path.Combine(_dir.Path, "тест 1.7z"), output);
        Assert.Equal(["тест 1.txt"], List7z(output).Keys);
    }

    [SkippableFact]
    public async Task Progress_is_read_from_7zip_output()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var data = new byte[8 << 20];
        new Random(1).NextBytes(data);
        var src = Path.Combine(_dir.Path, "большой файл.bin");
        File.WriteAllBytes(src, data);

        var progress = new List<double>();
        await _converter.ConvertAsync([src], Pack, new SyncProgress(progress.Add));

        Assert.All(progress, p => Assert.InRange(p, 0, 1));
        // Проценты из -bsp1 (хотя бы начальные «0%») — проверено на 7-Zip 23.01 (Linux), на Windows не проверялось.
        if (!OperatingSystem.IsWindows()) Assert.Contains(progress, p => p < 1.0);
        Assert.Equal(1.0, progress[^1]);
    }

    [SkippableFact]
    public async Task Pack_same_names_from_different_folders_fails_clearly()
    {
        Skip.IfNot(Has7z, "нет 7-Zip");
        var a = _dir.File("одно имя.txt", "a");
        Directory.CreateDirectory(Path.Combine(_dir.Path, "другая"));
        var b = Path.Combine(_dir.Path, "другая", "одно имя.txt");
        File.WriteAllText(b, "b");

        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([a, b], Pack));

        Assert.Equal("В 7Z нельзя положить файлы с одинаковыми именами из разных папок", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir.Path, "Мои фото.7z")));
        AssertNoTempLeft();
    }

    /// <summary>Progress&lt;T&gt; шлёт значения через контекст синхронизации — в тесте нужен синхронный.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        private readonly object _lock = new();
        public void Report(double value) { lock (_lock) report(value); }
    }
}
