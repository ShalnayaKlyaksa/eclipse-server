using System.Numerics;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.CCVar;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialVibrationSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;

    private readonly Dictionary<EntityUid, (Vector2 Base, Vector2 Applied)> _offsets = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<IndustrialProcessorComponent, ComponentShutdown>(OnProcessorShutdown);
    }

    public override void Shutdown()
    {
        foreach (var (uid, offsets) in _offsets)
            Restore(uid, offsets);
        _offsets.Clear();
    }

    public override void FrameUpdate(float frameTime)
    {
        var enabled = !_config.GetCVar(CCVars.ReducedMotion);
        var time = (float) (_timing.CurTime.TotalSeconds % 60);
        var query = EntityQueryEnumerator<IndustrialProcessorComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var processor, out var sprite))
        {
            if (!enabled || !sprite.Visible
                || processor.ProcessorType is not (IndustrialProcessorType.Crusher or IndustrialProcessorType.ThermalCentrifuge
                    or IndustrialProcessorType.OreWasher or IndustrialProcessorType.Mixer or IndustrialProcessorType.Grinder)
                || !_appearance.TryGetData<IndustrialProcessorState>(uid, IndustrialProcessorVisuals.State, out var state)
                || state != IndustrialProcessorState.Working)
            {
                if (_offsets.Remove(uid, out var stopped))
                    Restore(uid, stopped);
                continue;
            }

            var basis = sprite.Offset;
            if (_offsets.TryGetValue(uid, out var previous) && basis == previous.Applied)
                basis = previous.Base;

            var phase = time * (MathF.Tau * 8f) + uid.GetHashCode() % 17;
            var offset = basis + new Vector2(MathF.Sin(phase) / 32f, 0f);
            _sprites.SetOffset((uid, sprite), offset);
            _offsets[uid] = (basis, offset);
        }
    }

    private void OnProcessorShutdown(Entity<IndustrialProcessorComponent> ent, ref ComponentShutdown args)
    {
        if (_offsets.Remove(ent.Owner, out var offsets))
            Restore(ent.Owner, offsets);
    }

    private void Restore(EntityUid uid, (Vector2 Base, Vector2 Applied) offsets)
    {
        if (TryComp<SpriteComponent>(uid, out var sprite) && sprite.Offset == offsets.Applied)
            _sprites.SetOffset((uid, sprite), offsets.Base);
    }
}
