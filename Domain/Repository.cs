using System.Collections;
using System.Text;

namespace LogiCore.Domain;

public interface IEntity
{
    Guid Id { get; }
}

public interface IReadOnlyRepository<out T> where T : class, IEntity
{
    int Count { get; }

    IEnumerable<T> GetAll();
}

public class Repository<T> : IReadOnlyRepository<T>, IEnumerable<T> where T : class, IEntity
{
    private readonly List<T> _items = new();

    public int Count => _items.Count;

    public T this[Guid id] => _items.Find(x => x.Id == id)
        ?? throw new KeyNotFoundException($"Объект с Id {id} не найден.");

    public void Add(T item) => _items.Add(item);

    public bool Remove(T item) => _items.RemoveAll(x => x.Id == item.Id) > 0;

    public IReadOnlyCollection<T> FindAll(Predicate<T> match) => _items.FindAll(match).AsReadOnly();

    public void Clear() => _items.Clear();

    public IEnumerable<T> GetAll() => this;

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _items.Count; i++)
        {
            yield return _items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public static class EnumerableExtensions
{
    public static string ToReportTable<T>(this IEnumerable<T> items,
        params (string Header, Func<T, string> Value)[] columns)
    {
        var rows = items.Select(x => columns.Select(c => c.Value(x)).ToArray()).ToList();
        var widths = columns
            .Select((c, i) => rows.Select(r => r[i].Length).Append(c.Header.Length).Max())
            .ToArray();

        var text = new StringBuilder();
        text.AppendLine(string.Join(" | ", columns.Select((c, i) => c.Header.PadRight(widths[i]))));
        text.AppendLine(string.Join("-+-", widths.Select(w => new string('-', w))));
        foreach (var row in rows)
        {
            text.AppendLine(string.Join(" | ", row.Select((v, i) => v.PadRight(widths[i]))));
        }

        return text.ToString();
    }
}
