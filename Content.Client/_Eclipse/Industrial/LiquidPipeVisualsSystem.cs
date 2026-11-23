using Content.Shared._Eclipse.Industrial;
using Content.Shared.Chemistry;
using Robust.Client.GameObjects;

namespace Content.Client._Eclipse.Industrial;

/// <summary>Native connection masks with a flat square in the actual solution's colour.</summary>
public sealed class LiquidPipeVisualsSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<LiquidPipeComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LiquidPipeComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<LiquidPipeComponent, AppearanceChangeEvent>(OnAppearance);
    }

    private void OnInit(Entity<LiquidPipeComponent> ent, ref ComponentInit args) => Update(ent);
    private void OnState(Entity<LiquidPipeComponent> ent, ref AfterAutoHandleStateEvent args) => Update(ent);
    private void OnAppearance(Entity<LiquidPipeComponent> ent, ref AppearanceChangeEvent args) => Update(ent);

    private void Update(Entity<LiquidPipeComponent> ent)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;
        EntityManager.System<IndustrialPipeConnectorSystem>().Update((ent.Owner, sprite),
            ent.Comp.MachineInputDirections, ent.Comp.MachineOutputDirections, "_Eclipse/Structures/Piping/liquid_pipe.rsi");
        if (_sprite.LayerMapTryGet((ent, sprite), "pipe", out var body, false))
            _sprite.LayerSetRsiState((ent, sprite), body, "pipe" + ((int) ent.Comp.ConnectedDirections & 15));
        if (!_sprite.LayerMapTryGet((ent, sprite), "content", out var marker, false))
            return;
        var color = Color.FromHex("#2B3139");
        if (_appearance.TryGetData<float>(ent, SolutionContainerVisuals.FillFraction, out var fraction) && fraction > 0 &&
            _appearance.TryGetData<Color>(ent, SolutionContainerVisuals.Color, out var filled))
            color = filled.WithAlpha(1f);
        _sprite.LayerSetColor((ent, sprite), marker, color);
    }
}
