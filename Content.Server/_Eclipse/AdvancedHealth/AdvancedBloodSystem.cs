using System.Linq;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Eclipse.AdvancedHealth;

/// <summary>Identified samples, bedside crossmatching, donor collection and actual transfusion physiology.</summary>
public sealed class AdvancedBloodSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly AdvancedOrganHealthSystem _organs = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BloodProductComponent, ComponentInit>(OnProductInit);
        SubscribeLocalEvent<AdvancedHealthComponent, AdvancedBloodTransferEvent>(OnBloodTransfer);
        SubscribeLocalEvent<BloodProductComponent, ExaminedEvent>(OnProductExamined);
        SubscribeLocalEvent<ClinicalBloodSampleComponent, ExaminedEvent>(OnSampleExamined);
        SubscribeLocalEvent<EmptyBloodBagComponent, AfterInteractEvent>(OnCollect);
        SubscribeLocalEvent<ClinicalBloodSampleComponent, AfterInteractEvent>(OnCrossmatch);
        SubscribeLocalEvent<BloodProductComponent, AfterInteractEvent>(OnProductUse);
        SubscribeLocalEvent<AdvancedHealthComponent, ClinicalBloodDoAfterEvent>(OnCollectionFinished);
        SubscribeLocalEvent<BloodProductComponent, ClinicalBloodDoAfterEvent>(OnCrossmatchFinished);
        SubscribeLocalEvent<AdvancedHealthComponent, ClinicalTransfusionDoAfterEvent>(OnTransfusionFinished);
    }

    private void OnProductInit(Entity<BloodProductComponent> ent, ref ComponentInit args)
    {
        if (BloodCompatibility.TryParse(ent.Comp.BloodType, out var group, out var rh))
        {
            ent.Comp.BloodGroup = group;
            ent.Comp.RhPositive = rh;
        }
    }

    private bool CanUse(EntityUid user, EntityUid tool, EntityUid target)
        => Exists(tool) && Exists(target) && _hands.IsHolding(user, tool)
            && _blocker.CanConsciouslyPerformAction(user) && _blocker.CanInteract(user, target)
            && _interaction.InRangeUnobstructed(user, target);

    /// <summary>Used by both in-world interactions and the chip; client requests never apply a transfusion directly.</summary>
    public bool BeginTransfusion(EntityUid user, EntityUid pack, EntityUid target)
        => CanUse(user, pack, target) && TryComp<AdvancedHealthComponent>(target, out var health) && health.HasBlood
            && TryComp<BloodProductComponent>(pack, out var product) && product.Charges > 0
            && _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, 6,
                new ClinicalTransfusionDoAfterEvent(), target, target: target, used: pack)
            { NeedHand = true, BreakOnMove = true, BreakOnDamage = true });

    private void OnProductUse(Entity<BloodProductComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.Handled && args.CanReach && args.Target is { } target)
            args.Handled = BeginTransfusion(args.User, ent, target);
    }

    private void OnTransfusionFinished(Entity<AdvancedHealthComponent> ent, ref ClinicalTransfusionDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target != ent.Owner || args.Used is not { } pack
            || !CanUse(args.User, pack, ent)) return;
        args.Handled = Transfuse(ent, pack);
    }

    public bool Transfuse(EntityUid patient, EntityUid pack)
    {
        if (!TryComp<AdvancedHealthComponent>(patient, out var health) || !health.HasBlood
            || !TryComp<BloodProductComponent>(pack, out var product) || product.Charges <= 0
            || !float.IsFinite(product.Volume) || product.Volume <= 0
            || !float.IsFinite(product.OxygenCarryFactor) || product.OxygenCarryFactor < 0) return false;
        Receive(patient, health, product);
        if (product.ProductType is BloodProductType.WholeBlood or BloodProductType.PackedCells)
            foreach (var id in product.BloodborneConditions)
                if (_prototypes.TryIndex<AdvancedDiseasePrototype>(id, out var disease) && disease.Bloodborne)
                    EntityManager.System<AdvancedDiseaseSystem>().ApplyCondition(patient, id, 0.15f);
        product.Charges--;
        Dirty(pack, product);
        if (product.Charges <= 0) QueueDel(pack);
        Dirty(patient, health);
        return true;
    }

    private void Receive(EntityUid patient, AdvancedHealthComponent health, BloodProductComponent product)
    {
        var oldVolume = health.BloodVolume;
        var newVolume = Math.Min(health.MaxBloodVolume, oldVolume + product.Volume);
        var factor = BloodCompatibility.OxygenFactor(_prototypes, health.BodyFluid, health.BloodType, product);
        if (factor == null)
        {
            var exposure = Math.Clamp(product.Volume / 500f, 0, 2);
            health.TransfusionReaction = Math.Clamp(health.TransfusionReaction + 0.6f * exposure, 0f, 1f);
            health.OxygenCarryingCapacity = Math.Max(0.1f, health.OxygenCarryingCapacity - 0.2f * exposure);
            health.ToxinLoad = Math.Min(100, health.ToxinLoad + 10 * exposure);
            health.BloodVolume = Math.Min(health.MaxBloodVolume, oldVolume + product.Volume * 0.5f);
            _popup.PopupEntity(Loc.GetString(health.IncompatibleTransfusionPopup), patient, patient, PopupType.LargeCaution);
        }
        else
        {
            var carry = factor.Value * product.OxygenCarryFactor * (product.ProductType == BloodProductType.PackedCells ? 1.2f : 1f);
            if (newVolume > 0)
                health.OxygenCarryingCapacity = Math.Clamp((health.OxygenCarryingCapacity * oldVolume
                    + carry * (newVolume - oldVolume)) / newVolume, 0f, 1f);
            health.BloodVolume = newVolume;
            _popup.PopupEntity(Loc.GetString(factor == 0 ? "advanced-health-transfusion-fluid" : "advanced-health-transfusion-ok"), patient, patient);
        }
        Dirty(patient, health);
    }

    private void OnBloodTransfer(Entity<AdvancedHealthComponent> ent, ref AdvancedBloodTransferEvent args)
    {
        if (!ent.Comp.HasBlood || !TryComp<BloodstreamComponent>(ent, out var stream)
            || stream.BloodReferenceSolution.Volume <= 0) return;
        foreach (var reagent in args.Solution.Contents.ToArray())
        {
            var data = reagent.Reagent.Data?.OfType<ClinicalBloodData>().FirstOrDefault();
            if (data == null || reagent.Quantity <= 0) continue;
            var volume = reagent.Quantity.Float() / (float) stream.BloodReferenceSolution.Volume * ent.Comp.MaxBloodVolume;
            if (args.Drawn)
            {
                if (data.PatientIdentity == ent.Comp.MedicalIdentity)
                {
                    var updated = reagent.Reagent.Data!.Where(value => value is not ClinicalBloodData).Select(value => value.Clone()).ToList();
                    updated.Add(new ClinicalBloodData { BodyFluid = ent.Comp.BodyFluid, BloodType = ent.Comp.BloodType,
                        PatientIdentity = ent.Comp.MedicalIdentity, OxygenCarryFactor = ent.Comp.OxygenCarryingCapacity });
                    args.Solution.RemoveReagent(reagent);
                    args.Solution.AddReagent(new ReagentId(reagent.Reagent.Prototype, updated), reagent.Quantity);
                }
                ent.Comp.BloodVolume = Math.Max(0, ent.Comp.BloodVolume - volume);
            }
            else
            {
                Receive(ent, ent.Comp, new BloodProductComponent
                { BodyFluid = data.BodyFluid, BloodType = data.BloodType, Volume = volume, OxygenCarryFactor = data.OxygenCarryFactor });
                // Physiology consumed these red cells. Do not also metabolize foreign blood as a generic poison.
                if (_solutions.TryGetSolution(ent.Owner, stream.BloodSolutionName, out var blood, out _))
                    _solutions.RemoveReagent(blood.Value, reagent.Reagent, reagent.Quantity);
            }
        }
        Dirty(ent);
    }

    public bool CanSample(EntityUid patient)
        => TryComp<AdvancedHealthComponent>(patient, out var health) && health.HasBlood
            && TryComp<BloodstreamComponent>(patient, out var stream) && stream.BloodReferenceSolution.Volume > 0
            && health.BloodVolume >= 5f / (float) stream.BloodReferenceSolution.Volume * health.MaxBloodVolume;

    /// <summary>Completed blood testing consumes a real sample volume and produces a dated physical profile.</summary>
    public EntityUid? Sample(EntityUid patient)
    {
        if (!CanSample(patient)) return null;
        var health = Comp<AdvancedHealthComponent>(patient);
        var source = Comp<BloodstreamComponent>(patient);
        var sampleVolume = 5f / (float) source.BloodReferenceSolution.Volume * health.MaxBloodVolume;
        if (health.BloodVolume < sampleVolume) return null;
        health.BloodVolume -= sampleVolume;
        health.BloodTypeConfirmed = true;
        var tube = Spawn("AdvancedClinicalBloodSample", Transform(patient).Coordinates);
        var sample = Comp<ClinicalBloodSampleComponent>(tube);
        sample.Patient = patient;
        sample.PatientIdentity = health.MedicalIdentity;
        sample.PatientName = Name(patient);
        sample.BodyFluid = health.BodyFluid;
        sample.BloodType = health.BloodType;
        sample.CollectedAt = _timing.CurTime;
        sample.Glucose = health.BloodGlucose;
        sample.OxygenCarryingCapacity = health.OxygenCarryingCapacity;
        sample.ToxinLoad = health.ToxinLoad;
        sample.KidneyFunction = _organs.Function(patient, "Kidneys");
        sample.LiverFunction = _organs.Function(patient, "Liver");
        if (TryComp<Content.Shared.Humanoid.HumanoidProfileComponent>(patient, out var appearance)
            && appearance.Species.Id == "Human")
        {
            var inflammation = 0f;
            if (TryComp<AdvancedDiseaseComponent>(patient, out var clinical))
                foreach (var id in clinical.Active.Keys)
                    if (_prototypes.TryIndex<AdvancedDiseasePrototype>(id, out var disease)
                        && disease.Cause is ClinicalCause.Bacterial or ClinicalCause.Fungal or ClinicalCause.Viral or ClinicalCause.Inflammatory)
                        inflammation = Math.Max(inflammation, clinical.Severity.GetValueOrDefault(id));
            var dehydration = TryComp<AdvancedNutritionComponent>(patient, out var nutrition) ? nutrition.DehydrationStrain : 0;
            sample.LaboratoryReadings = ClinicalLaboratoryModel.Calculate(health, sample.KidneyFunction,
                sample.LiverFunction, dehydration, inflammation);
        }
        if (_solutions.TryGetSolution(tube, "drink", out var tubeSolution, out _))
        {
            var blood = new Solution();
            var data = EntityManager.System<SharedBloodstreamSystem>().GetEntityBloodData(patient);
            foreach (var reagent in source.BloodReferenceSolution)
                blood.AddReagent(new ReagentId(reagent.Reagent.Prototype, data),
                    reagent.Quantity * 5 / source.BloodReferenceSolution.Volume);
            _solutions.TryAddSolution(tubeSolution.Value, blood);
        }
        if (TryComp<BloodstreamComponent>(patient, out var bloodstream))
            foreach (var solutionName in new[] { bloodstream.BloodSolutionName, bloodstream.MetabolitesSolutionName })
                if (_solutions.TryGetSolution(patient, solutionName, out _, out var solution))
                    foreach (var profile in _prototypes.EnumeratePrototypes<ClinicalMedicationPrototype>())
                    {
                        var quantity = solution.GetTotalPrototypeQuantity(profile.Reagent).Float();
                        if (quantity > 0) sample.Medications[profile.Reagent] = sample.Medications.GetValueOrDefault(profile.Reagent) + quantity;
                    }
        Dirty(tube, sample);
        Dirty(patient, health);
        return tube;
    }

    public bool IsFresh(ClinicalBloodSampleComponent sample)
        => sample.PatientIdentity != "" && sample.CollectedAt <= _timing.CurTime
            && _timing.CurTime - sample.CollectedAt <= TimeSpan.FromMinutes(30)
            && _solutions.TryGetSolution(sample.Owner, "drink", out _, out var contents)
            && contents.Contents.Any(reagent => reagent.Quantity > 0 && reagent.Reagent.Data?.OfType<ClinicalBloodData>().Any() == true)
            && contents.Contents.Where(reagent => reagent.Quantity > 0).SelectMany(reagent => reagent.Reagent.Data ?? [])
                .OfType<ClinicalBloodData>().All(data => data.PatientIdentity == sample.PatientIdentity && data.BodyFluid == sample.BodyFluid
                    && data.BloodType == sample.BloodType)
            && contents.Contents.SelectMany(reagent => reagent.Reagent.Data ?? []).OfType<DnaData>()
                .Select(data => data.DNA).Distinct().Count() <= 1;

    private void OnCollect(Entity<EmptyBloodBagComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !CanUse(args.User, ent, target)
            || !CanDonate(target)) return;
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 8,
            new ClinicalBloodDoAfterEvent(), target, target: target, used: ent)
        { NeedHand = true, BreakOnMove = true, BreakOnDamage = true });
    }

    private bool CanDonate(EntityUid patient) => TryComp<AdvancedHealthComponent>(patient, out var health)
        && health.HasBlood && health.BloodVolume - 250 >= health.MaxBloodVolume * 0.7f;

    public bool Collect(EntityUid patient, EntityUid bag)
    {
        if (!HasComp<EmptyBloodBagComponent>(bag) || !CanDonate(patient)) return false;
        var health = Comp<AdvancedHealthComponent>(patient);
        RemComp<EmptyBloodBagComponent>(bag);
        var product = EnsureComp<BloodProductComponent>(bag);
        product.ProductType = BloodProductType.WholeBlood;
        product.BodyFluid = health.BodyFluid; product.BloodType = health.BloodType;
        product.BloodGroup = health.BloodGroup; product.RhPositive = health.RhPositive;
        product.FluidColor = health.FluidColor; product.OxygenCarryFactor = health.OxygenCarryingCapacity;
        product.Volume = 250; product.Charges = 1; product.DonorName = Name(patient);
        if (TryComp<AdvancedDiseaseComponent>(patient, out var diseases))
            product.BloodborneConditions = diseases.Active.Keys.Where(id => _prototypes.Index<AdvancedDiseasePrototype>(id).Bloodborne).ToList();
        health.BloodVolume -= product.Volume;
        Dirty(bag, product); Dirty(patient, health);
        return true;
    }

    private void OnCollectionFinished(Entity<AdvancedHealthComponent> ent, ref ClinicalBloodDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Crossmatch || args.Target != ent.Owner || args.Used is not { } bag
            || !CanUse(args.User, bag, ent)) return;
        args.Handled = Collect(ent, bag);
    }

    private void OnCrossmatch(Entity<ClinicalBloodSampleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } pack || !HasComp<BloodProductComponent>(pack)
            || !CanUse(args.User, ent, pack) || !IsFresh(ent.Comp)) return;
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 5,
            new ClinicalBloodDoAfterEvent { Crossmatch = true }, pack, target: pack, used: ent)
        { NeedHand = true, BreakOnMove = true, BreakOnDamage = true });
    }

    public bool Crossmatch(EntityUid tube, EntityUid pack)
    {
        if (!TryComp<ClinicalBloodSampleComponent>(tube, out var sample) || !IsFresh(sample)
            || !TryComp<BloodProductComponent>(pack, out var product) || product.Charges <= 0) return false;
        product.CrossmatchedIdentity = sample.PatientIdentity;
        product.CrossmatchedName = sample.PatientName;
        product.CrossmatchCompatible = BloodCompatibility.OxygenFactor(_prototypes, sample.BodyFluid, sample.BloodType, product) != null;
        Dirty(pack, product);
        return true;
    }

    private void OnCrossmatchFinished(Entity<BloodProductComponent> ent, ref ClinicalBloodDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || !args.Crossmatch || args.Target != ent.Owner || args.Used is not { } tube
            || !CanUse(args.User, tube, ent)) return;
        args.Handled = Crossmatch(tube, ent);
        if (args.Handled) _popup.PopupEntity(Loc.GetString(ent.Comp.CrossmatchCompatible
            ? "clinical-crossmatch-ok" : "clinical-crossmatch-incompatible", ("patient", ent.Comp.CrossmatchedName)), ent, args.User);
    }

    private void OnProductExamined(Entity<BloodProductComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("clinical-blood-pack-label", ("group", BloodCompatibility.Format(ent.Comp.BodyFluid, ent.Comp.BloodType)),
            ("volume", ent.Comp.Volume), ("product", Loc.GetString($"clinical-product-{ent.Comp.ProductType.ToString().ToLowerInvariant()}"))));
        if (ent.Comp.DonorName != "") args.PushMarkup(Loc.GetString("clinical-blood-donor", ("patient", ent.Comp.DonorName)));
        if (ent.Comp.CrossmatchedIdentity != "") args.PushMarkup(Loc.GetString(ent.Comp.CrossmatchCompatible
            ? "clinical-crossmatch-ok" : "clinical-crossmatch-incompatible", ("patient", ent.Comp.CrossmatchedName)));
    }

    private void OnSampleExamined(Entity<ClinicalBloodSampleComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("clinical-sample-label", ("patient", ent.Comp.PatientName),
            ("group", BloodCompatibility.Format(ent.Comp.BodyFluid, ent.Comp.BloodType)), ("glucose", MathF.Round(ent.Comp.Glucose, 1)),
            ("carry", MathF.Round(ent.Comp.OxygenCarryingCapacity * 100)), ("toxins", MathF.Round(ent.Comp.ToxinLoad, 1))));
        args.PushMarkup(Loc.GetString(IsFresh(ent.Comp) ? "clinical-sample-fresh" : "clinical-sample-expired"));
        args.PushMarkup(Loc.GetString("clinical-sample-organs", ("kidneys", MathF.Round(ent.Comp.KidneyFunction * 100)),
            ("liver", MathF.Round(ent.Comp.LiverFunction * 100))));
        foreach (var (drug, quantity) in ent.Comp.Medications)
            args.PushMarkup(Loc.GetString("clinical-sample-drug", ("drug", _prototypes.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>(drug).LocalizedName),
                ("quantity", MathF.Round(quantity, 1))));
    }
}
