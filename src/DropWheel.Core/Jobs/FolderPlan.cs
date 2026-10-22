using DropWheel.Conversions;
using DropWheel.Localization;
using DropWheel.Wheel;

namespace DropWheel.Jobs;

/// <summary>Файлы одной выбранной папки (с подпапками). TooMany — больше предела, файлы не собраны до конца.</summary>
public sealed record FolderScan(string Root, IReadOnlyList<string> Files, bool TooMany);

/// <summary>Выбор пользователя: папки (уже развёрнутые в файлы) и отдельные файлы рядом с ними.</summary>
public sealed record FolderSelection(IReadOnlyList<FolderScan> Folders, IReadOnlyList<string> Files)
{
    /// <summary>Все файлы: сначала из папок (по порядку), затем отдельные.</summary>
    public IReadOnlyList<string> AllFiles => [.. Folders.SelectMany(f => f.Files), .. Files];

    /// <summary>Больше <see cref="FolderPlan.MaxFiles"/> файлов вместе — за раз не берём.</summary>
    public bool TooMany => Folders.Any(f => f.TooMany) || Folders.Sum(f => f.Files.Count) + Files.Count > FolderPlan.MaxFiles;
}

/// <summary>Одна задача плана: входы и своя папка результата (null — как обычно, рядом с исходником).</summary>
public sealed record PlannedJob(IReadOnlyList<string> Inputs, OutputTarget? Target);

/// <summary>
/// Что поставить в очередь для папок: задачи, созданные папки-результаты («Отпуск (JPG)»)
/// и сколько файлов пропущено (операция к ним не подходит).
/// </summary>
public sealed record FolderBatchPlan(IReadOnlyList<PlannedJob> Jobs, IReadOnlyList<string> OutputFolders, int Skipped);

/// <summary>Что показать на колесе: операции, тип файлов (для своего колеса и «последнего выбора») и развёрнутые папки.</summary>
public sealed record WheelOffer(IReadOnlyList<Operation> Operations, string Group, FolderSelection? Folders);

/// <summary>
/// «Папки целиком» (решение D-FOLDERS): папка → её файлы (с подпапками, без скрытых и системных),
/// какие операции предложить, какие задачи поставить и куда класть результат — «Отпуск (JPG)» рядом с «Отпуск».
/// Исходные файлы только читаются.
/// </summary>
public static class FolderPlan
{
    /// <summary>Больше файлов за раз не берём — защита от случайного «Конвертировать…» на «Документах» или диске.</summary>
    public const int MaxFiles = 1000;

    /// <summary>Глубже не спускаемся (и защищаемся от зацикленных ссылок).</summary>
    private const int MaxDepth = 32;

    private static readonly char[] Separators = ['\\', '/'];

    public static bool ContainsFolders(IEnumerable<string> paths, Func<string, bool>? isFolder = null)
    {
        isFolder ??= Directory.Exists;
        return paths.Any(isFolder);
    }

