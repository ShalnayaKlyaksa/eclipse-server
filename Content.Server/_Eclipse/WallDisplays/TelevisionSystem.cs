using Robust.Shared.Containers;
using Content.Shared.Interaction.Events;
using System.Linq;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Server.SurveillanceCamera;
using Content.Shared._Eclipse.WallDisplays;
using Content.Shared.Containers;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.SurveillanceCamera;
using Content.Shared.SurveillanceCamera.Components;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.WallDisplays;

/// <summary>Explicit public cameras only. Existing surveillance subscriptions and speech relays carry the broadcast.</summary>
public sealed partial class TelevisionSystem : EntitySystem
{
    [Dependency] private readonly SurveillanceCameraSystem _cameras = default!;
    [Dependency] private readonly SurveillanceCameraMicrophoneSystem _microphones = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly HashSet<(EntityUid Camera, EntityUid Player)> _views = new();
    private readonly HashSet<(EntityUid Camera, EntityUid Television)> _speakers = new();
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        SubscribeLocalEvent<TelevisionComponent, TelevisionTuneMessage>(OnTune);
        SubscribeLocalEvent<TelevisionComponent, BeforeActivatableUIOpenEvent>((uid, comp, _) => UpdateUi(uid, comp));
        SubscribeLocalEvent<TelevisionComponent, PowerChangedEvent>(OnPower);
        SubscribeLocalEvent<TelevisionComponent, ComponentShutdown>((uid, _, _) => RemoveTelevision(uid));
        SubscribeLocalEvent<TelevisionComponent, GetVerbsEvent<Verb>>(OnWatchVerb);
        SubscribeLocalEvent<TelevisionComponent, BoundUserInterfaceCheckRangeEvent>(OnCheckRange);
        SubscribeLocalEvent<PublicBroadcastCameraComponent, UseInHandEvent>(OnUseCamera);
        SubscribeLocalEvent<PublicBroadcastCameraComponent, ComponentShutdown>((uid, _, _) => RemoveCamera(uid));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _views.Clear(); _speakers.Clear(); });
    }

    private bool CanWatch(EntityUid television, EntityUid viewer) =>
        TryComp<TelevisionComponent>(television, out var receiver) && receiver.Powered &&
        _interaction.InRangeUnobstructed(viewer, television, range: TelevisionComponent.ViewingRange);

    public bool TryWatch(EntityUid television, EntityUid viewer)
    {
        if (!CanWatch(television, viewer)) return false;
        UpdateUi(television, Comp<TelevisionComponent>(television));
        return _ui.TryOpenUi(television, TelevisionUiKey.Key, viewer);
    }

    private void OnWatchVerb(EntityUid uid, TelevisionComponent television, GetVerbsEvent<Verb> args)
    {
        if (!args.CanInteract || !CanWatch(uid, args.User)) return;
        args.Verbs.Add(new Verb { Text = Loc.GetString("television-watch"), Act = () => TryWatch(uid, args.User) });
    }

    private void OnCheckRange(EntityUid uid, TelevisionComponent television, ref BoundUserInterfaceCheckRangeEvent args)
    {
        if (TelevisionUiKey.Key.Equals(args.UiKey))
            args.Result = CanWatch(uid, args.Actor) ? BoundUserInterfaceRangeResult.Pass : BoundUserInterfaceRangeResult.Fail;
    }

    private void OnUseCamera(EntityUid uid, PublicBroadcastCameraComponent camera, ref UseInHandEvent args)
    {
        if (args.Handled) return;
        SetOnAir(uid, camera, !camera.OnAir, args.User);
        _popup.PopupEntity(Loc.GetString(camera.OnAir ? "television-camera-on" : "television-camera-off"), uid, args.User);
        args.Handled = true;
    }

    public bool SetOnAir(EntityUid uid, PublicBroadcastCameraComponent camera, bool onAir, EntityUid? reporter = null)
    {
        if (onAir && _stations.GetOwningStation(uid) is not { } station) return false;
        if (onAir)
        {
            camera.Station = _stations.GetOwningStation(uid);
            camera.Reporter = reporter is { } actor ? Name(actor) : Name(uid);
        }
        _cameras.SetActive(uid, onAir);
        camera.OnAir = TryComp<SurveillanceCameraComponent>(uid, out var surveillance) && surveillance.Active;
        _microphones.SetEnabled(uid, camera.OnAir);
        Dirty(uid, camera);
        RefreshAudience();
        return camera.OnAir == onAir;
    }

    public bool CanReceive(EntityUid television, EntityUid camera)
    {
        if (!TryComp<PublicBroadcastCameraComponent>(camera, out var source) || !source.OnAir || source.Station == null ||
            source.Station != _stations.GetOwningStation(television) ||
            !TryComp<SurveillanceCameraComponent>(camera, out var sensor) || !sensor.Active) return false;
        // A stowed camera cannot film through a bag. Held and neck-worn cameras remain usable.
        if (_containers.TryGetContainingContainer(camera, out var container) && container.ID is not ("left-hand" or "right-hand" or "neck"))
        {
            if (!HasComp<Content.Shared.Hands.Components.HandsComponent>(container.Owner)) return false;
            var hands = EntityManager.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
            if (!hands.IsHolding(container.Owner, camera, out _)) return false;
        }
        return true;
    }

    public bool TryTune(EntityUid uid, TelevisionComponent television, TelevisionChannel channel, EntityUid? camera)
    {
        if (!television.Powered || !Enum.IsDefined(channel) ||
            channel == TelevisionChannel.Broadcast && (camera == null || !CanReceive(uid, camera.Value))) return false;
        television.Channel = channel;
        television.Camera = channel == TelevisionChannel.Broadcast ? camera : null;
        Dirty(uid, television); RefreshAudience(); UpdateUi(uid, television);
        return true;
    }

    private void OnTune(EntityUid uid, TelevisionComponent television, TelevisionTuneMessage message)
    {
        if (!TelevisionUiKey.Key.Equals(message.UiKey)) return;
        TryTune(uid, television, message.Channel, GetEntity(message.Camera));
        UpdateUi(uid, television);
    }

    private void OnPower(EntityUid uid, TelevisionComponent television, ref PowerChangedEvent args)
    {
        television.Powered = args.Powered; Dirty(uid, television);
        if (!args.Powered) _ui.CloseUis(uid);
        RefreshAudience(); UpdateUi(uid, television);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate) return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(.5);
        RefreshAudience();
        var televisions = EntityQueryEnumerator<TelevisionComponent>();
        while (televisions.MoveNext(out var uid, out var television)) UpdateUi(uid, television);
    }

    public void RefreshAudience()
    {
        var sources = EntityQueryEnumerator<PublicBroadcastCameraComponent, SurveillanceCameraComponent>();
        while (sources.MoveNext(out var sourceUid, out var source, out var sensor))
        {
            if (!source.OnAir || sensor.Active) continue;
            source.OnAir = false;
            Dirty(sourceUid, source);
            _microphones.SetEnabled(sourceUid, false);
        }
        var desiredViews = new HashSet<(EntityUid Camera, EntityUid Player)>();
        var desiredSpeakers = new HashSet<(EntityUid Camera, EntityUid Television)>();
        var query = EntityQueryEnumerator<TelevisionComponent>();
        while (query.MoveNext(out var uid, out var television))
        {
            if (!television.Powered || television.Channel != TelevisionChannel.Broadcast || television.Camera is not { } camera) continue;
            if (!CanReceive(uid, camera))
            {
                television.Camera = null; Dirty(uid, television); continue;
            }
            desiredSpeakers.Add((camera, uid));
            var players = EntityQueryEnumerator<ActorComponent>();
            while (players.MoveNext(out var player, out _))
                if (CanWatch(uid, player)) desiredViews.Add((camera, player));
        }
        foreach (var view in _views.Except(desiredViews)) _cameras.RemoveActiveViewer(view.Camera, view.Player);
        foreach (var view in desiredViews) _cameras.AddActiveViewer(view.Camera, view.Player);
        foreach (var speaker in _speakers.Except(desiredSpeakers)) _cameras.RemoveActiveViewers(speaker.Camera, new(), speaker.Television);
        foreach (var speaker in desiredSpeakers.Except(_speakers)) _cameras.AddActiveViewers(speaker.Camera, new(), speaker.Television);
        _views.Clear(); _views.UnionWith(desiredViews);
        _speakers.Clear(); _speakers.UnionWith(desiredSpeakers);
    }

    private void UpdateUi(EntityUid uid, TelevisionComponent television)
    {
        var channels = EntityQuery<PublicBroadcastCameraComponent>().Where(camera => CanReceive(uid, camera.Owner)).Take(32)
            .Select(camera => new PublicBroadcastChannel(GetNetEntity(camera.Owner), camera.Reporter)).ToArray();
        _ui.SetUiState(uid, TelevisionUiKey.Key, new TelevisionState(television.Channel, GetNetEntity(television.Camera), television.Powered, channels));
    }

    private void RemoveCamera(EntityUid camera)
    {
        foreach (var view in _views.Where(view => view.Camera == camera).ToArray())
        { _cameras.RemoveActiveViewer(camera, view.Player); _views.Remove(view); }
        foreach (var speaker in _speakers.Where(speaker => speaker.Camera == camera).ToArray())
        { _cameras.RemoveActiveViewers(camera, new(), speaker.Television); _speakers.Remove(speaker); }
        var televisions = EntityQueryEnumerator<TelevisionComponent>();
        while (televisions.MoveNext(out var uid, out var television))
            if (television.Camera == camera) { television.Camera = null; Dirty(uid, television); }
    }

    private void RemoveTelevision(EntityUid uid)
    {
        foreach (var speaker in _speakers.Where(speaker => speaker.Television == uid).ToArray())
        { _cameras.RemoveActiveViewers(speaker.Camera, new(), uid); _speakers.Remove(speaker); }
        // View subscriptions may be shared by several TVs; recompute after the terminating entity is gone.
        _nextUpdate = TimeSpan.Zero;
    }
}
