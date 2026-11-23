using Content.Shared.Chemistry.EntitySystems;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Content.Shared.Interaction;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>
/// Client-side helper that relays treatment results from minigames to the server.
/// </summary>
public sealed class AdvancedHealthClientSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly TagSystem _tags = default!;

    private AdvancedHealthFieldWindow? _fieldWindow;
    private AdvancedHealthTreatmentMinigameWindow? _injectionWindow;
    private MedicalDiagnosticReportWindow? _diagnosticWindow;
    private int _nextInjectionId;
    public event Action<AdvancedHealthInjectionStatusEvent>? InjectionStatus;

    public override void Initialize()
    {
        SubscribeLocalEvent<AdvancedHealthComponent, GetVerbsEvent<ExamineVerb>>(OnHealthVerb);
        SubscribeNetworkEvent<AdvancedHealthInjectionStatusEvent>(ev => InjectionStatus?.Invoke(ev));
        SubscribeNetworkEvent<MedicalDiagnosticResultEvent>(ev =>
        {
            _diagnosticWindow?.Close();
            _diagnosticWindow = new MedicalDiagnosticReportWindow(ev.Report);
            _diagnosticWindow.OpenCentered();
        });
        SubscribeNetworkEvent<AdvancedHealthOpenTreatmentEvent>(ev =>
        {
            var target = GetEntity(ev.Target);
            var tool = GetEntity(ev.Tool);
            if (!Exists(target) || !Exists(tool)) return;
            _injectionWindow?.Close();
            var player = IoCManager.Resolve<Robust.Client.Player.IPlayerManager>().LocalEntity;
            var pain = player is { } user && TryComp<AdvancedHealthComponent>(user, out var health) ? health.Pain : 0;
            _injectionWindow = new AdvancedHealthTreatmentMinigameWindow(target, ev.Slot, ev.Treatment, tool, pain);
            _injectionWindow.OpenCentered();
        });
    }

    private void OnHealthVerb(EntityUid uid, AdvancedHealthComponent component, GetVerbsEvent<ExamineVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        args.Verbs.Add(new ExamineVerb
        {
            Text = Loc.GetString("advanced-health-examine-verb"),
            Category = VerbCategory.Examine,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png")),
            ClientExclusive = true,
            Act = () =>
            {
                if (user != uid && !EntityManager.System<SharedInteractionSystem>().InRangeUnobstructed(user, uid))
                    return;
                _fieldWindow?.Close();
                var window = new AdvancedHealthFieldWindow(uid, user);
                _fieldWindow = window;
                window.OnClose += () =>
                {
                    if (_fieldWindow == window) _fieldWindow = null;
                };
                window.OpenCentered();
            },
        });
    }

    public int BeginInjection(EntityUid target, EntityUid tool, BodyPartSlot slot)
    {
        if (++_nextInjectionId <= 0) _nextInjectionId = 1;
        SendInjectionInput(_nextInjectionId, target, tool, slot, InjectionInputAction.Start);
        return _nextInjectionId;
    }

    public void SendInjectionInput(int id, EntityUid target, EntityUid tool, BodyPartSlot slot,
        InjectionInputAction action, float depth = 0, bool held = false) =>
        RaiseNetworkEvent(new AdvancedHealthInjectionInputEvent(id, GetNetEntity(target), GetNetEntity(tool), slot, action, depth, held));

    private static readonly ProtoId<TagPrototype> TourniquetTag = "Tourniquet";
    private static readonly ProtoId<TagPrototype> OintmentTag = "Ointment";

    public void ReportTreatmentInjury(EntityUid target, BodyPartSlot slot, AdvancedTreatmentType treatment, EntityUid? tool, float severity)
        => RaiseNetworkEvent(new AdvancedHealthTreatmentInjuryEvent(GetNetEntity(target), slot, treatment,
            tool is { } item ? GetNetEntity(item) : null, severity));

    public void CompleteTreatment(EntityUid target, BodyPartSlot slot, AdvancedTreatmentType treatment,
        float quality, EntityUid? tool, int segments = 0)
    {
        RaiseNetworkEvent(new AdvancedHealthTreatmentCompleteEvent(
            GetNetEntity(target),
            slot,
            treatment,
            quality,
            tool is { } t ? GetNetEntity(t) : null,
            segments));
    }

    public void SetAimTarget(BodyPartTarget target)
    {
        RaiseNetworkEvent(new AdvancedHealthSetAimTargetEvent(target));
        _fieldWindow?.SelectPart(target);
    }

    /// <summary>Finds a held blood/fluid pack, if any.</summary>
    public bool TryGetHeldBloodProduct(EntityUid user, out EntityUid pack)
    {
        pack = default;
        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (HasComp<BloodProductComponent>(held))
            {
                pack = held;
                return true;
            }
        }

        return false;
    }

    public void Transfuse(EntityUid target, EntityUid pack)
    {
        RaiseNetworkEvent(new AdvancedHealthTransfusionEvent(GetNetEntity(target), GetNetEntity(pack)));
    }

    /// <summary>Finds a recognised treatment tool currently held and the treatment it provides.</summary>
    public bool TryGetHeldTreatment(EntityUid user, out AdvancedTreatmentType treatment, out EntityUid tool)
    {
        treatment = default;
        tool = default;

        if (_hands.GetActiveItem(user) is not { } held) return false;
        if (TryComp<AdvancedTreatmentComponent>(held, out var advanced))
        {
            treatment = advanced.Treatment;
            tool = held;
            return true;
        }
        foreach (var candidate in Enum.GetValues<AdvancedTreatmentType>())
        {
            if (candidate == AdvancedTreatmentType.ForeignBodyRemoval) continue;
            if (!MatchesLocalTool(held, candidate)) continue;
            treatment = candidate;
            tool = held;
            return true;
        }

        return false;
    }

    public bool TryGetPartAction(EntityUid user, EntityUid target, BodyPartSlot slot, out EntityUid? tool, out string label)
    {
        tool = _hands.GetActiveItem(user);
        label = "";
        if (!TryComp<AdvancedHealthComponent>(target, out var health) || !health.BodyParts.TryGetValue(slot, out var part)) return false;
        if (tool == null)
        {
            if (!AdvancedTreatmentRules.IsNeeded(part, AdvancedTreatmentType.ForeignBodyRemoval)) return false;
            label = Loc.GetString("advanced-health-action-foreignbodyremoval");
            return true;
        }
        if (HasComp<BloodProductComponent>(tool.Value))
        {
            label = Loc.GetString("advanced-health-action-transfuse");
            return true;
        }
        if (slot is BodyPartSlot.LeftForearm or BodyPartSlot.RightForearm
            && EntityManager.System<InjectorSystem>().TryGetBloodDrawCapacity(tool.Value, out _))
        {
            label = Loc.GetString("advanced-health-action-drawblood");
            return true;
        }
        if (!TryGetHeldTreatment(user, out var treatment, out _) || !AdvancedTreatmentRules.IsNeeded(part, treatment)) return false;
        label = Loc.GetString("advanced-health-action-" + treatment.ToString().ToLowerInvariant());
        return true;
    }

    public void UseHeldItemOnPart(EntityUid user, EntityUid target, BodyPartSlot slot)
    {
        if (!TryGetPartAction(user, target, slot, out var tool, out _)) return;
        if (tool == null)
        {
            var pain = TryComp<AdvancedHealthComponent>(user, out var health) ? health.Pain : 0;
            new AdvancedHealthTreatmentMinigameWindow(target, slot, AdvancedTreatmentType.ForeignBodyRemoval, null, pain).OpenCentered();
        }
        else if (HasComp<BloodProductComponent>(tool.Value)) Transfuse(target, tool.Value);
        else RaiseNetworkEvent(new AdvancedHealthUseItemOnPartEvent(GetNetEntity(target), GetNetEntity(tool.Value), slot));
    }

    /// <summary>Returns a held treatment tool, or null when bare hands are allowed.</summary>
    public bool TryFindLocalTool(EntityUid user, AdvancedTreatmentType treatment, out EntityUid? tool)
    {
        tool = null;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (MatchesLocalTool(held, treatment))
            {
                tool = held;
                return true;
            }
        }

        return treatment == AdvancedTreatmentType.ForeignBodyRemoval;
    }

    private bool MatchesLocalTool(EntityUid item, AdvancedTreatmentType treatment)
    {
        if (treatment == AdvancedTreatmentType.Injection)
            return EntityManager.System<InjectorSystem>().TryGetInjectionVolume(item, out _);

        if (TryComp<AdvancedTreatmentComponent>(item, out var advanced))
            return advanced.Treatment == treatment;

        if (MetaData(item).EntityPrototype is not { } proto)
            return false;

        var id = proto.ID;
        return treatment switch
        {
            AdvancedTreatmentType.Bandage or AdvancedTreatmentType.PressureBandage =>
                HasComp<AdvancedBandageRollComponent>(item),
            AdvancedTreatmentType.Tourniquet => _tags.HasTag(item, TourniquetTag),
            AdvancedTreatmentType.Splint => id is "Brutepack" or "Brutepack1",
            AdvancedTreatmentType.Hemostatic => _tags.HasTag(item, OintmentTag),
            AdvancedTreatmentType.Suture => id is "MedicatedSuture" or "MedicatedSuture1" or "BrutepackAdvanced1",
            AdvancedTreatmentType.ForeignBodyRemoval => id is "AdvancedForcepsPack",
            AdvancedTreatmentType.ClinicalExamination => id is "HandheldHealthAnalyzer" or "BasicMedScanner",
            _ => false,
        };
    }
}
