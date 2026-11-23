using System.Linq;
using System.Collections.Generic;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.MedicalChemistry;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Forensics.Components;
using Content.Shared.Rejuvenate;
using Content.Shared.Hands.EntitySystems;
using Content.Server._Eclipse.MedicalChemistry;
using Content.Server.Power.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Chemistry;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class ClinicalBloodTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task CollectedBloodIsAcceptedByTheActualPharmaceuticalAnalyzer()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var tube = SEntMan.System<AdvancedBloodSystem>().Sample(patient)!.Value;
            var analyzer = SEntMan.SpawnEntity("PharmaceuticalAnalyzer", map.GridCoords);
            SEntMan.GetComponent<ApcPowerReceiverComponent>(analyzer).Powered = true;
            Assert.That(SEntMan.System<ItemSlotsSystem>().TryInsert(analyzer, SharedChemMaster.InputSlotName, tube, null), Is.True);
            var system = SEntMan.System<MedicalSynthesizerSystem>();
            Entity<MedicalSynthesizerComponent> machine = (analyzer, SEntMan.GetComponent<MedicalSynthesizerComponent>(analyzer));
            Assert.That(system.TryAnalyze(machine), Is.True);
            system.Advance(machine, 10);
            Assert.That(SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(analyzer, SharedChemMaster.BufferSolutionName, out _, out var assayed), Is.True);
            Assert.That(MedicalBatch.GetSampleDna(assayed!, true), Is.EqualTo(SEntMan.GetComponent<DnaComponent>(patient).DNA));
            Assert.That(assayed!.Contents.SelectMany(reagent => reagent.Reagent.Data ?? []).OfType<ClinicalBloodData>()
                .All(data => data.PatientIdentity == SEntMan.GetComponent<AdvancedHealthComponent>(patient).MedicalIdentity), Is.True);
            Assert.That(SEntMan.System<AdvancedBloodSystem>().IsFresh(SEntMan.GetComponent<ClinicalBloodSampleComponent>(tube)), Is.False,
                "An emptied reference tube must not keep passing crossmatches after the laboratory consumes its contents.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task TransfusionRequiresTheHeldPackAndDoesNotConsumeAnInterruptedAction()
    {
        var map = await Pair.CreateTestMap();
        EntityUid doctor = default, patient = default, pack = default;
        await Server.WaitAssertion(() =>
        {
            doctor = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            pack = SEntMan.SpawnEntity("EclipsePackedCellsONeg", map.MapCoords);
            SEntMan.GetComponent<AdvancedHealthComponent>(patient).BloodVolume -= 1000;
            var system = SEntMan.System<AdvancedBloodSystem>();
            Assert.That(system.BeginTransfusion(doctor, pack, patient), Is.False);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(doctor, pack), Is.True);
            Assert.That(system.BeginTransfusion(doctor, pack, patient), Is.True);
            Assert.That(SEntMan.GetComponent<BloodProductComponent>(pack).Charges, Is.EqualTo(1));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryDrop(doctor, pack), Is.True);
        });
        await Pair.RunTicksSync(450);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(pack), Is.False);
            Assert.That(SEntMan.GetComponent<BloodProductComponent>(pack).Charges, Is.EqualTo(1));
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(doctor, pack), Is.True);
            Assert.That(SEntMan.System<AdvancedBloodSystem>().BeginTransfusion(doctor, pack, patient), Is.True);
        });
        await Pair.RunTicksSync(450);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.Deleted(pack), Is.True);
            Assert.That(SEntMan.GetComponent<AdvancedHealthComponent>(patient).TransfusionReaction, Is.Zero);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task BloodTypeAndDnaRemainConsistentAcrossRealSamplingAndRejuvenation()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            Assert.That(BloodCompatibility.TryParse(health.BloodType, out var group, out var rh), Is.True);
            Assert.That(health.BloodGroup, Is.EqualTo(group));
            Assert.That(health.RhPositive, Is.EqualTo(rh));
            var id = health.MedicalIdentity;
            var type = health.BloodType;
            var stream = SEntMan.GetComponent<BloodstreamComponent>(patient);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(patient, stream.BloodSolutionName, out _, out var blood), Is.True);
            var typing = blood!.Contents.SelectMany(reagent => reagent.Reagent.Data ?? []).OfType<ClinicalBloodData>().First();
            Assert.That(typing.PatientIdentity, Is.EqualTo(id));
            Assert.That(typing.BloodType, Is.EqualTo(type));
            var system = SEntMan.System<AdvancedBloodSystem>();
            var tube = system.Sample(patient)!.Value;
            Assert.That(health.BloodTypeConfirmed, Is.True);
            Assert.That(solutions.TryGetSolution(tube, "drink", out _, out var sample), Is.True);
            Assert.That(sample!.Volume.Float(), Is.EqualTo(5).Within(0.001));
            var dna = SEntMan.GetComponent<DnaComponent>(patient).DNA;
            Assert.That(MedicalBatch.GetSampleDna(sample, false), Is.EqualTo(dna));
            Assert.That(MedicalBatch.GetSampleDna(sample, true), Is.Null, "Collection does not bypass the pharmaceutical assay.");
            Assert.That(MedicalBatch.Assay(sample), Is.EqualTo(dna));
            Assert.That(MedicalBatch.GetSampleDna(sample, true), Is.EqualTo(dna));
            Assert.That(system.IsFresh(SEntMan.GetComponent<ClinicalBloodSampleComponent>(tube)), Is.True);
            var rejuvenate = new RejuvenateEvent();
            SEntMan.EventBus.RaiseLocalEvent(patient, rejuvenate);
            Assert.That(health.MedicalIdentity, Is.EqualTo(id));
            Assert.That(health.BloodType, Is.EqualTo(type));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task AllHumanGroupsDistinguishWholeBloodFromPackedCellsAndRejectForeignFluids()
    {
        await Server.WaitAssertion(() =>
        {
            var prototypes = Server.ResolveDependency<IPrototypeManager>();
            foreach (var anatomy in prototypes.EnumeratePrototypes<AdvancedHealthAnatomyPrototype>())
                if (anatomy.HasBlood)
                    foreach (var type in anatomy.BloodTypes)
                        Assert.That(prototypes.HasIndex<AdvancedHealthBloodCompatibilityPrototype>(type), Is.True,
                            $"Missing compatibility table for {anatomy.ID}: {type}");
            var choices = new Dictionary<string, string[]>
            {
                ["O-"] = ["O-"], ["O+"] = ["O-", "O+"],
                ["A-"] = ["O-", "A-"], ["A+"] = ["O-", "O+", "A-", "A+"],
                ["B-"] = ["O-", "B-"], ["B+"] = ["O-", "O+", "B-", "B+"],
                ["AB-"] = ["O-", "A-", "B-", "AB-"],
                ["AB+"] = ["O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+"],
            };
            foreach (var (recipient, donors) in choices)
                foreach (var donor in choices.Keys)
                {
                    var product = new BloodProductComponent { BloodType = donor, ProductType = BloodProductType.PackedCells };
                    Assert.That(BloodCompatibility.OxygenFactor(prototypes, "Blood", recipient, product) != null,
                        Is.EqualTo(donors.Contains(donor)), $"Packed cells {donor} -> {recipient}");
                    product.ProductType = BloodProductType.WholeBlood;
                    Assert.That(BloodCompatibility.OxygenFactor(prototypes, "Blood", recipient, product) != null,
                        Is.EqualTo(donors.Contains(donor) && recipient[..^1] == donor[..^1]), $"Whole blood {donor} -> {recipient}");
                    Assert.That(BloodCompatibility.OxygenFactor(prototypes, "Hemolymph", recipient, product), Is.Null);
                }
            Assert.That(BloodCompatibility.TryParse("M-A", out _, out _), Is.False);
            Assert.That(BloodCompatibility.TryParse("A?", out _, out _), Is.False);
        });
    }

    [Test]
    public async Task DonationKeepsTheDonorsGroupAnemiaAndBloodborneInfection()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var donor = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var recipient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(donor);
            var receiver = SEntMan.GetComponent<AdvancedHealthComponent>(recipient);
            receiver.BloodType = health.BloodType;
            receiver.BloodVolume -= 500;
            health.OxygenCarryingCapacity = 0.4f;
            SEntMan.System<AdvancedDiseaseSystem>().ApplyCondition(donor, "ClinicalHepatitis");
            var bag = SEntMan.SpawnEntity("AdvancedEmptyBloodBag", map.MapCoords);
            var system = SEntMan.System<AdvancedBloodSystem>();
            Assert.That(system.Collect(donor, bag), Is.True);
            Assert.That(health.BloodVolume, Is.EqualTo(health.MaxBloodVolume - 250));
            var product = SEntMan.GetComponent<BloodProductComponent>(bag);
            Assert.That(product.BloodType, Is.EqualTo(health.BloodType));
            Assert.That(product.OxygenCarryFactor, Is.EqualTo(0.4f));
            Assert.That(product.BloodborneConditions, Does.Contain("ClinicalHepatitis"));
            Assert.That(system.Collect(donor, bag), Is.False, "A filled bag cannot collect twice.");
            Assert.That(system.Transfuse(recipient, bag), Is.True);
            Assert.That(receiver.TransfusionReaction, Is.Zero);
            Assert.That(SEntMan.GetComponent<AdvancedDiseaseComponent>(recipient).Active, Does.ContainKey("ClinicalHepatitis"));
            Assert.That(receiver.OxygenCarryingCapacity, Is.LessThan(1));
            Assert.That(system.Transfuse(recipient, bag), Is.False, "A consumed pack cannot be replayed.");
            var empty = SEntMan.SpawnEntity("AdvancedEmptyBloodBag", map.MapCoords);
            health.BloodVolume = health.MaxBloodVolume * 0.7f;
            Assert.That(system.Collect(donor, empty), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task CrossmatchUsesAnIdentifiedFreshPhysicalSampleAndRejectsMixedOrEmptyTubes()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var other = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            var system = SEntMan.System<AdvancedBloodSystem>();
            var tube = system.Sample(patient)!.Value;
            var pack = SEntMan.SpawnEntity("EclipsePackedCellsONeg", map.MapCoords);
            Assert.That(system.Crossmatch(tube, pack), Is.True);
            var product = SEntMan.GetComponent<BloodProductComponent>(pack);
            Assert.That(product.CrossmatchedIdentity, Is.EqualTo(health.MedicalIdentity));
            Assert.That(product.CrossmatchedIdentity, Is.Not.EqualTo(SEntMan.GetComponent<AdvancedHealthComponent>(other).MedicalIdentity));
            Assert.That(product.CrossmatchCompatible, Is.True);
            var sample = SEntMan.GetComponent<ClinicalBloodSampleComponent>(tube);
            sample.CollectedAt -= TimeSpan.FromMinutes(31);
            Assert.That(system.Crossmatch(tube, pack), Is.False);
            sample.CollectedAt += TimeSpan.FromMinutes(31);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(tube, "drink", out var contents, out _);
            solutions.TryAddSolution(contents!.Value, new Solution([new(new ReagentId("Blood",
                [new ClinicalBloodData { PatientIdentity = "somebody-else", BloodType = "O-", BodyFluid = "Blood" }]), 1)]));
            Assert.That(system.Crossmatch(tube, pack), Is.False);
            solutions.SplitSolution(contents.Value, 30);
            Assert.That(system.Crossmatch(tube, pack), Is.False);
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task WrongFluidCausesAnOngoingReactionWhileSalineOnlyDilutesCarriers()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            health.BloodType = "A+";
            health.BloodVolume = 3000;
            var system = SEntMan.System<AdvancedBloodSystem>();
            var wrong = SEntMan.SpawnEntity("EclipseBloodPackAPos", map.MapCoords);
            SEntMan.GetComponent<BloodProductComponent>(wrong).BodyFluid = "Hemolymph";
            Assert.That(system.Transfuse(patient, wrong), Is.True);
            var carry = health.OxygenCarryingCapacity;
            Assert.That(health.TransfusionReaction, Is.GreaterThan(0));
            SEntMan.System<AdvancedDiseaseSystem>().Advance((patient, health), 10);
            Assert.That(health.ClinicalShock, Is.GreaterThan(0));
            Assert.That(health.ClinicalFever, Is.GreaterThan(0));
            Assert.That(health.OxygenCarryingCapacity, Is.LessThan(carry));
            var reaction = health.TransfusionReaction;
            carry = health.OxygenCarryingCapacity;
            Assert.That(system.Transfuse(patient, SEntMan.SpawnEntity("EclipseSalinePack", map.MapCoords)), Is.True);
            Assert.That(health.TransfusionReaction, Is.EqualTo(reaction));
            Assert.That(health.OxygenCarryingCapacity, Is.LessThan(carry));
            SEntMan.DeleteEntity(map.MapUid);
        });
    }

    [Test]
    public async Task SyringeBloodKeepsTypingAndCannotBypassTransfusionCompatibility()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(patient);
            health.BloodType = "A-";
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            var stream = SEntMan.GetComponent<BloodstreamComponent>(patient);
            solutions.TryGetSolution(patient, stream.BloodSolutionName, out var reservoir, out _);
            var withdrawn = solutions.SplitSolution(reservoir!.Value, 5);
            var before = health.BloodVolume;
            var draw = new AdvancedBloodTransferEvent(withdrawn, true);
            SEntMan.EventBus.RaiseLocalEvent(patient, ref draw);
            Assert.That(health.BloodVolume, Is.LessThan(before));
            var injected = new Solution([new(new ReagentId("Blood", [new ClinicalBloodData
                { BodyFluid = "Blood", BloodType = "B+", PatientIdentity = "incompatible-donor" }]), 5)]);
            Assert.That(SEntMan.System<SharedBloodstreamSystem>().TryAddToBloodstream(patient, injected), Is.True);
            Assert.That(health.TransfusionReaction, Is.GreaterThan(0));
            Assert.That(solutions.TryGetSolution(patient, stream.BloodSolutionName, out _, out var contents), Is.True);
            Assert.That(contents!.Contents.SelectMany(reagent => reagent.Reagent.Data ?? []).OfType<ClinicalBloodData>()
                .Any(data => data.PatientIdentity == "incompatible-donor"), Is.False, "Foreign cells must not also run generic poison metabolism.");
            SEntMan.DeleteEntity(map.MapUid);
        });
    }
}
