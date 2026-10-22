using DropWheel.Localization;
using DropWheel.Conversions;

namespace DropWheel.Wheel;

/// <summary>
/// Свой порядок и видимость лепестков в одном режиме колеса для одного типа файлов.
/// Order — id операций в нужном порядке (операции, которых тут нет, идут после, в порядке каталога);
/// Hidden — скрытые id; Recent — последняя выбранная операция («последний выбор — первым»).
/// </summary>
public sealed record WheelModePrefs
{
    public IReadOnlyList<string> Order { get; init; } = [];
    public IReadOnlyList<string> Hidden { get; init; } = [];
    public string? Recent { get; init; }

    /// <summary>Свой порядок или скрытые есть (иначе — как в каталоге).</summary>
    public bool IsCustomized => Order.Count > 0 || Hidden.Count > 0;

    public bool Equals(WheelModePrefs? other) =>
        other is not null && Order.SequenceEqual(other.Order) && Hidden.SequenceEqual(other.Hidden)
        && Recent == other.Recent;

    public override int GetHashCode() => HashCode.Combine(Order.Count, Hidden.Count, Recent);
}

/// <summary>Настройки колеса для одного типа файлов: режимы «Конвертация» и «Инструменты».</summary>
public sealed record WheelGroupPrefs
{
    public WheelModePrefs? Convert { get; init; }
    public WheelModePrefs? Tools { get; init; }

    public WheelModePrefs? For(OperationMode mode) => mode == OperationMode.Convert ? Convert : Tools;

    public WheelGroupPrefs With(OperationMode mode, WheelModePrefs? prefs) =>
        mode == OperationMode.Convert ? this with { Convert = prefs } : this with { Tools = prefs };

    public bool IsEmpty => Convert is null && Tools is null;
}

/// <summary>
/// «Своё колесо» (раздел 15 ТЗ): порядок и видимость лепестков по типам файлов (<see cref="WheelGroups"/>)
/// и флаг «последний выбор — первым». Хранится в settings.json; значения неизменяемые — правка даёт новый объект.
/// </summary>
public sealed record WheelPreferences
{
    /// <summary>Последняя выбранная для этого типа операция встаёт на верхний лепесток.</summary>
    public bool MruFirst { get; init; }

    /// <summary>Ключ — id типа (<see cref="WheelGroups"/>).</summary>
    public IReadOnlyDictionary<string, WheelGroupPrefs> Groups { get; init; } =
        new Dictionary<string, WheelGroupPrefs>();

    public WheelModePrefs? For(string group, OperationMode mode) =>
        Groups.TryGetValue(group, out var g) ? g?.For(mode) : null;

    /// <summary>Заменить настройки режима (null — убрать).</summary>
    public WheelPreferences With(string group, OperationMode mode, WheelModePrefs? prefs)
    {
        var groups = new Dictionary<string, WheelGroupPrefs>(Groups);
        var g = (groups.TryGetValue(group, out var old) ? old : null) ?? new WheelGroupPrefs();
        g = g.With(mode, prefs);
        if (g.IsEmpty) groups.Remove(group);
        else groups[group] = g;
        return this with { Groups = groups };
    }

    /// <summary>Свой порядок и видимость режима (последний выбор сохраняется).</summary>
    public WheelPreferences WithLayout(string group, OperationMode mode, IReadOnlyList<string> order, IReadOnlyList<string> hidden)
    {
        var current = For(group, mode) ?? new WheelModePrefs();
        return With(group, mode, current with { Order = order.ToList(), Hidden = hidden.Distinct().ToList() });
    }

    /// <summary>Запомнить выбор. Тот же выбор, что и раньше, — тот же объект (сохранять нечего).</summary>
    public WheelPreferences WithRecent(string group, OperationMode mode, string operationId)
    {
        var current = For(group, mode) ?? new WheelModePrefs();
        return current.Recent == operationId ? this : With(group, mode, current with { Recent = operationId });
    }

    /// <summary>«Сбросить» тип: порядок и видимость — как в каталоге (последний выбор остаётся).</summary>
    public WheelPreferences Reset(string group)
    {
        var result = this;
        foreach (var mode in new[] { OperationMode.Convert, OperationMode.Tool })
        {
            var current = result.For(group, mode);
            if (current is null) continue;
            result = result.With(group, mode, current.Recent is null ? null : new WheelModePrefs { Recent = current.Recent });
        }
        return result;
    }