    /// <summary>Развернуть выбор: каждая папка — в свои файлы; отдельные файлы — как есть.</summary>
    public static FolderSelection Expand(IReadOnlyList<string> paths, int maxFiles = MaxFiles)
    {
        var folders = new List<FolderScan>();
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path)) folders.Add(Scan(path, maxFiles));
            else if (File.Exists(path)) files.Add(Path.GetFullPath(path));
        }
        return new(folders, files);
    }

    /// <summary>
    /// Файлы папки с подпапками в порядке проводника: сначала файлы папки («2.jpg» раньше «10.jpg»), затем подпапки.
    /// Пропускаются скрытые и системные файлы и папки (desktop.ini, Thumbs.db, .git), временная папка DropWheel
    /// и ссылки на другие папки (символические ссылки, junction) — чтобы не ходить по кругу.
    /// </summary>
    public static FolderScan Scan(string folder, int maxFiles = MaxFiles)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        if (root.EndsWith(':')) root += Path.DirectorySeparatorChar; // «D:» → «D:\»
        var files = new List<string>();
        var tooMany = false;

        void Walk(DirectoryInfo dir, int depth)
        {
            if (tooMany || depth > MaxDepth) return;
            List<FileInfo> here;
            List<DirectoryInfo> subdirs;
            try
            {
                here = dir.EnumerateFiles().Where(f => Visible(f) && !f.Name.StartsWith(".dropwheel-", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name, NaturalComparer.Instance).ToList();
                subdirs = dir.EnumerateDirectories()
                    .Where(d => Visible(d) && !IsLink(d)
                                && !string.Equals(d.Name, Converter.TempDirName, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => d.Name, NaturalComparer.Instance).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                return; // нет доступа к подпапке — просто без неё
            }

            foreach (var f in here)
            {
                if (files.Count >= maxFiles)
                {
                    tooMany = true;
                    return;
                }
                files.Add(f.FullName);
            }
            foreach (var d in subdirs) Walk(d, depth + 1);
        }

        Walk(new DirectoryInfo(root), 0);
        return new(root, files, tooMany);
    }

    private static bool Visible(FileSystemInfo info) =>
        (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    private static bool IsLink(DirectoryInfo dir)
    {
        try
        {
            return dir.LinkTarget is not null;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Тип, которого в папке больше всего (фото, видео…); при равенстве — тот, что раньше в списке типов.
    /// «Любые файлы» — только если известных типов нет совсем.
    /// </summary>
    public static string DominantGroup(IEnumerable<string> files)
    {
        var counts = files.GroupBy(f => WheelGroups.For(Path.GetExtension(f)))
            .ToDictionary(g => g.Key, g => g.Count());
        return WheelGroups.All.Where(g => g != WheelGroups.Any && counts.ContainsKey(g))
            .OrderByDescending(g => counts[g])
            .FirstOrDefault() ?? WheelGroups.Any;
    }

    /// <summary>
    /// Операции для файлов из папок: по преобладающему типу. Операция подходит, если каждый файл этого типа
    /// она обрабатывает или он уже в нужном формате (JPG в папке для «JPG» — скопируется как есть).
    /// Упаковка всего в один архив («В ZIP», «В 7Z») для папок не предлагается: она складывает файлы без подпапок.
    /// </summary>
    public static IReadOnlyList<Operation> Offer(IReadOnlyList<string> files, Func<Operation, string, bool>? available = null)
    {
        var group = DominantGroup(files);
        var extensions = files.Where(f => WheelGroups.For(Path.GetExtension(f)) == group)
            .GroupBy(f => Extensions.Normalize(Path.GetExtension(f)))
            .Select(g => (Ext: g.Key, Count: g.Count()))
            .ToList();
        if (extensions.Count == 0) return [];

        var result = new List<Operation>();
        foreach (var op in OperationCatalog.All)
        {
            if (op.Inputs == InputMode.Combine && op.Kind == MediaKind.Any) continue;
            var accepted = 0;
            var ok = true;
            foreach (var (ext, count) in extensions)
            {
                if (Accepts(op, ext, available)) accepted += count;
                else if (!op.AlreadyIn(ext))
                {
                    ok = false;
                    break;
                }
            }
            if (ok && accepted > 0 && accepted >= op.MinFiles) result.Add(op);
        }
        return result;
    }

    /// <summary>
    /// Что показать на колесе для выбора (меню проводника, перетаскивание): без папок — как раньше (общие операции файлов),
    /// с папками — по их содержимому (<see cref="Offer"/>). Слишком много файлов — пусто.
    /// </summary>
    public static WheelOffer OfferFor(IReadOnlyList<string> paths, Func<Operation, string, bool>? available = null,
        int maxFiles = MaxFiles)
    {
        if (!ContainsFolders(paths))
            return new(OperationCatalog.ForFiles(paths, available), WheelGroups.ForFiles(paths), null);
        var selection = Expand(paths, maxFiles);
        return OfferFor(selection, available);
    }

    /// <summary>То же для уже развёрнутых папок.</summary>
    public static WheelOffer OfferFor(FolderSelection selection, Func<Operation, string, bool>? available = null)
    {
        var files = selection.AllFiles;
        if (selection.TooMany || files.Count == 0) return new([], WheelGroups.Any, selection);
        return new(Offer(files, available), DominantGroup(files), selection);
    }

    private static bool Accepts(Operation op, string extension, Func<Operation, string, bool>? available) =>
        op.AppliesTo(extension) && (available is null || available(op, extension));

    /// <summary>
    /// Пометка папки-результата: у конвертации — формат («JPG»; уточнение в скобках отбрасывается:
    /// «PNG (страницы)» → «PNG»), у инструмента — его пометка («сжато»), если она есть, иначе название.
    /// </summary>
    public static string Tag(Operation op)
    {
        string tag;
        var suffix = Loc.Suffix(op).Trim();
        if (op.Mode == OperationMode.Tool && suffix.Length > 2 && suffix[0] == '(' && suffix[^1] == ')')
            tag = suffix[1..^1];
        else
        {
            tag = Loc.Label(op);
            var bracket = tag.IndexOf(" (", StringComparison.Ordinal);
            if (bracket > 0) tag = tag[..bracket];
        }
        // Символы, которых не бывает в именах Windows.
        tag = new string(tag.Where(c => !"\\/:*?\"<>|".Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        return tag.Length > 0 ? tag : op.Id;
    }

    /// <summary>Имя папки (у корня диска «D:\» — «D»).</summary>
    public static string FolderName(string folder)
    {
        // Вручную, а не Path.GetFileName: «\» — разделитель и в тестах на Linux.
        var trimmed = folder.TrimEnd(Separators);
        var name = trimmed[(trimmed.LastIndexOfAny(Separators) + 1)..].TrimEnd(':');
        return name.Length > 0 ? name : "DropWheel";
    }

    /// <summary>«Отпуск» + «JPG» → «Отпуск (JPG)».</summary>
    public static string OutputFolderName(string folder, Operation op) => $"{FolderName(folder)} ({Tag(op)})";

    /// <summary>Где лежат результаты для папки: «В одну папку» из настроек, иначе рядом с самой папкой.</summary>
    public static string ResultParent(string folder, string? sharedOutput)
    {
        if (sharedOutput is { Length: > 0 }) return sharedOutput;
        return Path.GetDirectoryName(folder.TrimEnd(Separators)) is { Length: > 0 } parent
            ? parent
            : folder; // корень диска: «D:\D (JPG)»
    }

    /// <summary>
    /// Задачи для выбранной операции. Для каждой папки:
    /// по файлу — в «Отпуск (JPG)» с теми же подпапками (файлы уже в нужном формате копируются, пометки в именах нет);
    /// Combine («Один PDF», «Склеить») — один результат по имени папки рядом с ней: «Отпуск (все).pdf».
    /// Неподходящие файлы пропускаются. Отдельные файлы из того же выбора — как обычно, рядом с собой.
    /// </summary>
    /// <param name="reserve">Занять папку-результат: свободное имя («Отпуск (JPG) (1)») и создать её.</param>
    public static FolderBatchPlan Plan(FolderSelection selection, Operation op, string? sharedOutput,
        Func<Operation, string, bool>? available = null, Func<string, string>? reserve = null)
    {
        reserve ??= ReserveFolder;
        var jobs = new List<PlannedJob>();
        var roots = new List<string>();
        var skipped = 0;

        bool accepts(string f) => Accepts(op, Path.GetExtension(f), available);
        bool copies(string f) => !accepts(f) && op.AlreadyIn(Path.GetExtension(f));

        foreach (var scan in selection.Folders)
        {
            var parent = ResultParent(scan.Root, sharedOutput);
            if (op.Inputs == InputMode.Combine)
            {
                var accepted = scan.Files.Where(accepts).ToList();
                if (accepted.Count > 0 && accepted.Count >= op.MinFiles)
                {
                    jobs.Add(new(accepted, new OutputTarget(parent, Stem: FolderName(scan.Root))));
                    skipped += scan.Files.Count - accepted.Count;
                }
                else skipped += scan.Files.Count;
                continue;
            }

            var work = scan.Files.Where(f => accepts(f) || copies(f)).ToList();
            skipped += scan.Files.Count - work.Count;
            if (work.Count == 0) continue;

            var outRoot = reserve(Path.Combine(parent, OutputFolderName(scan.Root, op)));
            roots.Add(outRoot);
            foreach (var file in work)
            {
                var relative = Path.GetRelativePath(scan.Root, Path.GetDirectoryName(file)!);
                var dir = relative == "." ? outRoot : Path.Combine(outRoot, relative);
                jobs.Add(new([file], new OutputTarget(dir, KeepSuffix: false, CopySameFormat: true)));
            }
        }

        var loose = selection.Files.Where(accepts).ToList();
        if (op.Inputs == InputMode.Combine)
        {
            if (loose.Count > 0 && loose.Count >= op.MinFiles) jobs.Add(new(loose, null));
            else loose = [];
        }
        else jobs.AddRange(loose.Select(f => new PlannedJob([f], null)));
        skipped += selection.Files.Count - loose.Count;

        return new(jobs, roots, skipped);
    }

    /// <summary>Свободное имя для папки-результата («Отпуск (JPG) (1)», если занято) — сразу создаётся, чтобы его не заняли.</summary>
    public static string ReserveFolder(string desired)
    {
        var path = OutputNamer.FirstFree(desired, folder: true);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Убрать папку-результат, если в ней не оказалось ни одного файла (все задачи с ошибкой или отменены).
    /// Удаляется только папка без файлов — исходники в ней не лежат никогда.
    /// </summary>
    public static bool RemoveIfEmpty(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return false;
            if (Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any()) return false;
            Directory.Delete(folder, recursive: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>Сравнение имён как в проводнике: «фото 2» раньше «фото 10», без учёта регистра.</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static NaturalComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                var c = string.CompareOrdinal(a, b);
                if (c != 0) return c;
                continue;
            }
            var cmp = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (cmp != 0) return cmp;
            i++;
            j++;
        }
        var rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
