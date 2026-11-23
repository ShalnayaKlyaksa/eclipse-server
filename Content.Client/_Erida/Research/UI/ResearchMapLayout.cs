using System.Linq;
using System.Numerics;
using Content.Shared.Research.Prototypes;

namespace Content.Client._Erida.Research.UI;

/// <summary>Local prerequisite layers and separate discipline lanes, independent of legacy map coordinates.</summary>
public static class ResearchMapLayout
{
    public static readonly Vector2 Spacing = new(256, 156);

    public static string Group(TechnologyPrototype technology)
        => technology.ID.StartsWith("Industrial", StringComparison.Ordinal) ? "factory" : technology.Discipline.Id;

    public static Dictionary<string, Vector2> Arrange(IEnumerable<TechnologyPrototype> technologies)
    {
        var result = new Dictionary<string, Vector2>();
        var top = 0f;
        foreach (var group in technologies.GroupBy(Group).OrderBy(g => g.Key == "factory" ? "" : g.Key, StringComparer.Ordinal))
        {
            var nodes = group.ToDictionary(t => t.ID);
            var depths = new Dictionary<string, int>();
            var visiting = new HashSet<string>();
            int Depth(string id)
            {
                if (depths.TryGetValue(id, out var depth)) return depth;
                if (!visiting.Add(id)) return 0;
                depth = nodes[id].TechnologyPrerequisites.Where(p => nodes.ContainsKey(p.Id))
                    .Select(p => Depth(p.Id) + 1).DefaultIfEmpty(0).Max();
                visiting.Remove(id);
                return depths[id] = depth;
            }
            foreach (var id in nodes.Keys) Depth(id);
            var height = 1;
            foreach (var column in nodes.Values.GroupBy(t => depths[t.ID]).OrderBy(g => g.Key))
            {
                // Keep siblings near their parents; stable ties prevent cards moving on state updates.
                var row = 0;
                foreach (var node in column.OrderBy(t => t.TechnologyPrerequisites
                             .Where(p => result.ContainsKey(p.Id)).Select(p => result[p.Id].Y)
                             .DefaultIfEmpty(top).Average()).ThenBy(t => t.Position.Y).ThenBy(t => t.ID, StringComparer.Ordinal))
                    result[node.ID] = new Vector2(column.Key, top + row++) * Spacing;
                height = Math.Max(height, row);
            }
            top += height + 1;
        }
        return result;
    }

    public static HashSet<string> PrerequisitePath(string target, IReadOnlyDictionary<string, TechnologyPrototype> technologies)
    {
        var path = new HashSet<string>();
        void Visit(string id)
        {
            if (!technologies.TryGetValue(id, out var technology) || !path.Add(id)) return;
            foreach (var prerequisite in technology.TechnologyPrerequisites) Visit(prerequisite.Id);
        }
        Visit(target);
        return path;
    }
}