    /// <summary>Явные null из settings.json (руками правленного) — в пустые значения.</summary>
    public WheelPreferences Sanitized()
    {
        static WheelModePrefs? Fix(WheelModePrefs? m) => m is null ? null : m with
        {
            Order = (m.Order ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            Hidden = (m.Hidden ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
        };
        var groups = new Dictionary<string, WheelGroupPrefs>();
        foreach (var (key, g) in Groups ?? new Dictionary<string, WheelGroupPrefs>())
        {
            if (g is null || string.IsNullOrWhiteSpace(key)) continue;
            var fixedGroup = new WheelGroupPrefs { Convert = Fix(g.Convert), Tools = Fix(g.Tools) };
            if (!fixedGroup.IsEmpty) groups[key] = fixedGroup;
        }
        return this with { Groups = groups };
    }

    public bool Equals(WheelPreferences? other)
    {
        if (other is null || MruFirst != other.MruFirst) return false;
        var a = Groups.Where(p => p.Value is { IsEmpty: false }).ToList();
        var b = other.Groups.Where(p => p.Value is { IsEmpty: false }).ToDictionary(p => p.Key, p => p.Value);
        return a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var v) && Equals(p.Value, v));
    }

    public override int GetHashCode() => HashCode.Combine(MruFirst, Groups.Count(p => p.Value is { IsEmpty: false }));
}

/// <summary>Строка списка в настройках колеса: операция и скрыта ли она.</summary>
public sealed record WheelEntry(Operation Operation, bool Hidden);

/// <summary>
/// Как раскладываются лепестки с учётом <see cref="WheelPreferences"/>: свой порядок, скрытые, «последний — первым».
/// Чистые функции — их используют и колесо при перетаскивании, и меню проводника, и окно настроек.
/// </summary>
public static class WheelArrangement
{
    /// <summary>
    /// Все операции режима в порядке пользователя (вместе со скрытыми; без ограничения в 8 и без «последнего»):
    /// сначала сохранённый порядок, затем новые операции в порядке каталога.
    /// </summary>
    public static IReadOnlyList<WheelEntry> Ordered(IReadOnlyList<Operation> ops, OperationMode mode, WheelModePrefs? prefs)
    {
        var candidates = ops.Where(o => o.Mode == mode).DistinctBy(o => o.Id).ToList();
        if (prefs is null || !prefs.IsCustomized) return candidates.Select(o => new WheelEntry(o, false)).ToList();

        var byId = candidates.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<Operation>();
        foreach (var id in prefs.Order)
            if (byId.TryGetValue(id, out var op) && seen.Add(op.Id)) ordered.Add(op);
        foreach (var op in candidates)
            if (seen.Add(op.Id)) ordered.Add(op);

        var hidden = new HashSet<string>(prefs.Hidden, StringComparer.OrdinalIgnoreCase);
        return ordered.Select(o => new WheelEntry(o, hidden.Contains(o.Id))).ToList();
    }

    /// <summary>
    /// Лепестки обоих режимов для доступных операций: свой порядок, без скрытых, «последний — первым» (если включено),
    /// не больше 8. Если пользователь скрыл всё в обоих режимах — порядок каталога, чтобы колесо
    /// для поддерживаемого файла не было пустым.
    /// </summary>
    public static (IReadOnlyList<WheelItem> Convert, IReadOnlyList<WheelItem> Tools) Arrange(
        IReadOnlyList<Operation> ops, string group, WheelPreferences? prefs)
    {
        prefs ??= new WheelPreferences();
        var convert = Visible(ops, OperationMode.Convert, group, prefs);
        var tools = Visible(ops, OperationMode.Tool, group, prefs);
        if (convert.Count == 0 && tools.Count == 0 && ops.Count > 0)
            return (WheelItems.For(ops, OperationMode.Convert), WheelItems.For(ops, OperationMode.Tool));
        return (convert, tools);
    }

    /// <summary>Лепестки одного режима (без запасного варианта «всё скрыто»).</summary>
    private static IReadOnlyList<WheelItem> Visible(IReadOnlyList<Operation> ops, OperationMode mode, string group, WheelPreferences prefs)
    {
        var modePrefs = prefs.For(group, mode);
        var visible = Ordered(ops, mode, modePrefs).Where(e => !e.Hidden).Select(e => e.Operation).ToList();
        if (prefs.MruFirst && modePrefs?.Recent is { } recent)
        {
            var index = visible.FindIndex(o => string.Equals(o.Id, recent, StringComparison.OrdinalIgnoreCase));
            if (index > 0)
            {
                var op = visible[index];
                visible.RemoveAt(index);
                visible.Insert(0, op);
            }
        }
        return visible.Take(WheelGeometry.MaxSectors).Select(o => new WheelItem(o.Id, Loc.Label(o))).ToList();
    }
}
