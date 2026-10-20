using System.IO.Compression;
using System.Text;
using DropWheel.Conversions;

namespace DropWheel.Engines;

/// <summary>
/// ZIP средствами .NET (System.IO.Compression), без внешних утилит.
/// «В ZIP» — все выбранные файлы в один архив; «Распаковать» — архив в папку.
/// </summary>
public sealed class ZipEngine : IInternalEngine
{
    public const string PackId = "zip-pack";
    public const string ExtractId = "zip-extract";

    /// <summary>Защита от «zip-бомб»: не распаковываем больше 20 ГБ…</summary>
    public const long MaxExtractedBytes = 20L * 1024 * 1024 * 1024;
    /// <summary>…и больше 100 000 записей.</summary>
    public const int MaxEntries = 100_000;

    private const int BufferSize = 1 << 16;

    public EngineKind Kind => EngineKind.Zip;
    public bool IsAvailable => true;

    public Task RunAsync(Operation op, IReadOnlyList<string> inputs, string output, ConversionSettings settings,
        IProgress<double>? progress, CancellationToken ct) => op.Id switch
    {
        PackId => PackAsync(inputs, output, progress, ct),
        ExtractId => ExtractAsync(inputs[0], output, progress, ct),
        _ => throw new ConversionException($"ZIP не умеет «{op.Label}»", op.Id),
    };

