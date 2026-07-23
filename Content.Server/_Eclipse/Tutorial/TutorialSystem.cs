using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mind;
using Content.Shared._Eclipse.Tutorial;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.Tutorial;

/// <summary>
/// Server side of the tutorial. Spawns the player a real body on a private map so lessons can affect the
/// server world (spawn items into hands, delete things, etc.), and drives the step sequence.
///
/// It never sets the player's ticker status to "joined" (which would break round-start), only flips them
/// to "not ready" and switches their client to the game view manually. Runs are torn down on round
/// restart / disconnect. Startable from the lobby.
/// </summary>
public sealed class TutorialSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedGodmodeSystem _godmode = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly GameTicker _ticker = default!;

    private readonly Dictionary<Guid, TutorialRun> _runs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TutorialStartRequestEvent>(OnStartRequest);
        SubscribeNetworkEvent<TutorialAdvanceEvent>(OnAdvance);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _players.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    private void OnStartRequest(TutorialStartRequestEvent ev, EntitySessionEventArgs args)
    {
        var session = args.SenderSession;

        if (_runs.ContainsKey(session.UserId.UserId))
            return;

        if (!_prototypes.TryIndex<TutorialLessonPrototype>(ev.LessonId, out var lesson) || !lesson.Enabled)
        {
            _chat.DispatchServerMessage(session, "Этот урок пока недоступен.", suppressLog: true);
            return;
        }

        // Must be a lobby player (no round body to hijack).
        if (session.AttachedEntity != null)
        {
            _chat.DispatchServerMessage(session, "Обучение можно начать только из лобби.", suppressLog: true);
            return;
        }

        StartTutorial(session, lesson);
    }

    private void StartTutorial(ICommonSession session, TutorialLessonPrototype lesson)
    {
        // Keep the player "not ready" so a starting round ignores them and the round-start counts stay
        // consistent (never set JoinedGame from the lobby — that trips a round-start assert).
        _ticker.ToggleReady(session, false);

        var mapUid = _maps.CreateMap(out var mapId);
        var mob = Spawn(lesson.Body, new EntityCoordinates(mapUid, Vector2.Zero));

        // Bare space; keep the player unharmed for the lesson.
        _godmode.EnableGodmode(mob);

        var mindId = _mind.GetOrCreateMind(session.UserId).Owner;
        _mind.TransferTo(mindId, mob);

        // Switch the client to the in-game view so it can see/control the tutorial body.
        RaiseNetworkEvent(new TickerJoinGameEvent(), session.Channel);

        var comp = EnsureComp<TutorialSessionComponent>(mob);
        comp.LessonId = lesson.ID;
        comp.MapId = mapId;

        var run = new TutorialRun
        {
            Session = session,
            Lesson = lesson,
            Mob = mob,
            MapId = mapId,
            StepIndex = 0,
        };
        _runs[session.UserId.UserId] = run;

        RunCurrentStep(run);
    }

    private void RunCurrentStep(TutorialRun run)
    {
        // Run through instant (server-action) steps until we hit one that waits for the player.
        while (run.StepIndex < run.Lesson.Steps.Count)
        {
            switch (run.Lesson.Steps[run.StepIndex])
            {
                case TutorialDialogueStep d:
                    RaiseNetworkEvent(new TutorialShowDialogueEvent
                    {
                        Speaker = d.Speaker,
                        Text = d.Text,
                        Portrait = d.Portrait,
                        Side = d.Side,
                        Animation = d.Animation,
                        CanAdvance = true,
                    }, run.Session.Channel);
                    return;

                case TutorialClickHealthStep t:
                    RaiseNetworkEvent(new TutorialShowDialogueEvent
                    {
                        Speaker = t.Speaker,
                        Text = t.Objective,
                        Portrait = t.Portrait,
                        CanAdvance = false,
                        SpotlightHealthAlert = true,
                    }, run.Session.Channel);
                    return;

                case TutorialSpawnInHandStep s:
                    SpawnInHand(run, s.Item);
                    run.StepIndex++;
                    continue;

                case TutorialClearHandsStep:
                    ClearHands(run);
                    run.StepIndex++;
                    continue;

                default:
                    run.StepIndex++;
                    continue;
            }
        }

        EndTutorial(run);
    }

    private void SpawnInHand(TutorialRun run, EntProtoId proto)
    {
        var item = Spawn(proto, Transform(run.Mob).Coordinates);
        _hands.TryForcePickupAnyHand(run.Mob, item);
    }

    private void ClearHands(TutorialRun run)
    {
        foreach (var held in _hands.EnumerateHeld(run.Mob))
            QueueDel(held);
    }

    private void OnAdvance(TutorialAdvanceEvent ev, EntitySessionEventArgs args)
    {
        if (!_runs.TryGetValue(args.SenderSession.UserId.UserId, out var run))
            return;

        if (run.StepIndex >= run.Lesson.Steps.Count)
            return;

        run.StepIndex++;
        RunCurrentStep(run);
    }

    private void EndTutorial(TutorialRun run)
    {
        RaiseNetworkEvent(new TutorialHideEvent(), run.Session.Channel);
        _runs.Remove(run.Session.UserId.UserId);

        // Return to the lobby (wipes the tutorial mind), then delete the private map.
        if (run.Session.Status == SessionStatus.InGame)
            _ticker.Respawn(run.Session);

        if (_maps.MapExists(run.MapId))
            _maps.DeleteMap(run.MapId);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        foreach (var run in _runs.Values)
        {
            RaiseNetworkEvent(new TutorialHideEvent(), run.Session.Channel);
            // Make sure the client leaves the in-game view (its tutorial body is going away).
            RaiseNetworkEvent(new TickerJoinLobbyEvent(), run.Session.Channel);
            if (_maps.MapExists(run.MapId))
                _maps.DeleteMap(run.MapId);
        }

        _runs.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        if (!_runs.Remove(args.Session.UserId.UserId, out var run))
            return;

        if (_maps.MapExists(run.MapId))
            _maps.DeleteMap(run.MapId);
    }

    private sealed class TutorialRun
    {
        public ICommonSession Session = default!;
        public TutorialLessonPrototype Lesson = default!;
        public EntityUid Mob;
        public MapId MapId;
        public int StepIndex;
    }
}
