using System.Linq;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared._Eclipse.Industrial;

/// <summary>
/// Shared identity and accounting for industrial ingredients. Stack variants are the same resource;
/// non-stack ingredients retain their exact prototype identity.
/// </summary>
public sealed partial class IndustrialResourceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly Dictionary<string, string> _keys = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(_ => _keys.Clear());
    }

    public string Key(string prototype)
    {
        if (_keys.TryGetValue(prototype, out var key))
            return key;

        key = _prototypes.TryIndex<EntityPrototype>(prototype, out var proto) &&
              proto.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory)
            ? $"stack:{stack.StackTypeId}"
            : $"item:{prototype}";
        _keys[prototype] = key;
        return key;
    }

    public bool Matches(EntityUid item, string prototype)
    {
        var actual = MetaData(item).EntityPrototype?.ID;
        return actual != null && Key(actual) == Key(prototype);
    }

    public int Count(EntityUid item) => TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;

    public int Count(IEnumerable<EntityUid> items, string prototype)
    {
        var count = 0;
        foreach (var item in items)
        {
            if (!TerminatingOrDeleted(item) && Matches(item, prototype))
                count += Count(item);
        }
        return count;
    }

    public bool CanAfford(IEnumerable<EntityUid> items, IReadOnlyDictionary<string, int> ingredients, int amount = 1)
        => TryPlan(items, ingredients, out _, amount);

    /// <summary>Plan the entire consumption before changing anything. Aliased ingredients cannot double-count a stack.</summary>
    public bool TryPlan(IEnumerable<EntityUid> items, IReadOnlyDictionary<string, int> ingredients,
        out List<(EntityUid Item, int Count)> plan, int amount = 1)
    {
        plan = new();
        if (amount <= 0)
            return false;

        var remaining = new Dictionary<string, int>();
        foreach (var (prototype, count) in ingredients)
        {
            if (count <= 0 || count > int.MaxValue / amount)
                return false;
            var key = Key(prototype);
            var required = (long) remaining.GetValueOrDefault(key) + count * amount;
            if (required > int.MaxValue)
                return false;
            remaining[key] = (int) required;
        }

        foreach (var item in items.Distinct())
        {
            if (TerminatingOrDeleted(item) || MetaData(item).EntityPrototype is not { } proto ||
                !remaining.TryGetValue(Key(proto.ID), out var needed))
                continue;

            var take = Math.Min(needed, Count(item));
            if (take <= 0)
                continue;
            plan.Add((item, take));
            if (take == needed)
                remaining.Remove(Key(proto.ID));
            else
                remaining[Key(proto.ID)] = needed - take;
        }

        return remaining.Count == 0;
    }

    public bool TryGetStack(string prototype, out StackComponent stack)
    {
        if (_prototypes.TryIndex<EntityPrototype>(prototype, out var proto) &&
            proto.TryGetComponent<StackComponent>(out var found, EntityManager.ComponentFactory) && found != null)
        {
            stack = found;
            return true;
        }
        stack = default!;
        return false;
    }
}
