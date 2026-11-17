namespace KosHome;

/// <summary>Which items opened or closed since the last <see cref="Step"/>: KosHome draws the apps' windows itself, so it
/// raises OnOpen/OnClose the way Dalamud's WindowHost does (an item is closed until first seen open).</summary>
internal sealed class OpenTracker<T> where T : notnull
{
    private Dictionary<T, bool> last = [];

    public void Step(IEnumerable<(T Item, bool Open)> now, List<T> opened, List<T> closed)
    {
        var next = new Dictionary<T, bool>();
        foreach (var (item, open) in now)
        {
            next[item] = open;
            var was = last.TryGetValue(item, out var w) && w;
            if (open && !was) opened.Add(item);
            else if (!open && was) closed.Add(item);
        }

        last = next; // an item no longer listed belongs to a stopped app: forgotten, no OnClose on a disposed window
    }
}
