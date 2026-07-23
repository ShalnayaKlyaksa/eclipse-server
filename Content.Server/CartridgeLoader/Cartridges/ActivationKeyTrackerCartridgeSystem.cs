using System.Numerics;
using Content.Server._Eclipse.ProtoCore.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.Station;
using Robust.Shared.Timing;

namespace Content.Server.CartridgeLoader.Cartridges;

/// <summary>
/// Publishes a single synchronized activation-key position snapshot to every tracker app.
/// </summary>
public sealed class ActivationKeyTrackerCartridgeSystem : EntitySystem
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);

    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedStationSystem _station = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private TimeSpan _nextUpdate;
    private EntityUid? _targetGrid;
    private Vector2? _keyPosition;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActivationKeyTrackerCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        _nextUpdate = TimeSpan.Zero;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextUpdate)
            return;

        _nextUpdate = _timing.CurTime + UpdateInterval;
        RefreshSnapshot();

        var query = EntityQueryEnumerator<ActivationKeyTrackerCartridgeComponent, CartridgeComponent>();
        while (query.MoveNext(out var uid, out _, out var cartridge))
        {
            if (cartridge.LoaderUid is not { } loader ||
                !TryComp(loader, out CartridgeLoaderComponent? loaderComp) ||
                loaderComp.ActiveProgram != uid)
            {
                continue;
            }

            SendSnapshot(loader);
        }
    }

    private void OnUiReady(
        Entity<ActivationKeyTrackerCartridgeComponent> ent,
        ref CartridgeUiReadyEvent args)
    {
        SendSnapshot(args.Loader);
    }

    private void RefreshSnapshot()
    {
        _targetGrid = FindTargetGrid();
        _keyPosition = null;

        if (_targetGrid is not { } grid)
            return;

        var gridXform = Transform(grid);
        var keyQuery = EntityQueryEnumerator<ProtoCoreActivationKeyComponent, TransformComponent>();
        while (keyQuery.MoveNext(out _, out var key, out var keyXform))
        {
            if (key.ZeroShift || keyXform.MapID != gridXform.MapID)
                continue;

            var mapPosition = _transform.ToMapCoordinates(keyXform.Coordinates).Position;
            _keyPosition = Vector2.Transform(mapPosition, _transform.GetInvWorldMatrix(gridXform));
            return;
        }
    }

    private EntityUid? FindTargetGrid()
    {
        foreach (var rule in _gameTicker.GetActiveGameRules<NukeopsRuleComponent>())
        {
            if (rule.Comp.TargetStation is { } targetStation)
                return _station.GetLargestGrid((targetStation, null));
        }

        return null;
    }

    private void SendSnapshot(EntityUid loader)
    {
        var state = new ActivationKeyTrackerUiState(
            _targetGrid is { } grid ? GetNetEntity(grid) : null,
            _keyPosition);

        _cartridgeLoader.UpdateCartridgeUiState(loader, state);
    }
}
