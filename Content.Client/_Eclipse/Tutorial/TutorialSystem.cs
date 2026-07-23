using Content.Client.Alerts;
using Content.Shared._Eclipse.Tutorial;
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

    private TutorialDialogueControl? _overlay;
    private bool _waitingForHealth;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<TutorialShowDialogueEvent>(OnShow);
        SubscribeNetworkEvent<TutorialHideEvent>(OnHide);
        SubscribeLocalEvent<AlertHudPressedEvent>(OnAlertPressed);
    }

    public void RequestLesson(string lessonId)
    {
        RaiseNetworkEvent(new TutorialStartRequestEvent(lessonId));
    }

    private void Advance()
    {
        RaiseNetworkEvent(new TutorialAdvanceEvent());
    }

    private void OnShow(TutorialShowDialogueEvent ev)
    {
        if (_overlay == null)
        {
            _overlay = new TutorialDialogueControl(Advance);
            _ui.WindowRoot.AddChild(_overlay);
        }

        if (ev.CanAdvance)
        {
            _overlay.ShowDialogue(ev.Speaker, ev.Text, ev.Portrait, ev.Side, ev.Animation);
            _waitingForHealth = false;
        }
        else
        {
            _overlay.ShowHealthTask(ev.Speaker, ev.Text);
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
        _overlay?.Orphan();
        _overlay = null;
        _waitingForHealth = false;
    }
}