    /// <summary>
    /// Имя архива (без «.zip»): один файл — как файл (photo.jpg → photo.zip);
    /// несколько — как папка первого файла (как «Добавить к "Папка.zip"» в 7-Zip),
    /// а если это корень диска — снова по первому файлу.
    /// </summary>
    public static string ArchiveName(IReadOnlyList<string> inputs)
    {
        var first = inputs[0];
        var stem = Path.GetFileNameWithoutExtension(first);
        if (stem.Length == 0) stem = Path.GetFileName(first); // «.gitignore» → «.gitignore.zip»
        if (inputs.Count == 1) return stem;
        var folder = Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(first)));
        return string.IsNullOrEmpty(folder) ? stem : folder;
    }

    // ---------- Упаковка ----------

    /// <summary>Записи — по именам файлов (UTF-8, .NET ставит флаг UTF-8); одинаковые имена → «name (1).ext».</summary>
    public static async Task PackAsync(IReadOnlyList<string> inputs, string output,
        IProgress<double>? progress, CancellationToken ct)
    {
        var report = new ByteProgress(inputs.Sum(p => new FileInfo(p).Length), progress);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using var stream = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            BufferSize, useAsync: true);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            var entry = zip.CreateEntry(UniqueName(Path.GetFileName(input), used), CompressionLevel.Optimal);
            entry.LastWriteTime = ZipTime(File.GetLastWriteTime(input));

            await using var source = new FileStream(input, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, BufferSize, useAsync: true);
            await using var target = entry.Open();
            await CopyAsync(source, target, long.MaxValue, report, ct);
        }
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var candidate = name;
        for (var i = 1; !used.Add(candidate); i++)
            candidate = $"{stem} ({i}){ext}";
        return candidate;
    }

    /// <summary>В ZIP время хранится в формате DOS: 1980–2107.</summary>
    private static DateTimeOffset ZipTime(DateTime time)
    {
        var min = new DateTime(1980, 1, 1, 0, 0, 0);
        var max = new DateTime(2107, 12, 31, 23, 59, 58);
        return new DateTimeOffset(time < min ? min : time > max ? max : time);
    }

    // ---------- Распаковка ----------

    /// <summary>
    /// Распаковать в пустую папку output. До записи проверяется весь архив: пути внутри папки
    /// (Zip Slip), нет паролей, размер и число записей в пределах лимитов.
    /// </summary>
    public static async Task ExtractAsync(string archive, string output,
        IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read,
                FileShare.Read, BufferSize, useAsync: true);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true, LegacyNameEncoding.Instance);

            var plan = Plan(zip, output);
            var report = new ByteProgress(plan.Sum(p => p.Entry.Length), progress);
            foreach (var (entry, target) in plan)
            {
                ct.ThrowIfCancellationRequested();
                if (target.EndsWith(Path.DirectorySeparatorChar))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                // Совпавшие имена (повтор в архиве, регистр, замена символов) не затирают друг друга.
                var path = Jobs.OutputNamer.FirstFree(target);
                await using (var source = entry.Open())
                await using (var dest = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 BufferSize, useAsync: true))
                {
                    var written = await CopyAsync(source, dest, entry.Length, report, ct);
                    if (written != entry.Length)
                        throw new ConversionException("Архив повреждён", $"{entry.FullName}: {written} из {entry.Length} байт");
                }
                try { File.SetLastWriteTime(path, entry.LastWriteTime.DateTime); } catch { /* не критично */ }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ConversionException("Архив повреждён или сжат неподдерживаемым способом", ex.Message, ex);
        }
    }

    /// <summary>Записи и пути назначения (папки — с разделителем на конце). Бросает, если архив небезопасен.</summary>
    private static List<(ZipArchiveEntry Entry, string Target)> Plan(ZipArchive zip, string output)
    {
        if (zip.Entries.Count > MaxEntries)
            throw new ConversionException("В архиве слишком много файлов", $"{zip.Entries.Count} > {MaxEntries}");
        if (zip.Entries.Count == 0)
            throw new ConversionException("Архив пуст");

        var root = Path.GetFullPath(output);
        if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

        var plan = new List<(ZipArchiveEntry, string)>();
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.IsEncrypted)
                throw new ConversionException("Архив с паролем не поддерживается", entry.FullName);

            var relative = SafeRelativePath(entry.FullName);
            if (relative is null) continue; // «./» и т. п. — сама папка
            var isDir = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            var target = Path.GetFullPath(Path.Combine(root, relative));
            if (!target.StartsWith(root, StringComparison.Ordinal))
                throw Unsafe(entry.FullName);
            if (isDir) target += Path.DirectorySeparatorChar;

            total += entry.Length;
            if (total > MaxExtractedBytes)
                throw new ConversionException("Архив слишком большой: больше 20 ГБ после распаковки", entry.FullName);
            plan.Add((entry, target));
        }
        return plan;
    }

    private static readonly string[] Reserved =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>
    /// Путь записи → безопасный относительный путь (null — пустой).
    /// Абсолютные пути, буквы дисков и «..» — отказ всему архиву (Zip Slip);
    /// недопустимые в Windows символы → «_», имена устройств (CON, NUL…) → «_CON».
    /// </summary>
    internal static string? SafeRelativePath(string entryName)
    {
        var name = entryName.Replace('\\', '/');
        if (name.StartsWith('/') || (name.Length >= 2 && name[1] == ':' && char.IsAsciiLetter(name[0])))
            throw Unsafe(entryName);

        var parts = new List<string>();
        foreach (var part in name.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "..") throw Unsafe(entryName);
            if (part == ".") continue;
            parts.Add(SafeSegment(part));
        }
        return parts.Count == 0 ? null : Path.Combine([.. parts]);
    }

    private static string SafeSegment(string part)
    {
        var chars = part.Select(c => c < 32 || "<>:\"|?*".Contains(c) ? '_' : c).ToArray();
        // Windows молча отрезает точки и пробелы в конце имени.
        var s = new string(chars).TrimEnd('.', ' ');
        if (s.Length == 0) return "_";
        var stem = s.Split('.')[0];
        return Reserved.Contains(stem, StringComparer.OrdinalIgnoreCase) ? "_" + s : s;
    }

    private static ConversionException Unsafe(string entryName) =>
        new("Архив небезопасен: в нём пути за пределы папки", entryName);

    // ---------- Общее ----------

    /// <returns>Сколько байт скопировано. Больше limit — архив врёт о размере (возможная «бомба»).</returns>
    private static async Task<long> CopyAsync(Stream source, Stream target, long limit, ByteProgress report,
        CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            copied += read;
            if (copied > limit)
                throw new ConversionException("Архив повреждён: файл больше заявленного размера");
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            report.Add(read);
        }
        return copied;
    }

    /// <summary>Прогресс по байтам; сообщает не чаще, чем раз в 0,5 %.</summary>
    private sealed class ByteProgress(long total, IProgress<double>? progress)
    {
        private long _done;
        private double _last = -1;

        public void Add(long bytes)
        {
            if (progress is null || total <= 0) return;
            _done += bytes;
            var value = Math.Min(1.0, (double)_done / total);
            if (value - _last < 0.005 && value < 1.0) return;
            _last = value;
            progress.Report(value);
        }
    }

    /// <summary>
    /// Имена записей без флага UTF-8 (бит 11). Записи с флагом .NET сам читает как UTF-8.
    /// Без флага: если байты — корректный UTF-8 (так пишут macOS, Linux, 7-Zip для не-ASCII без флага) —
    /// читаем как UTF-8, иначе — как CP866 (OEM-кодировка русской Windows: так пишут старые архиваторы и
    /// «Сжатая папка» Windows до 10-й). Случайно получить корректный UTF-8 из русского текста в CP866
    /// практически невозможно: строчные «р»–«я» (E0–EF) должны идти ровно перед двумя заглавными/«а»–«п».
    /// </summary>
    internal sealed class LegacyNameEncoding : Encoding
    {
        public static readonly LegacyNameEncoding Instance = new();

        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);
        private static readonly Encoding Oem = CodePagesEncodingProvider.Instance.GetEncoding(866)!;

        private static Encoding Pick(byte[] bytes, int index, int count)
        {
            try
            {
                StrictUtf8.GetCharCount(bytes, index, count);
                return StrictUtf8;
            }
            catch (DecoderFallbackException)
            {
                return Oem;
            }
        }

        public override string GetString(byte[] bytes, int index, int count) =>
            Pick(bytes, index, count).GetString(bytes, index, count);

        public override int GetCharCount(byte[] bytes, int index, int count) =>
            Pick(bytes, index, count).GetCharCount(bytes, index, count);

        public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex) =>
            Pick(bytes, byteIndex, byteCount).GetChars(bytes, byteIndex, byteCount, chars, charIndex);

        // Запись не используется (архив только читается) — на всякий случай UTF-8.
        public override int GetByteCount(char[] chars, int index, int count) => UTF8.GetByteCount(chars, index, count);

        public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex) =>
            UTF8.GetBytes(chars, charIndex, charCount, bytes, byteIndex);

        public override int GetMaxByteCount(int charCount) => UTF8.GetMaxByteCount(charCount);
        public override int GetMaxCharCount(int byteCount) => byteCount;
    }
}
