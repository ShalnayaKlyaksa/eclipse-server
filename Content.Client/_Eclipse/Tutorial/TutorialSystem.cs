using System;
using Content.Client.Alerts;
using Content.Shared._Eclipse.Tutorial;
using Robust.Client;
using Robust.Client.UserInterface;

namespace Content.Client._Eclipse.Tutorial;

/// <summary>
/// Client side of the tutorial: forwards the lesson request to the server and shows the visual-novel /
/// task overlay from server messages. Reports task completion (dialogue click, or the real health alert
/// being clicked — which this fork handles client-side) back to the server.
/// </summary>
public sealed class TutorialSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IBaseClient _client = default!;

    private TutorialDialogueControl? _overlay;
    private bool _waitingForHealth;
    private Guid _stepId;
    private bool _waitingForServer;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TutorialShowDialogueEvent>(OnShow);
        SubscribeNetworkEvent<TutorialHideEvent>(OnHide);
        SubscribeLocalEvent<AlertHudPressedEvent>(OnAlertPressed);
        _client.RunLevelChanged += OnRunLevelChanged;
    }

    public void RequestLesson(string lessonId)
    {
        RaiseNetworkEvent(new TutorialStartRequestEvent(lessonId));
    }

    private void Advance()
    {
        if (_waitingForServer)
            return;
        _waitingForServer = true;
        RaiseNetworkEvent(new TutorialAdvanceEvent { StepId = _stepId });
    }

    private void OnShow(TutorialShowDialogueEvent ev)
    {
        if (_overlay == null)
        {
            _overlay = new TutorialDialogueControl(Advance,
                start => RaiseNetworkEvent(new TutorialChoiceEvent { StepId = _stepId, StartLesson = start }),
                () => RaiseNetworkEvent(new TutorialExitEvent()));
            _ui.WindowRoot.AddChild(_overlay);
        }

        _overlay.SetSize = _ui.WindowRoot.Size;
        _overlay.SetCanExit(ev.CanExit);
        _stepId = ev.StepId;
        _waitingForServer = false;
        if (ev.CanAdvance || ev.ShowChoice)
        {
            _overlay.ShowDialogue(ev.Speaker, ev.Text, ev.Portrait, ev.Side, ev.Animation, ev.ShowChoice);
            _waitingForHealth = false;
        }
        else
        {
            _overlay.ShowTask(ev.Text, ev.SpotlightHealthAlert);
            _waitingForHealth = ev.SpotlightHealthAlert;
        }
    }

    private void OnAlertPressed(AlertHudPressedEvent ev)
    {
        if (_waitingForHealth && ev.Type.Id is "HumanHealth" or "HumanCrit" or "HumanDead")
        {
            _waitingForHealth = false;
            Advance();
        }
    }

    private void OnHide(TutorialHideEvent ev)
    {
        ClearOverlay();
    }

    private void OnRunLevelChanged(object? sender, RunLevelChangedEventArgs ev)
    {
        if (ev.NewLevel != ClientRunLevel.InGame)
            ClearOverlay();
    }

    private void ClearOverlay()
    {
        _overlay?.Dispose();
        _overlay = null;
        _waitingForHealth = false;
        _waitingForServer = false;
    }

    public override void Shutdown()
    {
        _client.RunLevelChanged -= OnRunLevelChanged;
        ClearOverlay();
        base.Shutdown();
    }
}
