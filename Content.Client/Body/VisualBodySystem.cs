using System.Linq;
using Content.Client.DisplacementMap;
using Content.Client.Humanoid;
using Content.Shared.Body;
using Content.Shared.CCVar;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Body;

public sealed class VisualBodySystem : SharedVisualBodySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly DisplacementMapSystem _displacement = default!;
    [Dependency] private readonly MarkingManager _marking = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IResourceCache _resourceCache = default!;

    private MarkingGradientBaker _gradientBaker = default!;

    public void PersistMarkingGradients(HumanoidCharacterAppearance appearance)
    {
        foreach (var layers in appearance.Markings.Values)
        foreach (var markings in layers.Values)
        foreach (var marking in markings)
        {
            if (marking.Gradient is not { } gradient || !_marking.TryGetMarking(marking, out var prototype))
                continue;

            foreach (var (specifier, i, _) in MarkingSprites(prototype))
            {
                if (i < marking.MarkingColors.Count && specifier is SpriteSpecifier.Rsi sprite &&
                    _resourceCache.TryGetResource<RSIResource>(SpriteSystem.TextureRoot / sprite.RsiPath, out var resource) &&
                    resource.RSI.TryGetState(sprite.RsiState, out var state))
                    _gradientBaker.GetBake(state, marking.MarkingColors[i], gradient, persist: true);
            }
        }
    }

    public override void Initialize()
    {
        base.Initialize();
        _gradientBaker = new MarkingGradientBaker(_resources);

        SubscribeLocalEvent<VisualOrganComponent, OrganGotInsertedEvent>(OnOrganGotInserted);
        SubscribeLocalEvent<VisualOrganComponent, OrganGotRemovedEvent>(OnOrganGotRemoved);
        SubscribeLocalEvent<VisualOrganComponent, AfterAutoHandleStateEvent>(OnOrganState);

        SubscribeLocalEvent<VisualOrganMarkingsComponent, OrganGotInsertedEvent>(OnMarkingsGotInserted);
        SubscribeLocalEvent<VisualOrganMarkingsComponent, OrganGotRemovedEvent>(OnMarkingsGotRemoved);
        SubscribeLocalEvent<VisualOrganMarkingsComponent, AfterAutoHandleStateEvent>(OnMarkingsState);

        SubscribeLocalEvent<VisualOrganMarkingsComponent, BodyRelayedEvent<HumanoidLayerVisibilityChangedEvent>>(OnMarkingsChangedVisibility);

        Subs.CVar(_cfg, CCVars.AccessibilityClientCensorNudity, OnCensorshipChanged, true);
        Subs.CVar(_cfg, CCVars.AccessibilityServerCensorNudity, OnCensorshipChanged, true);
    }

    private void OnCensorshipChanged(bool value)
    {
        var query = AllEntityQuery<OrganComponent, VisualOrganMarkingsComponent>();
        while (query.MoveNext(out var ent, out var organComp, out var markingsComp))
        {
            if (organComp.Body is not { } body)
                continue;

            RemoveMarkings((ent, markingsComp), body);
            ApplyMarkings((ent, markingsComp), body);
        }
    }

    private void OnOrganGotInserted(Entity<VisualOrganComponent> ent, ref OrganGotInsertedEvent args)
    {
        ApplyVisual(ent, args.Target);
    }

    private void OnOrganGotRemoved(Entity<VisualOrganComponent> ent, ref OrganGotRemovedEvent args)
    {
        RemoveVisual(ent, args.Target);
    }

    private void OnOrganState(Entity<VisualOrganComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (Comp<OrganComponent>(ent).Body is not { } body)
            return;

        ApplyVisual(ent, body);
    }

    private void ApplyVisual(Entity<VisualOrganComponent> ent, EntityUid target)
    {
        if (!_sprite.LayerMapTryGet(target, ent.Comp.Layer, out var index, true))
            return;

        _sprite.LayerSetData(target, index, ent.Comp.Data);
    }

    private void RemoveVisual(Entity<VisualOrganComponent> ent, EntityUid target)
    {
        if (!_sprite.LayerMapTryGet(target, ent.Comp.Layer, out var index, true))
            return;

        _sprite.LayerSetRsiState(target, index, RSI.StateId.Invalid);
    }

    private void OnMarkingsGotInserted(Entity<VisualOrganMarkingsComponent> ent, ref OrganGotInsertedEvent args)
    {
        ApplyMarkings(ent, args.Target);
    }

    private void OnMarkingsGotRemoved(Entity<VisualOrganMarkingsComponent> ent, ref OrganGotRemovedEvent args)
    {
        RemoveMarkings(ent, args.Target);
    }

    private void OnMarkingsState(Entity<VisualOrganMarkingsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (Comp<OrganComponent>(ent).Body is not { } body)
            return;

        RemoveMarkings(ent, body);
        ApplyMarkings(ent, body);
    }

    protected override void SetOrganColor(Entity<VisualOrganComponent> ent, Color color)
    {
        base.SetOrganColor(ent, color);

        if (Comp<OrganComponent>(ent).Body is not { } body)
            return;

        ApplyVisual(ent, body);
    }

    protected override void SetOrganMarkings(Entity<VisualOrganMarkingsComponent> ent, Dictionary<HumanoidVisualLayers, List<Marking>> markings)
    {
        base.SetOrganMarkings(ent, markings);

        if (Comp<OrganComponent>(ent).Body is not { } body)
            return;

        RemoveMarkings(ent, body);
        ApplyMarkings(ent, body);
    }

    protected override void SetOrganAppearance(Entity<VisualOrganComponent> ent, PrototypeLayerData data)
    {
        base.SetOrganAppearance(ent, data);

        if (Comp<OrganComponent>(ent).Body is not { } body)
            return;

        ApplyVisual(ent, body);
    }

    private IEnumerable<Marking> AllMarkings(Entity<VisualOrganMarkingsComponent> ent)
    {
        foreach (var markings in ent.Comp.Markings.Values)
        {
            foreach (var marking in markings)
            {
                yield return marking;
            }
        }

        var censorNudity = _cfg.GetCVar(CCVars.AccessibilityClientCensorNudity) || _cfg.GetCVar(CCVars.AccessibilityServerCensorNudity);
        if (!censorNudity)
            yield break;

        var group = _prototype.Index(ent.Comp.MarkingData.Group);
        foreach (var layer in ent.Comp.MarkingData.Layers)
        {
            if (!group.Limits.TryGetValue(layer, out var layerLimits))
                continue;

            if (layerLimits.NudityDefault.Count < 1)
                continue;

            var markings = ent.Comp.Markings.GetValueOrDefault(layer) ?? [];
            if (markings.Any(marking => _marking.TryGetMarking(marking, out var proto) && proto.BodyPart == layer))
                continue;

            foreach (var marking in layerLimits.NudityDefault)
            {
                yield return new(marking, 1);
            }
        }
    }

    private void ApplyMarkings(Entity<VisualOrganMarkingsComponent> ent, Entity<SpriteComponent?> target)
    {
        if (!Resolve(target, ref target.Comp))
            return;

        var applied = new List<Marking>();
        foreach (var marking in AllMarkings(ent))
        {
            if (!_marking.TryGetMarking(marking, out var proto))
                continue;

            if (!_sprite.LayerMapTryGet(target, proto.BodyPart, out var index, true))
                continue;

            var insertionIndex = proto.DrawBehindBody ? 0 : index + 1;
            ent.Comp.MarkingsDisplacement.TryGetValue(proto.BodyPart, out var displacement);

            foreach (var (sprite, i, behindHair) in MarkingSprites(proto))
            {
                DebugTools.Assert(sprite is SpriteSpecifier.Rsi);
                if (sprite is not SpriteSpecifier.Rsi rsi)
                    continue;

                var layerId = $"{proto.ID}-{rsi.RsiState}";

                if (!_sprite.LayerMapTryGet(target, layerId, out var spriteLayer, false))
                {
                    var layerIndex = insertionIndex + i;
                    if (behindHair && _sprite.LayerMapTryGet(target, HumanoidVisualLayers.Hair, out var hairIndex, false))
                        layerIndex = hairIndex;
                    spriteLayer = _sprite.AddLayer(target, sprite, layerIndex);
                    _sprite.LayerMapSet(target, layerId, spriteLayer);
                    _sprite.LayerSetSprite(target, layerId, rsi);
                }

                var color = marking.MarkingColors is { } colors && i < colors.Count ? colors[i] : Color.White;
                _sprite.LayerSetColor(target, layerId, color);

                if (displacement != null && proto.CanBeDisplaced)
                    _displacement.TryAddDisplacement(displacement, (target, target.Comp), spriteLayer, layerId, out _);

                if (marking.Gradient is { } gradient && _sprite.TryGetLayer(target, layerId, out var layer, false))
                    ApplyGradient(target.Comp, layer, layerId, color, gradient);
            }

            applied.Add(marking);
        }
        ent.Comp.AppliedMarkings = applied;
    }

    private void ApplyGradient(SpriteComponent sprite, SpriteComponent.Layer layer, string layerId,
        Color color, MarkingGradient gradient)
    {
        ProtoId<ShaderPrototype>? shaderId = layer.ShaderPrototype?.Id switch
        {
            null => new("MarkingGradient"),
            "DisplacedDraw" => new("MarkingGradientDisplaced"),
            "DisplacedDrawUnshaded" => new("MarkingGradientDisplacedUnshaded"),
            "DisplacedStencilDraw" => new("MarkingGradientDisplacedStencil"),
            _ => null,
        };
        if (shaderId is not { } id)
            return;

        if (layer.ActualState is not { } state || _gradientBaker.GetBake(state, color, gradient) is not { } bake)
            return;

        var shader = _prototype.Index(id).InstanceUnique();
        shader.SetParameter("bakedMarking", bake.Texture);
        shader.SetParameter("bakedMarkingTransform", bake.AtlasTransform);
        sprite.LayerSetShader(layerId, shader, id.Id);
        _sprite.LayerSetColor(layer, Color.White);
    }

    private static IEnumerable<(SpriteSpecifier Sprite, int ColorIndex, bool BehindHair)> MarkingSprites(MarkingPrototype prototype)
    {
        for (var i = 0; i < prototype.Sprites.Count; i++)
            yield return (prototype.Sprites[i], i, false);

        for (var i = 0; i < prototype.SpritesBehindHair.Count; i++)
            yield return (prototype.SpritesBehindHair[i], i, true);
    }

    private void RemoveMarkings(Entity<VisualOrganMarkingsComponent> ent, Entity<SpriteComponent?> target)
    {
        if (!Resolve(target, ref target.Comp))
            return;

        foreach (var marking in ent.Comp.AppliedMarkings)
        {
            if (!_marking.TryGetMarking(marking, out var proto))
                continue;

            foreach (var (sprite, _, _) in MarkingSprites(proto))
            {
                DebugTools.Assert(sprite is SpriteSpecifier.Rsi);
                if (sprite is not SpriteSpecifier.Rsi rsi)
                    continue;

                var layerId = $"{proto.ID}-{rsi.RsiState}";

                // If this marking is one that can be displaced, we need to remove the displacement as well; otherwise
                // altering a marking at runtime can lead to the renderer falling over.
                // The Vulps must be shaved.
                // (https://github.com/space-wizards/space-station-14/issues/40135).
                if (proto.CanBeDisplaced)
                    _displacement.EnsureDisplacementIsNotOnSprite((target, target.Comp), layerId);

                if (!_sprite.LayerMapTryGet(target, layerId, out var index, false))
                    continue;

                _sprite.LayerMapRemove(target, layerId);
                _sprite.RemoveLayer(target, index);
            }
        }
    }

    private void OnMarkingsChangedVisibility(Entity<VisualOrganMarkingsComponent> ent, ref BodyRelayedEvent<HumanoidLayerVisibilityChangedEvent> args)
    {
        if (!ent.Comp.HideableLayers.Contains(args.Args.Layer))
            return;

        foreach (var markings in ent.Comp.Markings.Values)
        {
            foreach (var marking in markings)
            {
                if (!_marking.TryGetMarking(marking, out var proto))
                    continue;

                if (proto.BodyPart != args.Args.Layer && !(ent.Comp.DependentHidingLayers.TryGetValue(args.Args.Layer, out var dependent) && dependent.Contains(proto.BodyPart)))
                    continue;

                foreach (var (sprite, _, _) in MarkingSprites(proto))
                {
                    DebugTools.Assert(sprite is SpriteSpecifier.Rsi);
                    if (sprite is not SpriteSpecifier.Rsi rsi)
                        continue;

                    var layerId = $"{proto.ID}-{rsi.RsiState}";

                    if (!_sprite.LayerMapTryGet(args.Body.Owner, layerId, out var index, true))
                        continue;

                    _sprite.LayerSetVisible(args.Body.Owner, index, args.Args.Visible);
                }
            }
        }
    }
}
