using System.Linq;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Body;

/// <summary>
/// Class responsible for managing the appearance of an entity with <see cref="VisualBodyComponent" /> via its organs with <see cref="VisualOrganComponent" />
/// </summary>
public abstract partial class SharedVisualBodySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly MarkingManager _marking = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VisualOrganComponent, BodyRelayedEvent<OrganCopyAppearanceEvent>>(OnVisualOrganCopyAppearance);
        SubscribeLocalEvent<VisualOrganMarkingsComponent, BodyRelayedEvent<OrganCopyAppearanceEvent>>(OnMarkingsOrganCopyAppearance);
        SubscribeLocalEvent<VisualOrganComponent, BodyRelayedEvent<ApplyOrganProfileDataEvent>>(OnVisualOrganApplyProfile);
        SubscribeLocalEvent<VisualOrganMarkingsComponent, BodyRelayedEvent<ApplyOrganMarkingsEvent>>(OnMarkingsOrganApplyMarkings);

        InitializeModifiers();
        InitializeInitial();
    }

    private List<Marking> ResolveMarkings(List<Marking> markings, Color? skinColor, Color? eyeColor, Dictionary<Enum, MarkingsAppearance> appearances)
    {
        var ret = new List<Marking>();
        var forcedColors = new List<(Marking, MarkingPrototype)>();

        // This method uses two loops since some marking with constrained colors care about the colors of previous markings.
        // As such we want to ensure we can apply the markings they rely on first.
        foreach (var marking in markings)
        {
            if (!_marking.TryGetMarking(marking, out var proto))
                continue;

            if (!proto.ForcedColoring && appearances.GetValueOrDefault(proto.BodyPart)?.MatchSkin != true)
                ret.Add(marking with { Gradient = marking.Gradient?.Normalized() });
            else
                forcedColors.Add((marking, proto));
        }

        foreach (var (marking, prototype) in forcedColors)
        {
            var colors = MarkingColoring.GetMarkingLayerColors(
                prototype,
                skinColor,
                eyeColor,
                ret);

            var markingWithColor = new Marking(marking.MarkingId, colors)
            {
                Forced = marking.Forced,
            };
            if (appearances.GetValueOrDefault(prototype.BodyPart) is { MatchSkin: true } appearance && skinColor is { } color)
            {
                markingWithColor = markingWithColor.WithColor(color.WithAlpha(appearance.LayerAlpha));
            }
            ret.Add(markingWithColor);
        }

        return ret;
    }

    protected virtual void SetOrganColor(Entity<VisualOrganComponent> ent, Color color)
    {
        ent.Comp.Data.Color = color;
        Dirty(ent);
    }

    protected virtual void SetOrganAppearance(Entity<VisualOrganComponent> ent, PrototypeLayerData data)
    {
        ent.Comp.Data = data;
        Dirty(ent);
    }

    protected virtual void SetOrganMarkings(Entity<VisualOrganMarkingsComponent> ent, Dictionary<HumanoidVisualLayers, List<Marking>> markings)
    {
        ent.Comp.Markings = markings;
        Dirty(ent);
    }

    private void OnVisualOrganCopyAppearance(Entity<VisualOrganComponent> ent, ref BodyRelayedEvent<OrganCopyAppearanceEvent> args)
    {
        if (!TryComp<VisualOrganComponent>(args.Args.Organ, out var other))
            return;

        if (!other.Layer.Equals(ent.Comp.Layer))
            return;

        SetOrganAppearance(ent, other.Data);
    }

    private void OnMarkingsOrganCopyAppearance(Entity<VisualOrganMarkingsComponent> ent, ref BodyRelayedEvent<OrganCopyAppearanceEvent> args)
    {
        if (!TryComp<VisualOrganMarkingsComponent>(args.Args.Organ, out var other))
            return;

        if (!other.MarkingData.Layers.SetEquals(ent.Comp.MarkingData.Layers))
            return;

        SetOrganMarkings(ent, other.Markings);
    }

    private void OnVisualOrganApplyProfile(Entity<VisualOrganComponent> ent, ref BodyRelayedEvent<ApplyOrganProfileDataEvent> args)
    {
        if (Comp<OrganComponent>(ent).Category is not { } category)
            return;

        var relevantData = args.Args.Base;
        if (args.Args.Profiles?.TryGetValue(category, out var profile) == true)
            relevantData = profile;

        if (relevantData is not { } data)
            return;

        ent.Comp.Profile = data;

        if (ent.Comp.Layer.Equals(HumanoidVisualLayers.Eyes))
            SetOrganColor(ent, ent.Comp.Profile.EyeColor);
        else
            SetOrganColor(ent, ent.Comp.Profile.SkinColor);

        if (ent.Comp.VulpkaninBodyVariant && ApplyVulpkaninBodyType(ent, data))
            return;

        if (ent.Comp.SexStateOverrides is { } overrides && overrides.TryGetValue(data.Sex.PresentationSex(), out var state))
        {
            ent.Comp.Data.State = state;
            SetOrganAppearance(ent, ent.Comp.Data);
        }
    }

    private bool ApplyVulpkaninBodyType(Entity<VisualOrganComponent> organ, OrganProfileData data)
    {
        var layer = organ.Comp.Layer;
        var part = layer switch
        {
            HumanoidVisualLayers.Head => data.Sex.PresentationSex() == Sex.Female ? "head_f" : "head_m",
            HumanoidVisualLayers.LLeg => "l_leg",
            HumanoidVisualLayers.RLeg => "r_leg",
            HumanoidVisualLayers.LFoot => "l_foot",
            HumanoidVisualLayers.RFoot => "r_foot",
            _ => null,
        };
        if (part == null)
            return false;

        organ.Comp.Data.RsiPath = "_Eclipse/Mobs/Species/VulpkaninLustCompat/parts.rsi";
        organ.Comp.Data.State = part;
        if (data.BodyType != VulpkaninBodyType.Default)
        {
            var curved = data.BodyType is VulpkaninBodyType.CurvedShortMuzzle or VulpkaninBodyType.CurvedLongMuzzle;
            var longMuzzle = data.BodyType is VulpkaninBodyType.CurvedLongMuzzle or VulpkaninBodyType.StraightLongMuzzle;
            organ.Comp.Data.RsiPath = "_Sunrise/Mobs/Species/Vulpkanin/parts.rsi";
            organ.Comp.Data.State = layer.Equals(HumanoidVisualLayers.Head)
                ? part + (longMuzzle ? "_big_muzzle" : "_small_muzzle")
                : part + (curved ? "_curved" : "_straight");
        }
        SetOrganAppearance(organ, organ.Comp.Data);
        return true;
    }

    private void OnMarkingsOrganApplyMarkings(Entity<VisualOrganMarkingsComponent> ent, ref BodyRelayedEvent<ApplyOrganMarkingsEvent> args)
    {
        if (Comp<OrganComponent>(ent).Category is not { } category)
            return;

        if (!args.Args.Markings.TryGetValue(category, out var markingSet))
            return;

        var groupProto = _prototype.Index(ent.Comp.MarkingData.Group);
        var organMarkings = ent.Comp.Markings.ShallowClone();

        foreach (var layer in ent.Comp.MarkingData.Layers)
        {
            if (!markingSet.TryGetValue(layer, out var markings))
                continue;

            var okSet = new List<Marking>();

            foreach (var marking in markings)
            {
                if (!_marking.TryGetMarking(marking, out _))
                    continue;

                okSet.Add(marking);
            }

            organMarkings[layer] = okSet;
        }

        var visual = Comp<VisualOrganComponent>(ent);
        var profile = visual.Profile;
        if (visual.VulpkaninBodyVariant && profile.BodyType != VulpkaninBodyType.Default)
            organMarkings.Remove(HumanoidVisualLayers.Snout);
        var resolved = organMarkings.ToDictionary(
            kvp => kvp.Key,
            kvp => ResolveMarkings(kvp.Value, profile.SkinColor, profile.EyeColor, groupProto.Appearances));

        SetOrganMarkings(ent, resolved);
    }
}

/// <summary>
/// Raised on body entity, when an organ is having its appearance copied to it
/// </summary>
[ByRefEvent]
public readonly record struct OrganCopyAppearanceEvent(EntityUid Organ);

/// <summary>
/// Raised on body entity when profiles are being applied to it
/// </summary>
[ByRefEvent]
public readonly record struct ApplyOrganProfileDataEvent(OrganProfileData? Base, Dictionary<ProtoId<OrganCategoryPrototype>, OrganProfileData>? Profiles);

/// <summary>
/// Raised on body entity when a profile is being applied to it
/// </summary>
[ByRefEvent]
public readonly record struct ApplyOrganMarkingsEvent(Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<HumanoidVisualLayers, List<Marking>>> Markings);

