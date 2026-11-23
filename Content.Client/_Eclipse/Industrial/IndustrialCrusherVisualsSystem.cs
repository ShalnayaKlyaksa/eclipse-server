using Content.Shared._Eclipse.Industrial;
using Robust.Client.GameObjects;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialCrusherVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<IndustrialCrusherVisualsComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<IndustrialCrusherVisualsComponent, AppearanceChangeEvent>(OnAppearance,
            after: new[] { typeof(GenericVisualizerSystem) });
    }

    private void OnInit(Entity<IndustrialCrusherVisualsComponent> ent, ref ComponentInit args) => Update(ent);
    private void OnAppearance(Entity<IndustrialCrusherVisualsComponent> ent, ref AppearanceChangeEvent args) => Update(ent);

    private void Update(Entity<IndustrialCrusherVisualsComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite) ||
            !_sprites.LayerMapTryGet((ent, sprite), IndustrialProcessorVisualLayers.Base, out var index, false))
            return;
        var powered = _appearance.TryGetData<bool>(ent, IndustrialProcessorVisuals.Powered, out var power) && power;
        var working = powered && _appearance.TryGetData<IndustrialProcessorState>(ent, IndustrialProcessorVisuals.State, out var state) &&
                      state == IndustrialProcessorState.Working;
        _sprites.LayerSetRsiState((ent, sprite), index, working ? "power_on" : "paused");
        _sprites.LayerSetAutoAnimated((ent, sprite), index, working);
        if (working || !_sprites.TryGetLayer((ent, sprite), index, out var layer, false) ||
            layer.ActualRsi is not { } rsi || !rsi.TryGetState("paused", out var paused))
            return;
        // Choose the middle of the second frame; exact delay boundaries still belong to the first frame.
        var time = powered || paused.DelayCount < 2 ? 0 : paused.GetDelay(0) + paused.GetDelay(1) / 2;
        _sprites.LayerSetAnimationTime((ent, sprite), index, time);
    }
}
