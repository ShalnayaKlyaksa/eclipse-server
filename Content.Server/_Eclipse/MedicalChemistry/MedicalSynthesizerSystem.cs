using System.Linq;
using Content.Server.Chemistry.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Shared._Eclipse.MedicalChemistry;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Database;
using Content.Shared.FixedPoint;
using Content.Shared.Power;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.MedicalChemistry;

public sealed partial class MedicalSynthesizerSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private ChemMasterSystem _chemMaster = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ISharedAdminLogManager _logs = default!;
    [Dependency] private ItemSlotsSystem _slots = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<MedicalSynthesizerComponent, MedicalSynthesisStartMessage>(OnStart);
        SubscribeLocalEvent<MedicalSynthesizerComponent, MedicalReactorHeatMessage>(OnHeat);
        SubscribeLocalEvent<MedicalSynthesizerComponent, MedicalSynthesisCancelMessage>(OnCancel);
        SubscribeLocalEvent<MedicalSynthesizerComponent, PowerChangedEvent>(OnPower);
        SubscribeLocalEvent<MedicalSynthesizerComponent, MedicalSampleAnalyzeMessage>(OnAnalyze);
    }

    private void OnStart(Entity<MedicalSynthesizerComponent> ent, ref MedicalSynthesisStartMessage args)
    {
        if (TryStart(ent, args.Recipe, args.Protocol, args.Amount))
            _logs.Add(LogType.ChemicalReaction, LogImpact.Low,
                $"{ToPrettyString(args.Actor):user} started {args.Protocol} medical synthesis {args.Recipe} ({args.Amount}u) at {ToPrettyString(ent)}.");
        Refresh(ent);
    }

    public bool TryStart(Entity<MedicalSynthesizerComponent> ent, string recipeId, MedicalSynthesisProtocol protocol, int amount)
    {
        if (ent.Comp.ActiveRecipe != null || ent.Comp.Analyzing || ent.Comp.AnalysisOnly
            || !_power.IsPowered(ent.Owner) || string.IsNullOrWhiteSpace(recipeId)
            || !Enum.IsDefined(protocol) || amount < 1 || amount > MedicalSynthesisRules.MaxBatchVolume
            || !_prototypes.TryIndex<MedicalSynthesisPrototype>(recipeId, out var recipe)
            || !_solutions.TryGetSolution(ent.Owner, SharedChemMaster.BufferSolutionName, out var buffer, out var solution)
            || !_solutions.TryGetSolution(ent.Owner, MedicalSynthesisRules.ReactorSolution, out var reactor, out var chamber)
            || chamber.Volume > 0 || recipe.Advanced && !ent.Comp.Advanced)
            return false;

        var finished = protocol is MedicalSynthesisProtocol.Purify or MedicalSynthesisProtocol.Personalize;
        var source = finished ? recipe.Medicine : recipe.Precursor;
        if (solution.GetTotalPrototypeQuantity(source) < amount
            || protocol == MedicalSynthesisProtocol.Purify && MedicalBatch.GetPurity(solution, source) >= 0.99f
            || !MedicalBatch.TryGetPatientProfile(solution, source, out var patientDna))
            return false;

        if (protocol == MedicalSynthesisProtocol.Personalize)
        {
            if (!ent.Comp.Advanced)
                return false;
            patientDna = GetReferenceProfile(ent.Owner);
            if (patientDna == null)
                return false;
        }

        var charge = MedicalBatch.Take(solution, source, FixedPoint2.New(amount));
        // Reserve real liquid in an isolated solution. Packaging and reagent transfer cannot spend it twice.
        chamber.AddSolution(charge, _prototypes);
        ent.Comp.ActiveRecipe = recipeId;
        ent.Comp.Protocol = protocol;
        ent.Comp.PatientDna = patientDna;
        ent.Comp.Heat = MedicalReactorHeat.Automatic;
        ent.Comp.Progress = ent.Comp.Stress = 0;
        ent.Comp.Status = "medical-chemistry-status-running";
        // The legacy ChemMaster buffer is unbounded (maxVol = 0); normal overflow processing would empty it.
        Dirty(buffer.Value);
        _solutions.UpdateChemicals(reactor.Value, false);
        return true;
    }

    private void OnHeat(Entity<MedicalSynthesizerComponent> ent, ref MedicalReactorHeatMessage args)
    {
        if (ent.Comp.ActiveRecipe == null || !_power.IsPowered(ent.Owner) || !Enum.IsDefined(args.Heat))
            return;
        ent.Comp.Heat = args.Heat;
        Refresh(ent);
    }

    private void OnCancel(Entity<MedicalSynthesizerComponent> ent, ref MedicalSynthesisCancelMessage args)
    {
        Cancel(ent);
    }

    public void Cancel(Entity<MedicalSynthesizerComponent> ent)
    {
        if (ent.Comp.ActiveRecipe == null && !ent.Comp.Analyzing
            || !_solutions.TryGetSolution(ent.Owner, SharedChemMaster.BufferSolutionName, out var buffer, out var solution)
            || !_solutions.TryGetSolution(ent.Owner, MedicalSynthesisRules.ReactorSolution, out var reactor, out var chamber))
            return;
        solution.AddSolution(chamber.SplitSolution(chamber.Volume), _prototypes);
        ent.Comp.ActiveRecipe = null;
        ent.Comp.Analyzing = false;
        ent.Comp.Status = "medical-chemistry-status-cancelled";
        Dirty(buffer.Value);
        _solutions.UpdateChemicals(reactor.Value, false);
        Refresh(ent);
    }

    private void OnPower(Entity<MedicalSynthesizerComponent> ent, ref PowerChangedEvent args) => Refresh(ent);

    private void OnAnalyze(Entity<MedicalSynthesizerComponent> ent, ref MedicalSampleAnalyzeMessage args)
    {
        if (TryAnalyze(ent))
            _logs.Add(LogType.ChemicalReaction, LogImpact.Low, $"{ToPrettyString(args.Actor):user} started pharmaceutical sample analysis at {ToPrettyString(ent)}.");
        Refresh(ent);
    }

    public bool TryAnalyze(Entity<MedicalSynthesizerComponent> ent)
    {
        if (!ent.Comp.AnalysisOnly || ent.Comp.ActiveRecipe != null || ent.Comp.Analyzing || !_power.IsPowered(ent.Owner)
            || _slots.GetItemOrNull(ent.Owner, SharedChemMaster.InputSlotName) is not { } sample
            || !_solutions.TryGetFitsInDispenser(sample, out var sampleEnt, out var sampleSolution)
            || sampleSolution.Volume < 5
            || !_solutions.TryGetSolution(ent.Owner, MedicalSynthesisRules.ReactorSolution, out var reactor, out var chamber)
            || chamber.Volume > 0)
            return false;
        chamber.AddSolution(sampleSolution.SplitSolution(FixedPoint2.New(5)), _prototypes);
        ent.Comp.Analyzing = true;
        ent.Comp.PatientDna = null;
        ent.Comp.AssayResults = [];
        ent.Comp.Progress = ent.Comp.Stress = 0;
        ent.Comp.Status = "medical-chemistry-status-analyzing";
        _solutions.UpdateChemicals(sampleEnt.Value);
        _solutions.UpdateChemicals(reactor.Value, false);
        Refresh(ent);
        return true;
    }

    private string? GetReferenceProfile(EntityUid uid)
    {
        if (_slots.GetItemOrNull(uid, MedicalSynthesisRules.SampleSlot) is not { } sample
            || !_solutions.TryGetFitsInDispenser(sample, out _, out var solution))
            return null;
        return MedicalBatch.GetSampleDna(solution, true);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<MedicalSynthesizerComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.ActiveRecipe == null && !comp.Analyzing || Paused(uid) || !_power.IsPowered(uid))
                continue;

            Advance((uid, comp), frameTime);
            comp.UiAccumulator += frameTime;
            if (comp.UiAccumulator < 0.5f)
                continue;
            comp.UiAccumulator = 0;
            Refresh((uid, comp));
        }
    }

    public void Advance(Entity<MedicalSynthesizerComponent> ent, float seconds)
    {
        if (!float.IsFinite(seconds) || seconds <= 0 || !_power.IsPowered(ent.Owner))
            return;
        if (ent.Comp.Analyzing)
        {
            ent.Comp.Progress = Math.Clamp(ent.Comp.Progress + seconds / MedicalSynthesisRules.AssayDuration, 0, 1);
            if (ent.Comp.Progress >= 1)
                CompleteAnalysis(ent);
            return;
        }
        if (ent.Comp.ActiveRecipe is not { } recipeId || !_power.IsPowered(ent.Owner) || !float.IsFinite(seconds) || seconds <= 0
            || !_solutions.TryGetSolution(ent.Owner, MedicalSynthesisRules.ReactorSolution, out var reactor, out var chamber))
            return;

        var recipe = _prototypes.Index(recipeId);
        // Small fixed steps keep yield and thermal damage independent of server frame length.
        while (seconds > 0 && ent.Comp.Progress < 1)
        {
            var dt = Math.Min(seconds, 0.1f);
            (chamber.Temperature, ent.Comp.Progress, ent.Comp.Stress) = MedicalSynthesisRules.Step(
                recipe, ent.Comp.Protocol, ent.Comp.Heat, chamber.Temperature, ent.Comp.Progress, ent.Comp.Stress, dt);
            seconds -= dt;
        }

        if (ent.Comp.Progress < 1)
            return;
        Complete(ent, recipe, reactor.Value, chamber);
    }

    private void CompleteAnalysis(Entity<MedicalSynthesizerComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, MedicalSynthesisRules.ReactorSolution, out var reactor, out var chamber)
            || !_solutions.TryGetSolution(ent.Owner, SharedChemMaster.BufferSolutionName, out var buffer, out var solution))
            return;
        ent.Comp.PatientDna = MedicalBatch.Assay(chamber);
        ent.Comp.AssayResults = chamber.Contents.Select(reagent => reagent.Clone()).ToArray();
        solution.AddSolution(chamber.SplitSolution(chamber.Volume), _prototypes);
        ent.Comp.Analyzing = false;
        ent.Comp.Status = "medical-chemistry-status-assayed";
        Dirty(buffer.Value);
        _solutions.UpdateChemicals(reactor.Value, false);
        Refresh(ent);
    }

    private void Complete(Entity<MedicalSynthesizerComponent> ent, MedicalSynthesisPrototype recipe,
        Entity<SolutionComponent> reactor, Solution chamber)
    {
        if (!_solutions.TryGetSolution(ent.Owner, SharedChemMaster.BufferSolutionName, out var buffer, out var solution))
            return;

        var inputPurity = MedicalBatch.GetPurity(chamber,
            ent.Comp.Protocol is MedicalSynthesisProtocol.Purify or MedicalSynthesisProtocol.Personalize ? recipe.Medicine : recipe.Precursor);
        var (purity, yield) = MedicalSynthesisRules.Result(ent.Comp.Protocol, ent.Comp.Stress, inputPurity);
        var volume = chamber.Volume * yield;
        var output = new ReagentId(recipe.Medicine, [new MedicalBatchData { Purity = purity, PatientDna = ent.Comp.PatientDna }]);
        chamber.RemoveAllSolution();
        solution.AddReagent(output, volume);
        ent.Comp.ActiveRecipe = null;
        ent.Comp.Status = "medical-chemistry-status-complete";
        _logs.Add(LogType.ChemicalReaction, LogImpact.Low,
            $"Medical synthesis at {ToPrettyString(ent)} produced {volume}u of {recipe.Medicine} at {purity}% purity.");
        Dirty(buffer.Value);
        _solutions.UpdateChemicals(reactor, false);
        Refresh(ent);
    }

    public MedicalSynthesizerState? GetState(EntityUid uid)
    {
        if (!TryComp<MedicalSynthesizerComponent>(uid, out var comp)
            || !_solutions.TryGetSolution(uid, SharedChemMaster.BufferSolutionName, out _, out var buffer)
            || !_solutions.TryGetSolution(uid, MedicalSynthesisRules.ReactorSolution, out _, out var reactor))
            return null;

        var analysis = _prototypes.EnumeratePrototypes<MedicalSynthesisPrototype>().Where(recipe => comp.Advanced || !recipe.Advanced).OrderBy(recipe => recipe.ID)
            .Select(recipe => new MedicalSynthesisAnalysis(recipe.ID,
                buffer.GetTotalPrototypeQuantity(recipe.Precursor), buffer.GetTotalPrototypeQuantity(recipe.Medicine),
                MedicalBatch.GetPurity(buffer, recipe.Medicine), MedicalBatch.GetPurity(buffer, recipe.Precursor),
                !MedicalBatch.TryGetPatientProfile(buffer, recipe.Medicine, out _))).ToArray();
        return new MedicalSynthesizerState(_power.IsPowered(uid), comp.ActiveRecipe?.Id, comp.Protocol, comp.Heat,
            reactor.Temperature, comp.Progress, comp.Stress, reactor.Volume, comp.Status, analysis,
            comp.Advanced, comp.AnalysisOnly, comp.Analyzing,
            comp.AnalysisOnly ? comp.PatientDna : comp.Advanced ? GetReferenceProfile(uid) : null, comp.AssayResults);
    }

    private void Refresh(Entity<MedicalSynthesizerComponent> ent)
    {
        _appearance.SetData(ent, MedicalReactorVisuals.Working,
            (ent.Comp.ActiveRecipe != null || ent.Comp.Analyzing) && _power.IsPowered(ent.Owner));
        if (TryComp<Content.Server.Chemistry.Components.ChemMasterComponent>(ent, out var chemMaster))
            _chemMaster.UpdateUiState((ent.Owner, chemMaster));
    }
}
