using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared.Damage.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Gravity;
using Content.Shared.Light.Components;
using Content.Shared.Mind;
using Content.Shared._Eclipse.Tutorial;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.ContentPack;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

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
    [Dependency] private readonly IResourceManager _resources = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;

    private const string WelcomeLesson = "TutorialWelcome";
    private static readonly ResPath WelcomeDirectory = new("/avi-welcome");

    private readonly Dictionary<Guid, TutorialRun> _runs = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TutorialStartRequestEvent>(OnStartRequest);
        SubscribeNetworkEvent<TutorialAdvanceEvent>(OnAdvance);
        SubscribeNetworkEvent<TutorialChoiceEvent>(OnChoice);
        SubscribeNetworkEvent<TutorialExitEvent>(OnExit);
        SubscribeLocalEvent<PlayerJoinedLobbyEvent>(OnJoinedLobby);
        SubscribeLocalEvent<PlayerBeforeSpawnEvent>(OnBeforeSpawn);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        _players.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    private void OnJoinedLobby(PlayerJoinedLobbyEvent ev)
    {
        var session = ev.PlayerSession;
        if (session.AttachedEntity != null || _runs.ContainsKey(session.UserId.UserId) ||
            _resources.UserData.Exists(WelcomeDirectory / $"{session.UserId.UserId}.txt"))
            return;

        if (_prototypes.TryIndex<TutorialLessonPrototype>(WelcomeLesson, out var lesson) && lesson.Enabled)
            StartTutorial(session, lesson);
    }

    private void OnBeforeSpawn(PlayerBeforeSpawnEvent ev)
    {
        if (_runs.ContainsKey(ev.Player.UserId.UserId))
            ev.Handled = true;
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

        var run = new TutorialRun { Session = session, Lesson = lesson };
        _runs[session.UserId.UserId] = run;
        if (lesson.LobbyOnly)
        {
            RunCurrentStep(run);
            return;
        }

        var mapUid = _maps.CreateMap(out var mapId);
        var coordinates = lesson.PracticeRoom
            ? CreatePracticeRoom(mapId)
            : new EntityCoordinates(mapUid, Vector2.Zero);
        var mob = Spawn(lesson.Body, coordinates);

        // Bare space; keep the player unharmed for the lesson.
        _godmode.EnableGodmode(mob);

        var mindId = _mind.GetOrCreateMind(session.UserId).Owner;
        _mind.TransferTo(mindId, mob);

        // Switch the client to the in-game view so it can see/control the tutorial body.
        RaiseNetworkEvent(new TickerJoinGameEvent(), session.Channel);

        var comp = EnsureComp<TutorialSessionComponent>(mob);
        comp.LessonId = lesson.ID;
        comp.MapId = mapId;

        run.Mob = mob;
        run.MapId = mapId;

        RunCurrentStep(run);
    }

    private EntityCoordinates CreatePracticeRoom(MapId mapId)
    {
        var grid = _mapManager.CreateGridEntity(mapId);
        var tiles = new List<(Vector2i, Tile)>();
        var floor = new Tile(_tiles["FloorSteel"].TileId);
        for (var x = -5; x <= 5; x++)
        for (var y = -5; y <= 5; y++)
            tiles.Add((new Vector2i(x, y), floor));
        _maps.SetTiles(grid.Owner, grid.Comp, tiles);
        var gravity = EnsureComp<GravityComponent>(grid);
        gravity.Enabled = gravity.Inherent = true;
        Dirty(grid, gravity);
        _maps.SetAmbientLight(mapId, Color.White);
        for (var x = -5; x <= 5; x++)
        for (var y = -5; y <= 5; y++)
            if (Math.Abs(x) == 5 || Math.Abs(y) == 5)
                Spawn("WallSolid", new EntityCoordinates(grid.Owner, new Vector2(x + 0.5f, y + 0.5f)));
        return new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f));
    }

    private void RunCurrentStep(TutorialRun run)
    {
        // Run through instant (server-action) steps until we hit one that waits for the player.
        while (run.StepIndex < run.Lesson.Steps.Count)
        {
            run.StepId = Guid.NewGuid();
            switch (run.Lesson.Steps[run.StepIndex])
            {
                case TutorialDialogueStep d:
                    RaiseNetworkEvent(new TutorialShowDialogueEvent
                    {
                        StepId = run.StepId,
                        CanExit = !run.Lesson.LobbyOnly,
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
                        StepId = run.StepId,
                        CanExit = !run.Lesson.LobbyOnly,
                        Speaker = t.Speaker,
                        Text = t.Objective,
                        Portrait = t.Portrait,
                        CanAdvance = false,
                        SpotlightHealthAlert = true,
                    }, run.Session.Channel);
                    return;

                case TutorialChoiceStep choice:
                    RaiseNetworkEvent(new TutorialShowDialogueEvent
                    {
                        StepId = run.StepId,
                        Speaker = choice.Speaker,
                        Text = choice.Text,
                        Portrait = choice.Portrait,
                        ShowChoice = true,
                        CanAdvance = false,
                    }, run.Session.Channel);
                    return;

                case TutorialPracticeStep task:
                    if (run.Mob is not { } body)
                        throw new InvalidOperationException("Practice tasks require a tutorial body.");
                    run.TaskStart = Transform(body).LocalPosition;
                    RaiseNetworkEvent(new TutorialShowDialogueEvent
                    {
                        StepId = run.StepId,
                        Speaker = "Ави",
                        Text = task.Objective,
                        CanAdvance = false,
                        CanExit = true,
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
        if (run.Mob is not { } mob)
            return;
        var item = Spawn(proto, Transform(mob).Coordinates);
        _hands.TryForcePickupAnyHand(mob, item);
        run.PracticeItem = item;
    }

    private void ClearHands(TutorialRun run)
    {
        if (run.Mob is not { } mob)
            return;
        foreach (var held in _hands.EnumerateHeld(mob).ToArray())
            QueueDel(held);
    }

    private void OnAdvance(TutorialAdvanceEvent ev, EntitySessionEventArgs args)
    {
        if (!_runs.TryGetValue(args.SenderSession.UserId.UserId, out var run))
            return;

        if (ev.StepId != run.StepId || run.StepIndex >= run.Lesson.Steps.Count ||
            run.Lesson.Steps[run.StepIndex] is not (TutorialDialogueStep or TutorialClickHealthStep))
            return;

        run.StepIndex++;
        RunCurrentStep(run);
    }

    private void OnChoice(TutorialChoiceEvent ev, EntitySessionEventArgs args)
    {
        if (!_runs.TryGetValue(args.SenderSession.UserId.UserId, out var run) ||
            ev.StepId != run.StepId || run.StepIndex >= run.Lesson.Steps.Count ||
            run.Lesson.Steps[run.StepIndex] is not TutorialChoiceStep choice)
            return;

        TutorialLessonPrototype? next = null;
        if (ev.StartLesson && (!_prototypes.TryIndex(choice.Lesson, out next) || !next.Enabled || next.LobbyOnly))
            return;

        if (run.Lesson.ID == WelcomeLesson)
        {
            try
            {
                _resources.UserData.CreateDir(WelcomeDirectory);
                using var writer = _resources.UserData.OpenWriteText(WelcomeDirectory / $"{run.Session.UserId.UserId}.txt");
                writer.WriteLine(ev.StartLesson ? "training" : "lobby");
            }
            catch (IOException e)
            {
                Log.Error($"Could not save Avi welcome completion: {e}");
            }
        }

        EndTutorial(run);
        if (next != null)
            StartTutorial(args.SenderSession, next);
    }

    private void OnExit(TutorialExitEvent ev, EntitySessionEventArgs args)
    {
        if (_runs.TryGetValue(args.SenderSession.UserId.UserId, out var run) && !run.Lesson.LobbyOnly)
            EndTutorial(run);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var run in _runs.Values.ToArray())
        {
            if (run.Mob is not { } mob || run.StepIndex >= run.Lesson.Steps.Count)
                continue;
            if (Deleted(mob) || run.Session.AttachedEntity != mob)
            {
                EndTutorial(run);
                continue;
            }
            if (run.Lesson.Steps[run.StepIndex] is not TutorialPracticeStep task)
                continue;

            var held = run.PracticeItem is { } item && _hands.IsHolding(mob, item);
            var complete = task.Action switch
            {
                TutorialPracticeAction.Move => Vector2.Distance(Transform(mob).LocalPosition, run.TaskStart) >= task.Distance,
                TutorialPracticeAction.DropItem => run.PracticeItem != null && !held,
                TutorialPracticeAction.PickUpItem => held,
                TutorialPracticeAction.ActivateLight => held && TryComp<HandheldLightComponent>(run.PracticeItem, out var light) && light.Activated,
                _ => false,
            };
            if (!complete)
                continue;
            run.StepIndex++;
            RunCurrentStep(run);
        }
    }

    private void EndTutorial(TutorialRun run)
    {
        RaiseNetworkEvent(new TutorialHideEvent(), run.Session.Channel);
        _runs.Remove(run.Session.UserId.UserId);

        // Return to the lobby (wipes the tutorial mind), then delete the private map.
        if (run.Mob != null && run.Session.Status == SessionStatus.InGame)
            _ticker.Respawn(run.Session);

        if (run.MapId is { } mapId && _maps.MapExists(mapId))
            _maps.DeleteMap(mapId);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        // Respawn may raise PlayerJoinedLobbyEvent: keep the run registered until cleanup finishes.
        foreach (var run in _runs.Values.ToArray())
        {
            RaiseNetworkEvent(new TutorialHideEvent(), run.Session.Channel);
            // Make sure the client leaves the in-game view (its tutorial body is going away).
            RaiseNetworkEvent(new TickerJoinLobbyEvent(), run.Session.Channel);
            if (run.MapId is { } mapId && _maps.MapExists(mapId))
                _maps.DeleteMap(mapId);
        }

        _runs.Clear();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.Disconnected)
            return;

        if (!_runs.Remove(args.Session.UserId.UserId, out var run))
            return;

        if (run.MapId is { } mapId && _maps.MapExists(mapId))
            _maps.DeleteMap(mapId);
    }

    private sealed class TutorialRun
    {
        public ICommonSession Session = default!;
        public TutorialLessonPrototype Lesson = default!;
        public EntityUid? Mob;
        public MapId? MapId;
        public int StepIndex;
        public Guid StepId;
        public Vector2 TaskStart;
        public EntityUid? PracticeItem;
    }
}
