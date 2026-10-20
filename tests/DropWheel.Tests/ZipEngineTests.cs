using System.IO.Compression;
using System.Text;
using DropWheel.Conversions;
using DropWheel.Engines;
using DropWheel.Jobs;

namespace DropWheel.Tests;

public class ZipEngineTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly Converter _converter = new(new ToolLocator(), ConversionSettings.Default, [new ZipEngine()]);
    private static readonly Operation Pack = OperationCatalog.Find("zip-pack")!;
    private static readonly Operation Extract = OperationCatalog.Find("zip-extract")!;

    public void Dispose() => _dir.Dispose();

    private string Bytes(string relative, int size, int seed = 1)
    {
        var path = Path.Combine(_dir.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    private string MakeZip(string name, params (string Entry, string Content)[] entries) =>
        MakeZip(name, null, entries);

    private string MakeZip(string name, Encoding? encoding, params (string Entry, string Content)[] entries)
    {
        var path = Path.Combine(_dir.Path, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create, encoding);
        foreach (var (entry, content) in entries)
        {
            var e = zip.CreateEntry(entry, CompressionLevel.NoCompression);
            if (entry.EndsWith('/')) continue;
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(content);
        }
        return path;
    }

    private void AssertNoTempLeft() =>
        Assert.Empty(Directory.GetFileSystemEntries(_dir.Path, ".dropwheel-*"));

    // ---------- Упаковка ----------

    [Fact]
    public async Task Pack_one_file_named_like_the_file()
    {
        var src = Bytes("фото 1.jpg", 100_000);

        var result = await _converter.ConvertAsync([src], Pack);

        Assert.Equal(Path.Combine(_dir.Path, "фото 1.zip"), result);
        using var zip = ZipFile.OpenRead(result);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("фото 1.jpg", entry.FullName);
        using var ms = new MemoryStream();
        entry.Open().CopyTo(ms);
        Assert.Equal(File.ReadAllBytes(src), ms.ToArray());
        // Флаг UTF-8 (бит 11) — проводник Windows показывает кириллицу правильно.
        Assert.All(ZipBytes.CentralFlags(result), f => Assert.True((f & 0x800) != 0));
        AssertNoTempLeft();
    }

    [Fact]
    public async Task Pack_several_files_with_duplicate_names()
    {
        var a = Bytes("отчёт.txt", 5000, seed: 1);
        var b = Bytes(Path.Combine("Другая папка", "Отчёт.TXT"), 6000, seed: 2);
        var c = Bytes("песня.mp3", 7000, seed: 3);
        var progress = new List<double>();

        var result = await _converter.ConvertAsync([a, b, c], Pack, new SyncProgress(progress.Add));

        // Несколько файлов — архив по имени папки первого файла.
        Assert.Equal(Path.Combine(_dir.Path, "Мои фото.zip"), result);
        using var zip = ZipFile.OpenRead(result);
        Assert.Equal(["отчёт.txt", "Отчёт (1).TXT", "песня.mp3"], zip.Entries.Select(e => e.FullName));
        foreach (var (entry, file) in zip.Entries.Zip([a, b, c]))
        {
            using var ms = new MemoryStream();
            entry.Open().CopyTo(ms);
            Assert.Equal(File.ReadAllBytes(file), ms.ToArray());
        }
        Assert.Equal(1.0, progress[^1]);
        Assert.Equal(progress.Order(), progress);
    }

    [Fact]
    public async Task Pack_through_queue_makes_one_job()
    {
        var queue = new JobQueue(_converter);
        var jobs = queue.EnqueueSelection([_dir.File("a.txt", "1"), _dir.File("b.txt", "2")], Pack);
        var job = Assert.Single(jobs);
        await job.Completion;
        Assert.Equal(JobState.Done, job.State);
        Assert.EndsWith(".zip", job.OutputPath);
    }

    [Theory]
    [InlineData(new[] { "/д/фото.jpg" }, "фото")]
    [InlineData(new[] { "/д/.gitignore" }, ".gitignore")]
    [InlineData(new[] { "/д/Отпуск/1.jpg", "/д/Отпуск/2.jpg" }, "Отпуск")]
    [InlineData(new[] { "/1.jpg", "/2.jpg" }, "1")]
    public void Archive_name_rule(string[] inputs, string expected)
    {
        Assert.Equal(expected, ZipEngine.ArchiveName(inputs));
    }

    [Fact]
    public async Task Pack_cancel_leaves_nothing()
    {
        var src = Bytes("большой.bin", 30_000_000);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _converter.ConvertAsync([src], Pack, new SyncProgress(_ => cts.Cancel()), ct: cts.Token));

        AssertNoTempLeft();
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.zip"));
        Assert.True(File.Exists(src));
    }

    // ---------- Распаковка ----------

    [Fact]
    public async Task Extract_into_folder_named_after_archive_then_numbered()
    {
        var zip = MakeZip("архив фото.zip",
            ("a.txt", "привет"), ("папка/б.txt", "мир"), ("пустая/", ""));

        var first = await _converter.ConvertAsync([zip], Extract);
        var second = await _converter.ConvertAsync([zip], Extract);

        Assert.Equal(Path.Combine(_dir.Path, "архив фото"), first);
        Assert.Equal(Path.Combine(_dir.Path, "архив фото (1)"), second);
        Assert.Equal("привет", File.ReadAllText(Path.Combine(first, "a.txt")));
        Assert.Equal("мир", File.ReadAllText(Path.Combine(first, "папка", "б.txt")));
        Assert.True(Directory.Exists(Path.Combine(first, "пустая")));
        Assert.True(File.Exists(zip));
        AssertNoTempLeft();
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("/abs.txt")]
    [InlineData("\\abs.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("C:\\evil.txt")]
    [InlineData("c:evil.txt")]
    public async Task Zip_slip_archive_is_rejected(string evil)
    {
        var zip = MakeZip("злой.zip", ("хороший.txt", "ok"), (evil, "pwned"));
        var parent = Path.GetDirectoryName(_dir.Path)!;
        var before = Directory.GetFileSystemEntries(parent, "*", SearchOption.AllDirectories).Order().ToList();

        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([zip], Extract));

        Assert.Contains("небезопасен", ex.Message);
        Assert.Equal(before, Directory.GetFileSystemEntries(parent, "*", SearchOption.AllDirectories).Order());
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "злой")));
    }

    [Theory]
    [InlineData("a/b.txt", "a/b.txt")]
    [InlineData("./a//b.txt", "a/b.txt")]
    [InlineData("a\\b.txt", "a/b.txt")]
    [InlineData("что?.txt", "что_.txt")]
    [InlineData("x/CON.txt", "x/_CON.txt")]
    [InlineData("нет точки. ", "нет точки")]
    [InlineData("a/b:c.txt", "a/b_c.txt")]
    public void Entry_names_are_made_safe(string entry, string expected)
    {
        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), ZipEngine.SafeRelativePath(entry));
    }

    [Fact]
    public async Task Encrypted_archive_is_rejected()
    {
        var zip = MakeZip("секрет.zip", ("a.txt", "тайна"));
        ZipBytes.PatchFlags(zip, set: 0x1, clear: 0);

        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([zip], Extract));

        Assert.Equal("Архив с паролем не поддерживается", ex.Message);
        AssertNoTempLeft();
    }

    [Fact]
    public async Task Legacy_cp866_names_are_decoded()
    {
        var cp866 = CodePagesEncodingProvider.Instance.GetEncoding(866)!;
        var zip = MakeZip("старый.zip", cp866, ("Привет мир.txt", "1"), ("Папка/яблоко.txt", "2"));
        Assert.All(ZipBytes.CentralFlags(zip), f => Assert.True((f & 0x800) == 0));

        var dir = await _converter.ConvertAsync([zip], Extract);

        Assert.True(File.Exists(Path.Combine(dir, "Привет мир.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "Папка", "яблоко.txt")));
    }

    [Fact]
    public async Task Utf8_names_without_flag_are_decoded()
    {
        // Так пишут macOS и Linux: байты UTF-8, но флаг не стоит.
        var zip = MakeZip("мак.zip", ("Ёжик в тумане.txt", "1"));
        ZipBytes.PatchFlags(zip, set: 0, clear: 0x800);

        var dir = await _converter.ConvertAsync([zip], Extract);

        Assert.True(File.Exists(Path.Combine(dir, "Ёжик в тумане.txt")));
    }

    [Fact]
    public async Task Entry_larger_than_declared_is_rejected()
    {
        var zip = MakeZip("бомба.zip", ("a.txt", new string('x', 1000)));
        ZipBytes.PatchCentralUncompressedSize(zip, 10);

        await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([zip], Extract));
        AssertNoTempLeft();
    }

    [Fact]
    public async Task Empty_and_broken_archives_fail_clearly()
    {
        var empty = MakeZip("пустой.zip");
        var ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([empty], Extract));
        Assert.Equal("Архив пуст", ex.Message);

        var broken = _dir.File("сломанный.zip", "это не архив");
        ex = await Assert.ThrowsAsync<ConversionException>(() => _converter.ConvertAsync([broken], Extract));
        Assert.Contains("повреждён", ex.Message);
        AssertNoTempLeft();
    }

    [Fact]
    public async Task Extract_cancel_leaves_nothing()
    {
        var src = Bytes("большой.bin", 30_000_000);
        var zip = await _converter.ConvertAsync([src], Pack);
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _converter.ConvertAsync([zip], Extract, new SyncProgress(_ => cts.Cancel()), ct: cts.Token));

        AssertNoTempLeft();
        Assert.False(Directory.Exists(Path.Combine(_dir.Path, "большой")));
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

/// <summary>Правка заголовков ZIP для тестов (флаги, размер). Архивы маленькие, без сжатия.</summary>
internal static class ZipBytes
{
    private const uint Local = 0x04034b50, Central = 0x02014b50;

    private static IEnumerable<int> Find(byte[] data, uint signature)
    {
        for (var i = 0; i + 4 <= data.Length; i++)
            if (BitConverter.ToUInt32(data, i) == signature) yield return i;
    }

    public static IReadOnlyList<ushort> CentralFlags(string path)
    {
        var data = File.ReadAllBytes(path);
        return Find(data, Central).Select(i => BitConverter.ToUInt16(data, i + 8)).ToList();
    }

    public static void PatchFlags(string path, ushort set, ushort clear)
    {
        var data = File.ReadAllBytes(path);
        foreach (var at in Find(data, Local).Select(i => i + 6).Concat(Find(data, Central).Select(i => i + 8)))
        {
            var flags = (ushort)((BitConverter.ToUInt16(data, at) | set) & ~clear);
            BitConverter.GetBytes(flags).CopyTo(data, at);
        }
        File.WriteAllBytes(path, data);
    }

    public static void PatchCentralUncompressedSize(string path, uint size)
    {
        var data = File.ReadAllBytes(path);
        foreach (var i in Find(data, Central).ToList())
            BitConverter.GetBytes(size).CopyTo(data, i + 24);
        File.WriteAllBytes(path, data);
    }
}
