using Content.Shared._Eclipse.Industrial;
using Content.Shared.Atmos;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.Industrial;

[UsedImplicitly]
public sealed partial class ItemPipeAppearanceSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ItemPipeComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ItemPipeComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
        SubscribeLocalEvent<ItemPipeComponent, AppearanceChangeEvent>(OnAppearanceChanged);
    }

    private void OnInit(Entity<ItemPipeComponent> ent, ref ComponentInit args)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        if (HasComp<ItemPipeWireConnectComponent>(ent))
        {
            UpdateDirectionalSpriteState(ent, sprite);
            return;
        }

        if (!TryComp(ent, out ItemPipeAppearanceComponent? appearance))
            return;

        SetupConnectionLayers(ent, sprite, appearance);
        UpdateLayeredSpriteState(ent, sprite);
    }

    private void OnAfterAutoHandleState(Entity<ItemPipeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp(ent, out SpriteComponent? sprite))
            return;

        if (HasComp<ItemPipeWireConnectComponent>(ent))
            UpdateDirectionalSpriteState(ent, sprite);
        else
            UpdateLayeredSpriteState(ent, sprite);
    }

    private void OnAppearanceChanged(Entity<ItemPipeComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;

        if (HasComp<ItemPipeWireConnectComponent>(ent))
            UpdateDirectionalSpriteState(ent, args.Sprite);
        else
            UpdateLayeredSpriteState(ent, args.Sprite);
    }

    /// <summary>
    /// One native connection-mask frame replaces the old hub and four overlapping arms.
    /// </summary>
    private void UpdateDirectionalSpriteState(EntityUid uid, SpriteComponent sprite)
    {
        if (!_sprite.LayerMapTryGet((uid, sprite), ItemPipeVisualLayers.Hub, out var key, false))
            return;
        var prefix = Comp<ItemPipeWireVisualizerComponent>(uid).StatePrefix;
        _sprite.LayerSetRsiState((uid, sprite), key, prefix + ((int) ResolveConnectedDirections(uid) & 15));
        var pipe = Comp<ItemPipeComponent>(uid);
        EntityManager.System<IndustrialPipeConnectorSystem>().Update((uid, sprite),
            pipe.MachineInputDirections, pipe.MachineOutputDirections, "_Eclipse/Structures/Piping/item_lp_pipe.rsi");
    }

    private PipeDirection ResolveConnectedDirections(EntityUid uid)
    {
        if (_appearance.TryGetData<int>(uid, ItemPipeVisuals.ConnectedDirections, out var connected))
            return (PipeDirection) connected;

        if (TryComp<ItemPipeComponent>(uid, out var pipe))
            return pipe.ConnectedDirections;

        return PipeDirection.None;
    }

    private void SetupConnectionLayers(EntityUid uid, SpriteComponent sprite, ItemPipeAppearanceComponent appearance)
    {
        var layer = ItemPipeConnectionHelper.GetLayer(uid, EntityManager);
        var rsiPath = new ResPath(appearance.SpriteRsiPaths.GetValueOrDefault(layer, "Structures/Piping/Atmospherics/pipe.rsi"));

        foreach (var (connectionName, pipeDir) in ConnectionLayerMap())
        {
            var key = _sprite.LayerMapReserve((uid, sprite), connectionName);
            _sprite.LayerSetRsi((uid, sprite), key, rsiPath);
            _sprite.LayerSetRsiState((uid, sprite), key, "pipeStraight");
            _sprite.LayerSetDirOffset((uid, sprite), key, GetDirOffset(pipeDir));
            _sprite.LayerSetVisible((uid, sprite), key, false);
        }
    }

    private void UpdateLayeredSpriteState(EntityUid uid, SpriteComponent sprite)
    {
        var connected = ResolveConnectedDirections(uid);
        if (connected == PipeDirection.None)
        {
            HideConnections((uid, sprite));
            return;
        }

        var connectedDirs = connected.RotatePipeDirection(-Transform(uid).LocalRotation);

        foreach (var (connectionName, pipeDir) in ConnectionLayerMap())
        {
            if (!_sprite.LayerMapTryGet((uid, sprite), connectionName, out var key, false))
                continue;

            var visible = connectedDirs.HasDirection(pipeDir);
            _sprite.LayerSetVisible((uid, sprite), key, visible);
        }
    }

    private void HideConnections(Entity<SpriteComponent?> ent)
    {
        foreach (var (connectionName, _) in ConnectionLayerMap())
        {
            if (_sprite.LayerMapTryGet(ent, connectionName, out var key, false))
                _sprite.LayerSetVisible(ent, key, false);
        }
    }

    private static SpriteComponent.DirectionOffset GetDirOffset(PipeDirection direction)
    {
        return direction switch
        {
            PipeDirection.North => SpriteComponent.DirectionOffset.Flip,
            PipeDirection.East => SpriteComponent.DirectionOffset.CounterClockwise,
            PipeDirection.West => SpriteComponent.DirectionOffset.Clockwise,
            _ => SpriteComponent.DirectionOffset.None,
        };
    }

    private static IEnumerable<(string Name, PipeDirection Direction)> ConnectionLayerMap()
    {
        yield return ("NorthConnection", PipeDirection.North);
        yield return ("SouthConnection", PipeDirection.South);
        yield return ("EastConnection", PipeDirection.East);
        yield return ("WestConnection", PipeDirection.West);
    }
}
