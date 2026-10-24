namespace DropWheel.Annotate;

/// <summary>
/// Пометки одной картинки и история правок (D-ANNOTATE): каждое изменение — новый неизменяемый список,
/// «Отменить» (Ctrl+Z) и «Повторить» (Ctrl+Y) ходят по этим спискам. Новое изменение после отмены стирает «повторы».
/// Пометки живут только пока открыто окно — история не сохраняется.
/// </summary>
public sealed class AnnotationDocument
{
    /// <summary>Сколько шагов назад можно отменить (старые забываются).</summary>
    public const int MaxHistory = 200;

    private readonly LinkedList<IReadOnlyList<Annotation>> _past = new();
    private readonly Stack<IReadOnlyList<Annotation>> _future = new();
    private IReadOnlyList<Annotation> _saved;

    public AnnotationDocument()
    {
        Shapes = [];
        _saved = Shapes;
    }

    /// <summary>Пометки в порядке рисования (нижняя — первая).</summary>
    public IReadOnlyList<Annotation> Shapes { get; private set; }

    public bool CanUndo => _past.Count > 0;
    public bool CanRedo => _future.Count > 0;
    public bool IsEmpty => Shapes.Count == 0;

    /// <summary>
    /// Есть пометки, которых нет ни в сохранённой, ни в скопированной картинке: Esc и «Отмена» сначала спрашивают.
    /// </summary>
    public bool HasUnsavedChanges => Shapes.Count > 0 && !ReferenceEquals(Shapes, _saved);

    /// <summary>Изменились пометки (добавили, отменили, повторили, удалили).</summary>
    public event Action? Changed;

    /// <summary>Номер для следующего кружка: на единицу больше самого большого из нарисованных (после отмены — снова тот же).</summary>
    public int NextNumber => Shapes.Where(s => s.Tool == AnnotationTool.Number).Select(s => s.Number).DefaultIfEmpty(0).Max() + 1;

    public void Add(Annotation shape) => Apply([.. Shapes, shape]);

    /// <summary>Заменить пометку (например, сдвинутую). Нет такой — ничего.</summary>
    public void Replace(int index, Annotation shape)
    {
        if (index < 0 || index >= Shapes.Count || Equals(Shapes[index], shape)) return;
        var list = Shapes.ToList();
        list[index] = shape;
        Apply(list);
    }

    /// <summary>«Удалить последнюю» — тоже шаг истории (отменяется Ctrl+Z).</summary>
    public bool RemoveLast()
    {
        if (Shapes.Count == 0) return false;
        Apply(Shapes.Take(Shapes.Count - 1).ToList());
        return true;
    }

    public bool Undo()
    {
        if (_past.Count == 0) return false;
        _future.Push(Shapes);
        Shapes = _past.Last!.Value;
        _past.RemoveLast();
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (_future.Count == 0) return false;
        _past.AddLast(Shapes);
        Shapes = _future.Pop();
        Changed?.Invoke();
        return true;
    }

    /// <summary>Картинку с этими пометками сохранили или скопировали — закрывать без вопроса можно.</summary>
    public void MarkSaved() => _saved = Shapes;

    private void Apply(IReadOnlyList<Annotation> next)
    {
        _past.AddLast(Shapes);
        while (_past.Count > MaxHistory) _past.RemoveFirst();
        _future.Clear();
        Shapes = next;
        Changed?.Invoke();
    }
}
