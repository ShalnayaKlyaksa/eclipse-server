using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Guidebook;

namespace Content.Client.Guidebook;

/// <summary>Navigation and text search over the entries supplied to a guidebook, including restricted help books.</summary>
public sealed class GuidebookCatalog
{
    private static readonly Regex CommentPattern = new("<!--.*?-->", RegexOptions.Singleline);
    private static readonly Regex CaptionPattern = new("""<[^>]*Caption="([^"]*)"[^>]*>""");
    private static readonly Regex TextLinkPattern = new("""\[textlink="([^"]*)"[^\]]*\]""");
    private static readonly Regex MarkupPattern = new(@"<[^>]*>|\[[^\]]*\]");
    private static readonly Regex WhitespacePattern = new(@"\s+");

    public readonly Dictionary<string, GuideEntry> Entries;
    public readonly List<string> Roots;
    private readonly Dictionary<string, List<string>> _children = new();
    private readonly Dictionary<string, string> _parents = new();
    private readonly Dictionary<string, string> _titles;
    private readonly Dictionary<string, string> _text;

    public GuidebookCatalog(IEnumerable<GuideEntry> entries, Func<GuideEntry, string> title,
        Func<GuideEntry, string> text, IEnumerable<string>? roots = null, string? forcedRoot = null)
    {
        Entries = entries.ToDictionary(e => e.Id);
        _titles = Entries.Values.ToDictionary(e => e.Id, title);
        _text = Entries.Values.ToDictionary(e => e.Id, text);
        foreach (var entry in Entries.Values)
            _children[entry.Id] = entry.Children.Select(c => c.Id).Where(Entries.ContainsKey).Distinct().ToList();
        var nested = _children.Values.SelectMany(c => c).ToHashSet();
        Roots = (roots ?? Entries.Keys.Where(id => !nested.Contains(id))).Where(Entries.ContainsKey).Distinct()
            .OrderBy(id => Entries[id].Priority).ThenBy(Title, StringComparer.CurrentCulture).ToList();
        // A malformed cycle should remain browsable without recursing forever.
        if (Roots.Count == 0)
            Roots.AddRange(Entries.Keys.OrderBy(Title, StringComparer.CurrentCulture));
        if (forcedRoot != null && Entries.ContainsKey(forcedRoot))
        {
            _children[forcedRoot] = _children[forcedRoot].Concat(Roots).Where(id => id != forcedRoot).Distinct().ToList();
            Roots = new List<string> { forcedRoot };
        }
        var visited = Roots.ToHashSet();
        var queue = new Queue<string>(Roots);
        while (queue.TryDequeue(out var parent))
        {
            foreach (var child in Children(parent))
            {
                if (!visited.Add(child)) continue;
                _parents[child] = parent;
                queue.Enqueue(child);
            }
        }
    }

    public string Title(string id) => _titles.GetValueOrDefault(id, id);
    public IReadOnlyList<string> Children(string? id) => id == null ? Roots : _children.GetValueOrDefault(id) ?? [];
    public string? Parent(string id) => _parents.GetValueOrDefault(id);
    public List<string> Ancestors(string id)
    {
        var result = new List<string>();
        while (_parents.TryGetValue(id, out var parent))
        {
            result.Insert(0, parent);
            id = parent;
        }
        return result;
    }
    public string Path(string id) => string.Join(" / ", Ancestors(id).Select(Title));

    public List<string> Search(string query)
    {
        var words = Normalize(query).Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        return Entries.Keys.Select(id =>
            {
                var title = Normalize(Title(id));
                var path = Normalize(Path(id));
                var body = Normalize(_text[id]);
                var score = 0;
                foreach (var word in words)
                {
                    if (title.Contains(word, StringComparison.Ordinal)) score += 50;
                    else if (path.Contains(word, StringComparison.Ordinal)) score += 10;
                    else if (body.Contains(word, StringComparison.Ordinal)) score++;
                    else return (Id: id, Score: -1);
                }
                return (Id: id, Score: score);
            }).Where(hit => hit.Score >= 0).OrderByDescending(hit => hit.Score)
            .ThenBy(hit => Title(hit.Id), StringComparer.CurrentCulture).Select(hit => hit.Id).ToList();
    }
    public string Snippet(string id, string query, int length = 190)
    {
        var body = _text.GetValueOrDefault(id, "");
        var word = Normalize(query).Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        var offset = Normalize(body).IndexOf(word, StringComparison.Ordinal);
        var start = Math.Max(0, offset - 45);
        var count = Math.Min(length, body.Length - start);
        return (start > 0 ? "…" : "") + body.Substring(start, count) + (start + count < body.Length ? "…" : "");
    }
    public static string Normalize(string text) => text.ToLowerInvariant().Replace('ё', 'е');
    public static string PlainText(string markup)
    {
        var text = CommentPattern.Replace(markup, " ");
        text = CaptionPattern.Replace(text, " $1 ");
        text = TextLinkPattern.Replace(text, "$1");
        text = MarkupPattern.Replace(text, " ");
        return WhitespacePattern.Replace(text, " ").Trim();
    }
}
