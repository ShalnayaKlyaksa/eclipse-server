using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Tag;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>
/// Client-side helper that relays treatment results from minigames to the server.
/// </summary>
public sealed class AdvancedHealthClientSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    public void CompleteTreatment(EntityUid target, BodyPartSlot slot, AdvancedTreatmentType treatment,
        float quality, EntityUid? tool, int segments = 0)
    {
        RaiseNetworkEvent(new AdvancedHealthTreatmentCompleteEvent(
            GetNetEntity(target),
            slot,
            treatment,
            quality,
            tool is { } t ? GetNetEntity(t) : null,
            segments));
    }

    public void SetAimTarget(BodyPartTarget target)
    {
        RaiseNetworkEvent(new AdvancedHealthSetAimTargetEvent(target));
    }

    /// <summary>Finds a held blood/fluid pack, if any.</summary>
    public bool TryGetHeldBloodProduct(EntityUid user, out EntityUid pack)
    {
        pack = default;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<BloodProductComponent>(held))
            {
                pack = held;
                return true;
            }
        }

        return false;
    }

    public void Transfuse(EntityUid target, EntityUid pack)
    {
        RaiseNetworkEvent(new AdvancedHealthTransfusionEvent(GetNetEntity(target), GetNetEntity(pack)));
    }

    /// <summary>Finds a recognised treatment tool currently held and the treatment it provides.</summary>
    public bool TryGetHeldTreatment(EntityUid user, out AdvancedTreatmentType treatment, out EntityUid tool)
    {
        treatment = default;
        tool = default;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (TryComp<AdvancedTreatmentComponent>(held, out var advanced))
            {
                treatment = advanced.Treatment;
                tool = held;
                return true;
            }

            foreach (var candidate in Enum.GetValues<AdvancedTreatmentType>())
            {
                if (candidate == AdvancedTreatmentType.ForeignBodyRemoval)
                    continue; // bare-hand removal is not a held tool

                if (MatchesLocalTool(held, candidate))
                {
                    treatment = candidate;
                    tool = held;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Returns a held treatment tool, or null when bare hands are allowed.</summary>
    public bool TryFindLocalTool(EntityUid user, AdvancedTreatmentType treatment, out EntityUid? tool)
    {
        tool = null;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (MatchesLocalTool(held, treatment))
            {
                tool = held;
                return true;
            }
        }

        return treatment == AdvancedTreatmentType.ForeignBodyRemoval;
    }

    private bool MatchesLocalTool(EntityUid item, AdvancedTreatmentType treatment)
    {
        if (TryComp<AdvancedTreatmentComponent>(item, out var advanced))
            return advanced.Treatment == treatment;

        if (!TryComp<MetaDataComponent>(item, out var meta) || meta.EntityPrototype is not { } proto)
            return false;

        var id = proto.ID;
        return treatment switch
        {
            AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage =>
                HasComp<AdvancedBandageRollComponent>(item),
            AdvancedTreatmentType.Tourniquet => _tags.HasTag(item, "Tourniquet"),
            AdvancedTreatmentType.Splint => id is "Brutepack" or "Brutepack1",
            AdvancedTreatmentType.Hemostatic => _tags.HasTag(item, "Ointment"),
            AdvancedTreatmentType.Suture => id is "MedicatedSuture" or "MedicatedSuture1" or "BrutepackAdvanced1",
            AdvancedTreatmentType.ForeignBodyRemoval => id is "AdvancedForcepsPack",
            _ => false,
        };
    }
}
