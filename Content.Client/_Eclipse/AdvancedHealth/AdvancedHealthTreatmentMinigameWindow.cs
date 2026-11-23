using System.Linq;
using System.Numerics;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Timing;
using Robust.Client.Player;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>A compact field-treatment window shared by every medical procedure.</summary>
public sealed class AdvancedHealthTreatmentMinigameWindow : BaseWindow
{
    private static readonly Color Ink = Color.FromHex("#dddcd3");
    private static readonly Color Muted = Color.FromHex("#92978f");
    private readonly AdvancedHealthClientSystem _client;
    private readonly EntityUid _target;
    private readonly BodyPartSlot _slot;
    private readonly AdvancedTreatmentType _treatment;
    private readonly EntityUid? _tool;
    private readonly bool _isBandage;
    private readonly TreatmentMinigameControl _game;
    private bool _bandageApplied;
    private int _injectionId;
    private bool _injectionStopped;
    private bool _injectionHeld;
    private float _injectionUpdate;
    private bool _hasInjectionStatus;
    private bool _closeQueued;
    private bool _closed;

    public AdvancedHealthTreatmentMinigameWindow(EntityUid target, BodyPartSlot slot,
        AdvancedTreatmentType treatment, EntityUid? tool, float painPercent)
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        _client = entities.System<AdvancedHealthClientSystem>();
        _target = target;
        _slot = slot;
        _treatment = treatment;
        _tool = tool;
        _isBandage = treatment is AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage;
        var maxSegments = 0;
        var durability = 0f;
        var bleeding = 0f;
        var perSegment = AdvancedBandageRollComponent.BleedPerPercent *
            (treatment == AdvancedTreatmentType.PressureBandage ? 1.6f : 1f);
        if (_isBandage)
        {
            if (tool is { } uid && entities.TryGetComponent<AdvancedBandageRollComponent>(uid, out var roll))
                durability = roll.Durability;
            if (entities.TryGetComponent<AdvancedHealthComponent>(target, out var health) &&
                health.BodyParts.TryGetValue(slot, out var part))
                bleeding = part.Wounds.Sum(w => w.ExternalBleedingRate) * 0.06f;
            maxSegments = Math.Max(0, Math.Min((int) MathF.Floor(durability),
                (int) MathF.Ceiling(bleeding / perSegment - 0.0001f)));
        }

