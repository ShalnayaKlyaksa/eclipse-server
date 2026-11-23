using Robust.Shared.Map;
using System.Linq;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared._Eclipse.Industrial;
using Content.Shared._Eclipse.Industrial.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Map.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialProcessorSystem
{
    /// <summary>Moves a unit using native stack splitting/merging, preserving non-stack entity state.</summary>
    public bool TryPipeTransfer(Entity<IndustrialProcessorComponent> source,
        Entity<IndustrialProcessorComponent> sink, string protoId)
    {
        if (!CanAcceptInputItem(sink, protoId) ||
            !_container.TryGetContainer(source, source.Comp.OutputContainerId, out var output) ||
            !_container.TryGetContainer(sink, sink.Comp.InputContainerId, out var input))
            return false;
        foreach (var item in output.ContainedEntities)
        {
            if (Resources.Matches(item, protoId) && Resources.Count(item) > 0)
                return TransferItem(source, sink, item, input);
        }
        return false;
    }

    public bool TryPipeTransfer(Entity<IndustrialProcessorComponent> source, Entity<IndustrialProcessorComponent> sink)
    {
        if (!_container.TryGetContainer(source, source.Comp.OutputContainerId, out var output))
            return false;
        foreach (var item in output.ContainedEntities)
        {
            if (Prototype(item) is { } proto && Resources.Count(item) > 0 &&
                CanAcceptInputItem(sink, proto.ID) &&
                _container.TryGetContainer(sink, sink.Comp.InputContainerId, out var input) &&
                TransferItem(source, sink, item, input))
                return true;
        }
        return false;
    }

    private bool TransferItem(Entity<IndustrialProcessorComponent> source,
        Entity<IndustrialProcessorComponent> sink, EntityUid item, BaseContainer input)
    {
        if (source.Comp.InTransaction || sink.Comp.InTransaction)
            return false;
        source.Comp.InTransaction = sink.Comp.InTransaction = true;
        EntityUid? split = null;
        try
        {
            if (TryComp<StackComponent>(item, out var stack))
            {
                foreach (var existing in input.ContainedEntities)
                {
                    if (!TryComp<StackComponent>(existing, out var recipient))
                        continue;
                    if (!_stack.TryMergeStacks((item, stack), (existing, recipient), out _, 1))
                        continue;
                    if (stack.Count == 0)
                        Del(item);
                    return true;
                }
                if (input.Count >= sink.Comp.MaxInputSlots)
                    return false;
                if (stack.Count > 1)
                {
                    split = _stack.Split((item, stack), 1, Transform(source).Coordinates);
                    if (split == null)
                        return false;
                }
            }
            return _container.Insert(split ?? item, input);
        }
        finally
        {
            // If an insertion vetoed a split stack, merge the unit back instead of deleting it.
            if (split is { } unit && !input.Contains(unit) && TryComp<StackComponent>(unit, out var donor) &&
                TryComp<StackComponent>(item, out var original))
                _stack.TryMergeStacks((unit, donor), (item, original), out _);
            source.Comp.InTransaction = sink.Comp.InTransaction = false;
            source.Comp.WaitingForOutput = false;
            if (source.Comp.AutoStart)
                TryStartProcessing(source);
            if (sink.Comp.AutoStart)
                TryStartProcessing(sink);
            UpdateAppearance(source);
            UpdateAppearance(sink);
            UpdateUserInterface(source);
            UpdateUserInterface(sink);
        }
    }
}
