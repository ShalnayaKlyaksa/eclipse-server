using System.Linq;

namespace Content.Client.PDA;

/// <summary>Saved cells always use eight columns, independently of the current window size.</summary>
public static class PdaDesktopLayout
{
    public const int Stride = 8;

    public static Dictionary<string, int> Arrange(IEnumerable<string> apps, IReadOnlyDictionary<string, int> saved, int columns, int rows)
    {
        var ids = apps.ToArray();
        var result = new Dictionary<string, int>();
        var used = new HashSet<int>();
        // Reserve saved locations first so new applications cannot displace existing shortcuts.
        foreach (var id in ids)
            if (saved.TryGetValue(id, out var cell) && cell >= 0 && cell % Stride < columns && cell / Stride < rows && used.Add(cell))
                result[id] = cell;
        foreach (var id in ids)
        {
            if (result.ContainsKey(id) || saved.GetValueOrDefault(id) == -1) continue;
            for (var index = 0; index < columns * rows; index++)
            {
                var cell = index / columns * Stride + index % columns;
                if (!used.Add(cell)) continue;
                result[id] = cell;
                break;
            }
        }
        return result;
    }

    public static void Move(Dictionary<string, int> saved, IReadOnlyDictionary<string, int> visible, string id, int target)
    {
        if (!visible.TryGetValue(id, out var origin)) return;
        foreach (var (other, cell) in visible)
            if (other != id && cell == target) saved[other] = origin;
        saved[id] = target;
    }
}