        MouseFilter = MouseFilterMode.Stop;
        Resizable = false;
        var available = Vector2.Max(Vector2.One, UserInterfaceManager.WindowRoot.Size - new Vector2(24));
        MinSize = Vector2.Min(new Vector2(580, 420), available);
        SetSize = Vector2.Min(new Vector2(720, 520), available);
        var shell = new PanelContainer
        {
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex("#080909"),
                BorderColor = Color.FromHex("#555a54"),
                BorderThickness = new Thickness(1),
            },
        };
        AddChild(shell);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 0 };
        shell.AddChild(root);
        var header = new PanelContainer { PanelOverride = new StyleBoxFlat(Color.FromHex("#101210")) };
        root.AddChild(header);
        var heading = new BoxContainer { SeparationOverride = 12, Margin = new Thickness(14, 7) };
        header.AddChild(heading);
        var titles = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 2 };
        titles.AddChild(Text(Loc.GetString($"advanced-health-minigame-title-{treatment.ToString().ToLowerInvariant()}"), 16, Ink, true));
        titles.AddChild(Text(Loc.GetString($"advanced-health-part-{slot.ToString().ToLowerInvariant()}"), 12, Muted));
        heading.AddChild(titles);
        var close = MakeButton("×", Close);
        close.MinSize = new Vector2(30);
        heading.AddChild(close);

        var mode = treatment switch
        {
            AdvancedTreatmentType.SurgicalClosure => AdvancedTreatmentType.Suture,
            AdvancedTreatmentType.BoneFixation => AdvancedTreatmentType.Splint,
            _ => treatment,
        };
        var foreignBody = ForeignBodyKind.Unknown;
        if (entities.TryGetComponent<AdvancedHealthComponent>(target, out var patientHealth)
            && patientHealth.BodyParts.TryGetValue(slot, out var bodyPart)
            && bodyPart.Wounds.FirstOrDefault(w => w.HasForeignBody) is { } wound)
            foreignBody = wound.ForeignBody != ForeignBodyKind.Unknown ? wound.ForeignBody
                : wound.Type == WoundType.Gunshot ? ForeignBodyKind.Bullet : ForeignBodyKind.Fragment;
        var dose = tool is { } syringe && entities.System<InjectorSystem>().TryGetInjectionVolume(syringe, out var amount)
            ? amount.Float() : 0;
        var syringeCapacity = 15f;
        if (treatment == AdvancedTreatmentType.Injection && tool is { } injectorUid
            && entities.TryGetComponent<InjectorComponent>(injectorUid, out var injector)
            && entities.System<SharedSolutionContainerSystem>().TryGetSolution(injectorUid, injector.SolutionName, out _, out var solution)
            && solution.MaxVolume > 0)
        {
            syringeCapacity = solution.MaxVolume.Float();
        }
        _game = new TreatmentMinigameControl(mode, Math.Clamp(painPercent / 100, 0, 1), tool != null,
            maxSegments, durability, bleeding, perSegment, foreignBody, dose, syringeCapacity)
        {
            Name = "TreatmentGame",
            Margin = new Thickness(8, 0, 8, 0),
            MinHeight = 220,
            HorizontalExpand = true,
            VerticalExpand = true,
        };
        root.AddChild(_game);
        _game.OnInjury += severity =>
        {
            if (IoCManager.Resolve<IPlayerManager>().LocalEntity != null)
                _client.ReportTreatmentInjury(_target, _slot, _treatment, _tool, severity);
        };
        _game.OnCancel += Close;
        _game.OnNeedleBroken += depth =>
        {
            if (_tool is not { } syringe || IoCManager.Resolve<IPlayerManager>().LocalEntity == null) return;
            if (_injectionId == 0) _injectionId = _client.BeginInjection(_target, syringe, _slot);
            _client.SendInjectionInput(_injectionId, _target, syringe, _slot, InjectionInputAction.Adjust, depth);
            _client.SendInjectionInput(_injectionId, _target, syringe, _slot, InjectionInputAction.BreakNeedle);
        };
        if (_treatment == AdvancedTreatmentType.Injection)
            _client.InjectionStatus += OnInjectionStatus;
        _game.OnFinished += result =>
        {
            if (_treatment == AdvancedTreatmentType.Injection) StopInjection();
            else if (result.Success)
            {
                if (_isBandage) ApplyBandageProgress();
                else if (IoCManager.Resolve<IPlayerManager>().LocalEntity != null)
                    _client.CompleteTreatment(_target, _slot, _treatment, result.Quality, _tool);
            }
            QueueClose();
        };
    }

    private void QueueClose()
    {
        if (_closeQueued || _closed) return;
        _closeQueued = true;
        Timer.Spawn(0, Close);
    }

    protected override DragMode GetDragModeFor(Vector2 position) => position.Y < 54 ? DragMode.Move : DragMode.None;

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var entities = IoCManager.Resolve<IEntityManager>();
        var medic = IoCManager.Resolve<IPlayerManager>().LocalEntity;
        if (entities.TryGetComponent<AdvancedHealthComponent>(_target, out var patient)
            && patient.BodyParts.TryGetValue(_slot, out var part))
        {
            var operatorPain = medic is { } user && entities.TryGetComponent<AdvancedHealthComponent>(user, out var doctor) ? doctor.Pain / 100 : 0;
            _game.SetMedicalState(operatorPain, patient.GetBloodLossRate(part) * 0.06f, part.SkinFraction);
        }
        if (_treatment == AdvancedTreatmentType.Injection && !_injectionStopped && !_closeQueued && _tool is { } syringe
            && IoCManager.Resolve<IPlayerManager>().LocalEntity != null)
        {
            if (_injectionId == 0)
                _injectionId = _client.BeginInjection(_target, syringe, _slot);
            _injectionUpdate -= args.DeltaSeconds;
            var held = _game.IsRunning && _game.State.PressingPlunger;
            if (_injectionUpdate <= 0 || held != _injectionHeld)
            {
                _injectionUpdate = 0.12f;
                _injectionHeld = held;
                var depth = _game.State.NeedleInserted ? Math.Clamp(_game.State.SyringeTip.Y - TreatmentMinigameState.TissueSurface, 0, 44) : 0;
                _client.SendInjectionInput(_injectionId, _target, syringe, _slot, InjectionInputAction.Adjust, depth, held);
            }
        }
        if (_treatment == AdvancedTreatmentType.Injection && _tool is { } tool
            && entities.TryGetComponent<InjectorComponent>(tool, out var injector)
            && entities.System<SharedSolutionContainerSystem>().TryGetSolution(tool, injector.SolutionName, out _, out var contents))
            _game.SetInjectionAmount(_hasInjectionStatus ? _game.State.InjectedVolume : 0,
                _hasInjectionStatus ? _game.State.RemainingVolume : contents.Volume.Float(), contents.MaxVolume.Float());
    }

    public override void Close()
    {
        if (_closed) return;
        _closed = true;
        if (_isBandage) ApplyBandageProgress();
        if (_treatment == AdvancedTreatmentType.Injection)
        {
            StopInjection();
            _client.InjectionStatus -= OnInjectionStatus;
        }
        base.Close();
    }

    private void StopInjection()
    {
        if (_injectionId != 0 && !_injectionStopped && _tool is { } syringe)
            _client.SendInjectionInput(_injectionId, _target, syringe, _slot, InjectionInputAction.Stop);
        _injectionStopped = true;
    }

    private void OnInjectionStatus(AdvancedHealthInjectionStatusEvent ev)
    {
        if (ev.Id != _injectionId) return;
        _hasInjectionStatus = true;
        _game.SetInjectionAmount(ev.Injected, ev.Remaining);
        if (ev.NeedleBroken) _game.State.BreakNeedle(report: false);
        if (ev.Failed) QueueClose();
    }

    private void ApplyBandageProgress()
    {
        if (_bandageApplied) return;
        _bandageApplied = true;
        if (_game.CompletedSegments > 0 && IoCManager.Resolve<IPlayerManager>().LocalEntity != null)
            _client.CompleteTreatment(_target, _slot, _treatment, 1, _tool, _game.CompletedSegments);
    }

    private static Font Font(int size, bool bold = false) => new VectorFont(
        IoCManager.Resolve<IResourceCache>().GetResource<FontResource>(
            $"/Fonts/NotoSansDisplay/NotoSansDisplay-{(bold ? "Bold" : "Regular")}.ttf"), size);

    private static Label Text(string text, int size, Color color, bool bold = false) => new()
    {
        Text = text, FontOverride = Font(size, bold), FontColorOverride = color,
        VerticalAlignment = VAlignment.Center,
    };

    private static Button MakeButton(string text, Action action)
    {
        var normal = new StyleBoxFlat
        {
            BackgroundColor = Color.FromHex("#171b18"), BorderColor = Color.FromHex("#70786c"),
            BorderThickness = new Thickness(1), ContentMarginLeftOverride = 12,
            ContentMarginRightOverride = 12, ContentMarginTopOverride = 5, ContentMarginBottomOverride = 5,
        };
        var button = new Button
        {
            Text = text, StyleBoxOverride = normal, MinHeight = 30,
            VerticalAlignment = VAlignment.Center, ModulateSelfOverride = Color.White,
        };
        button.Label.FontOverride = Font(13);
        button.Label.FontColorOverride = Ink;
        button.OnMouseEntered += _ => button.ModulateSelfOverride = Color.FromHex("#d0e9df");
        button.OnMouseExited += _ => button.ModulateSelfOverride = Color.White;
        button.OnPressed += _ => action();
        return button;
    }
}
