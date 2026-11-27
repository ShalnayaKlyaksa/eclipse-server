#nullable enable annotations

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.Client.Lobby;
using Content.IntegrationTests.Fixtures;
using Content.Server._Eclipse.Species;
using Content.Server.Body.Systems;
using Content.Server.Preferences.Managers;
using Content.Server.Traits;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.Species;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Atmos;
using Content.Shared.Body;
using Content.Shared.Corvax.TTS;
using Content.Shared.Damage.Components;
using Content.Shared.DisplacementMap;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Speech.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Traits;
using Content.Shared.Traits.Assorted;
using Robust.Client.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Humanoid;

[TestFixture]
[TestOf(typeof(HumanoidProfileSystem))]
public sealed class HumanoidProfileTests : GameTest
{
    private static readonly ProtoId<SpeciesPrototype> Vox = "Vox";
    private static readonly ProtoId<SpeciesPrototype> Vulpkanin = "Vulpkanin";

    [Test]
    public async Task LavriteThermalBurstPushesOnlyInTheChosenDirection()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var origin = map.GridCoords;
            var lavrite = SSpawnAtPosition("MobLavrite", origin);
            var target = SSpawnAtPosition("MobImperial", origin.Offset(Vector2.UnitX));
            var behind = SSpawnAtPosition("MobImperial", origin.Offset(-Vector2.UnitX));
            var actions = SEntMan.System<SharedActionsSystem>();
            var action = actions.GetActions(lavrite).Single(entry =>
                SComp<MetaDataComponent>(entry.Owner).EntityPrototype?.ID == "ActionLavriteThermalBurst");
            var burst = new LavriteThermalBurstEvent { Target = origin.Offset(Vector2.UnitX * 3f) };
            actions.PerformAction(lavrite, action, burst, predicted: false);
            Assert.That(burst.Handled, Is.True);
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(target), Is.True);
            Assert.That(SEntMan.HasComponent<ThrownItemComponent>(behind), Is.False);
        });
    }

    [Test]
    public async Task SlimeSpitFliesAndOnlyAppliesSlownessOnImpact()
    {
        var map = await Pair.CreateTestMap();
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            var origin = map.GridCoords.Offset(new Vector2(0.5f, 0.5f));
            var slime = SSpawnAtPosition("MobSlimefolk", origin);
            target = SSpawnAtPosition("MobImperial", origin.Offset(Vector2.UnitX * 1.3f));
            var actions = SEntMan.System<SharedActionsSystem>();
            var action = actions.GetActions(slime).Single(entry =>
                SComp<MetaDataComponent>(entry.Owner).EntityPrototype?.ID == "ActionSlimeAdhesive");
            var spit = new RacialTargetActionEvent { Ability = RacialTargetAbility.SlimeAdhesive, Target = target };
            actions.PerformAction(slime, action, spit, predicted: false);
            Assert.That(spit.Handled, Is.True);
            var status = SEntMan.System<StatusEffectsSystem>();
            Assert.That(status.TryGetStatusEffect(target, "StatusEffectSlimeAdhesive", out _), Is.False);
        });
        await Pair.RunTicksSync(24);
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.System<StatusEffectsSystem>().TryGetStatusEffect(target, "StatusEffectSlimeAdhesive", out _), Is.True));
    }

    [Test]
    public async Task ElirPulseDetectsHeartbeatsThroughWallsAndElirHasPointedEars()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var origin = map.GridCoords;
            var elir = SSpawnAtPosition("MobElir", origin);
            SSpawnAtPosition("WallSolid", origin.Offset(Vector2.UnitX));
            var target = SSpawnAtPosition("MobImperial", origin.Offset(Vector2.UnitX * 2f));
            var health = SComp<AdvancedHealthComponent>(target);
            health.HeartRate = 93f;
            var racial = SEntMan.System<RacialAbilitySystem>();
            var position = SEntMan.System<SharedTransformSystem>().GetMapCoordinates(target);
            Assert.That(racial.CollectPulseSamples(elir), Is.EqualTo(new[] { new ElirPulseSample(position, 93f) }));
            health.IsHeartStopped = true;
            Assert.That(racial.CollectPulseSamples(elir), Is.Empty);

            var appearance = HumanoidCharacterAppearance.DefaultWithSpecies("Elir", Sex.Female);
            Assert.That(appearance.Markings["Head"][HumanoidVisualLayers.HeadSide].Single().MarkingId.Id,
                Is.EqualTo("AnthropomorphHeadSideElf"));
            var organs = SComp<InitialBodyComponent>(elir).Organs;
            Assert.That(organs.ContainsKey("Torso"), Is.True);
            var action = SEntMan.System<SharedActionsSystem>().GetActions(elir).Single(entry =>
                SComp<MetaDataComponent>(entry.Owner).EntityPrototype?.ID == "ActionElirReadPulse");
            Assert.That(SEntMan.HasComponent<InstantActionComponent>(action.Owner), Is.True);
        });
    }

    [Test]
    public async Task SaurianTailSweepTargetsFloorAndDrainsReserveInThatDirection()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var origin = map.GridCoords;
            var saurian = SSpawnAtPosition("MobSaurian", origin);
            var target = SSpawnAtPosition("MobImperial", origin.Offset(Vector2.UnitX));
            var behind = SSpawnAtPosition("MobImperial", origin.Offset(-Vector2.UnitX));
            var reserve = SComp<AdvancedExertionComponent>(target);
            var otherReserve = SComp<AdvancedExertionComponent>(behind);
            var start = reserve.Reserve;
            var otherStart = otherReserve.Reserve;
            var actions = SEntMan.System<SharedActionsSystem>();
            var action = actions.GetActions(saurian).Single(entry =>
                SComp<MetaDataComponent>(entry.Owner).EntityPrototype?.ID == "ActionSaurianTailSweep");
            var aim = origin.Offset(new Vector2(3, 0));
            Assert.That(actions.ValidateWorldTarget(saurian, aim, (action.Owner, SComp<WorldTargetActionComponent>(action.Owner))), Is.True);
            var sweep = new SaurianTailSweepEvent { Target = aim };
            actions.PerformAction(saurian, action, sweep, predicted: false);
            Assert.That(sweep.Handled, Is.True);
            Assert.That(reserve.Reserve, Is.EqualTo(start - 28f));
            Assert.That(otherReserve.Reserve, Is.EqualTo(otherStart));
        });
    }

    [Test]
    public async Task AviansUseCanineTherianClothingVisuals()
    {
        await Client.WaitAssertion(() =>
        {
            var inventory = CEntMan.System<InventorySystem>();
            var sprites = CEntMan.System<SpriteSystem>();
            foreach (var (avianId, canineId) in new[]
                     {
                         ("AppearanceAvian", "AppearanceTherianCanine"),
                         ("MobAvian", "MobTherianCanine"),
                     })
            {
                var avian = CSpawn(avianId);
                var canine = CSpawn(canineId);
                var avianInventory = CEntMan.GetComponent<InventoryComponent>(avian);
                var canineInventory = CEntMan.GetComponent<InventoryComponent>(canine);
                Assert.That(avianInventory.SpeciesId, Is.EqualTo(canineInventory.SpeciesId).And.EqualTo("vulpkanin"));
                Assert.That(MapAssets(avianInventory.Displacements), Is.EquivalentTo(MapAssets(canineInventory.Displacements)));
                Assert.That(MapAssets(avianInventory.FemaleDisplacements), Is.EquivalentTo(MapAssets(canineInventory.FemaleDisplacements)));

                Assert.That(inventory.TryEquip(avian, CSpawn("ClothingHeadHelmetHardsuitEngineering"), "head", force: true), Is.True);
                Assert.That(sprites.TryGetLayer(avian, "head-0", out var helmet, false), Is.True);
                Assert.That(helmet!.State.Name, Is.EqualTo("equipped-head-vulpkanin"));
                Assert.That(sprites.LayerMapTryGet(avian, "head-0", out var helmetLayer, false), Is.True);
                Assert.That(sprites.LayerMapTryGet(avian, "AvianWings-harpy", out var wingLayer, false), Is.True);
                Assert.That(wingLayer, Is.GreaterThan(helmetLayer));

                var profile = HumanoidCharacterProfile.DefaultWithSpecies("Avian", Sex.Female);
                CEntMan.System<HumanoidProfileSystem>().ApplyProfileTo(avian, profile);
                Assert.That(inventory.TryEquip(avian, CSpawn("ClothingStockingsBlackSheer"), "socks", force: true), Is.True);
                Assert.That(sprites.TryGetLayer(avian, "socks-0", out var stockings, false), Is.True);
                Assert.That(stockings!.State.Name, Is.EqualTo("equipped-SOCKS"));
                Assert.That(sprites.TryGetLayer(avian, "socks-0-displacement", out var stockingMap, false), Is.True);
                Assert.That(stockingMap!.State.Name, Is.EqualTo("socks"));
            }
        });

        static IEnumerable<(string Slot, int Size, string? Sprite, string? State)> MapAssets(
            Dictionary<string, DisplacementData> maps)
        {
            foreach (var (slot, data) in maps)
            foreach (var (size, layer) in data.SizeMaps)
                yield return (slot, size, layer.RsiPath, layer.State);
        }
    }

    [Test]
    public async Task AvianProfilesKeepWingsAndAllowTailCustomization()
    {
        await Client.WaitAssertion(() =>
        {
            var avian = new ProtoId<SpeciesPrototype>("Avian");
            var species = CProtoMan.Index(avian);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(avian, Sex.Female);
            var appearance = profile.Appearance;
            var torso = appearance.Markings["Torso"];
            var wings = new Marking("AvianWingsTwoTone", [Color.Red, Color.Blue]);
            torso.Remove(HumanoidVisualLayers.Wings);
            torso[HumanoidVisualLayers.TailOverlay] = [wings];
            var legacyTail = new Marking("HarpyTail", [Color.Orange])
            {
                Gradient = new MarkingGradient { TipColor = Color.Yellow, Steps = 5 },
            };
            torso[HumanoidVisualLayers.Tail] = [legacyTail];

            var validated = HumanoidCharacterAppearance.EnsureValid(appearance, avian, profile.Sex, []);
            var validatedTorso = validated.Markings["Torso"];
            Assert.That(validatedTorso.ContainsKey(HumanoidVisualLayers.TailOverlay), Is.False);
            Assert.That(validatedTorso[HumanoidVisualLayers.Tail].Single(),
                Is.EqualTo(legacyTail with { MarkingId = "AvianTailHarpy" }));
            Assert.That(validatedTorso[HumanoidVisualLayers.Wings].Single(), Is.EqualTo(wings));

            var markings = Client.ResolveDependency<MarkingManager>();
            var model = new MarkingsViewModel
            {
                Species = avian,
                OrganData = markings.GetMarkingData(avian),
                OrganProfileData = markings.GetProfileData(avian, profile.Sex, validated.SkinColor, validated.EyeColor),
                Markings = validated.Markings,
            };
            Assert.That(model.OrganData["Torso"].Layers, Does.Not.Contain(HumanoidVisualLayers.TailOverlay));
            var tails = markings.MarkingsByLayerAndSpeciesGroupAndSex(
                HumanoidVisualLayers.Tail, avian, "Avian", profile.Sex);
            Assert.That(tails.Keys, Is.EquivalentTo(new[] { "AvianTailHarpy", "AvianTailPhoenix", "AvianTailRooster" }));
            Assert.That(tails.Values.Select(tail => tail.PreviewDirection), Is.All.EqualTo(Direction.North));
            Assert.That(model.TryDeselectMarking("Torso", HumanoidVisualLayers.Tail, "AvianTailHarpy"), Is.False);
            Assert.That(model.IsMarkingColorCustomizable("Torso", HumanoidVisualLayers.Tail, "AvianTailHarpy"), Is.True);
            Assert.That(model.TrySelectMarking("Torso", HumanoidVisualLayers.Tail, "AvianTailPhoenix"), Is.True);
            Assert.That(model.TrySelectMarking("Torso", HumanoidVisualLayers.Tail, "AvianTailRooster"), Is.True);
            model.TrySetMarkingColor("Torso", HumanoidVisualLayers.Tail, "AvianTailRooster", 0, Color.Blue);
            var editedTail = model.SelectedMarkings("Torso", HumanoidVisualLayers.Tail)!.Single();
            Assert.That(editedTail.MarkingId.Id, Is.EqualTo("AvianTailRooster"));
            Assert.That(editedTail.MarkingColors.Single(), Is.EqualTo(Color.Blue));

            var body = CSpawn(species.DollPrototype.Id);
            CEntMan.System<SharedVisualBodySystem>().ApplyProfileTo(body, profile.WithCharacterAppearance(validated));
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.That(sprites.LayerMapTryGet(body, "AvianWingsTwoTone-harpy2tone1", out var wingLayer, false), Is.True);
            Assert.That(sprites.LayerMapTryGet(body, "AvianWingsTwoTone-harpy2tone1_north", out var rearWing, false), Is.True);
            Assert.That(sprites.LayerMapTryGet(body, HumanoidVisualLayers.Hair, out var hairLayer, false), Is.True);
            Assert.That(rearWing, Is.LessThan(hairLayer));
            Assert.That(wingLayer, Is.GreaterThan(hairLayer));
            Assert.That(sprites.LayerMapTryGet(body, HumanoidVisualLayers.Chest, out var chestLayer, false), Is.True);
            Assert.That(sprites.LayerMapTryGet(body, "AvianTailHarpy-harpy_tail_feathers", out var tailLayer, false), Is.True);
            Assert.That(sprites.LayerMapTryGet(body, HumanoidVisualLayers.Handcuffs, out var cuffsLayer, false), Is.True);
            Assert.That(wingLayer, Is.GreaterThan(tailLayer).And.GreaterThan(cuffsLayer));
            Assert.That(tailLayer, Is.GreaterThan(chestLayer));
            Assert.That(sprites.TryGetLayer(body, wingLayer, out var wing, false), Is.True);
            Assert.That(wing!.Offset.Y, Is.Zero);
        });
    }

    [Test]
    public async Task LavriteTailsUseTheForegroundTailLayerWithPaletteGradients()
    {
        await Client.WaitAssertion(() =>
        {
            var lavrite = new ProtoId<SpeciesPrototype>("Lavrite");
            var species = CProtoMan.Index(lavrite);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(lavrite, Sex.Female);
            var body = CSpawn(species.DollPrototype.Id);
            var visuals = CEntMan.System<SharedVisualBodySystem>();
            var sprites = CEntMan.System<SpriteSystem>();

            foreach (var (marking, state) in new[] { ("LavriteTailFlame", "tail_large"), ("LavriteTailEmber", "tail_spikes") })
            {
                profile.Appearance.Markings["Torso"][HumanoidVisualLayers.Tail] = [new Marking(marking, 1)];
                var appearance = profile.Appearance.WithColorPreset(species.AppearancePresets[0]);
                visuals.ApplyProfileTo(body, profile.WithCharacterAppearance(appearance));

                Assert.That(sprites.LayerMapTryGet(body, $"{marking}-{state}", out var tail, false), Is.True);
                Assert.That(sprites.LayerMapTryGet(body, HumanoidVisualLayers.Tail, out var tailAnchor, false), Is.True);
                Assert.That(tail, Is.EqualTo(tailAnchor + 1));
            }
        });
    }

    [Test]
    public async Task LavritePalettesKeepTheirColorsAndSelectedSilhouettes()
    {
        await Server.WaitAssertion(() =>
        {
            var id = new ProtoId<SpeciesPrototype>("Lavrite");
            var species = SProtoMan.Index(id);
            Assert.That(species.AppearancePresets.Count, Is.EqualTo(4));

            var appearance = HumanoidCharacterAppearance.DefaultWithSpecies(id, Sex.Female);
            Assert.That(appearance.Markings["Torso"][HumanoidVisualLayers.Tail].Single().MarkingId.Id, Is.EqualTo("LavriteTailFlame"));
            appearance.Markings["Head"][HumanoidVisualLayers.Hair] = [new Marking("HumanHairShorthair2", 1)];
            appearance.Markings["Torso"][HumanoidVisualLayers.Tail] = [new Marking("LavriteTailEmber", 1)];

            foreach (var preset in species.AppearancePresets)
            {
                var applied = appearance.WithColorPreset(preset);
                var validated = HumanoidCharacterAppearance.EnsureValid(applied, id, Sex.Female, []);
                Assert.That(validated.SkinColor, Is.EqualTo(applied.SkinColor));
                Assert.That(validated.EyeColor, Is.EqualTo(appearance.EyeColor));
                AssertGradient(validated.Markings["Head"][HumanoidVisualLayers.Hair].Single(), "HumanHairShorthair2", preset.Hair);
                AssertGradient(validated.Markings["Torso"][HumanoidVisualLayers.Tail].Single(), "LavriteTailEmber", preset.Tail);
            }

            Assert.That(appearance.Markings["Head"][HumanoidVisualLayers.Hair].Single().Gradient, Is.Null);
            Assert.That(appearance.Markings["Torso"][HumanoidVisualLayers.Tail].Single().Gradient, Is.Null);
        });

        static void AssertGradient(Marking marking, string id, PresetGradient preset)
        {
            Assert.That(marking.MarkingId.Id, Is.EqualTo(id));
            Assert.That(marking.MarkingColors[0], Is.EqualTo(preset.Root));
            Assert.That(marking.Gradient?.Stops?.Single().Color, Is.EqualTo(preset.Middle));
            Assert.That(marking.Gradient?.TipColor, Is.EqualTo(preset.Tip));
        }
    }

    [Test]
    public async Task EclipseRacesSpawnWithTheirOwnAbilities()
    {
        await Server.WaitAssertion(() =>
        {
            var expected = new Dictionary<string, string[]>
            {
                ["Imperial"] = [],
                ["Molei"] = ["ActionMoleiSilk"],
                ["Dwarf"] = ["ActionDwarfBrace"],
                ["Lavrite"] = ["ActionLavriteVentHeat", "ActionLavriteThermalBurst"],
                ["Kobold"] = ["ActionKoboldScurry"],
                ["Saurian"] = ["ActionSaurianTailSweep"],
                ["Therian"] = ["ActionTherianPounce"],
                ["TherianCanine"] = ["ActionTherianScent", "ActionVulpkaninGravityJump"],
                ["TherianSmall"] = ["ActionTherianScurry"],
                ["Arkane"] = ["ActionArkaneWingHop"],
                ["Shadowkin"] = ["ActionShadowVeil"],
                ["Avian"] = ["ActionAvianWingLeap", "ActionAvianThrowFeather"],
                ["Elir"] = ["ActionElirReadPulse"],
                ["Slimefolk"] = ["ActionSlimeReform", "ActionSlimeAdhesive"],
            };
            var actions = SEntMan.System<SharedActionsSystem>();
            var bodies = new Dictionary<string, EntityUid>();
            foreach (var (id, actionIds) in expected)
            {
                var species = SProtoMan.Index<SpeciesPrototype>(id);
                Assert.That(species.RoundStart, Is.True, id);
                var mob = SSpawn(species.Prototype.Id);
                bodies.Add(id, mob);
                Assert.That(SComp<HumanoidProfileComponent>(mob).Species.Id, Is.EqualTo(id));
                var actual = actions.GetActions(mob).Select(action => SComp<MetaDataComponent>(action.Owner).EntityPrototype?.ID);
                foreach (var action in actionIds)
                    Assert.That(actual, Does.Contain(action), id);
            }

            var slime = bodies["Slimefolk"];
            Assert.That(SComp<AdvancedHealthComponent>(slime).NeedsOxygen, Is.True);
            var organs = SComp<InitialBodyComponent>(slime).Organs;
            Assert.That(organs.ContainsKey("Lungs"), Is.True);
            var respirator = SEntMan.System<RespiratorSystem>();
            var vacuum = new GasMixture(6f) { Temperature = 293f };
            Assert.That(respirator.CanMetabolizeInhaledAir(slime, vacuum), Is.False);
            foreach (var gas in new[] { Gas.Oxygen, Gas.Nitrogen, Gas.CarbonDioxide, Gas.Plasma, Gas.WaterVapor,
                         Gas.Ammonia, Gas.NitrousOxide, Gas.Tritium, Gas.Frezon })
            {
                var sample = new GasMixture(6f) { Temperature = 293f };
                sample.SetMoles(gas, 0.25f);
                var breathable = gas is not (Gas.Tritium or Gas.Frezon);
                Assert.That(respirator.CanMetabolizeInhaledAir(slime, sample), Is.EqualTo(breathable), gas.ToString());

                respirator.UpdateSaturation(slime, -100f);
                var inhaled = new InhaledGasEvent(sample);
                SEntMan.EventBus.RaiseLocalEvent(slime, ref inhaled);
                Assert.That(respirator.IsBreathing(slime), Is.EqualTo(breathable), gas.ToString());
            }
            Assert.That(SComp<AdvancedHealthComponent>(bodies["Lavrite"]).NormalBodyTemperature, Is.EqualTo(323.15f));
            Assert.That(SProtoMan.Index(Vulpkanin).RoundStart, Is.False);
            var migrated = HumanoidCharacterProfile.MigratePlayableSpecies("Vulpkanin");
            Assert.That(migrated.Id, Is.EqualTo("TherianCanine"));
            Assert.That(CharacterCustomization.HasBodyTypes(migrated), Is.True);
            Assert.That(expected.Keys.Count(id => SProtoMan.Index<SpeciesPrototype>(id).VariantOf?.Id == "Therian"), Is.EqualTo(2));

            var molei = bodies["Molei"];
            var patient = SComp<AdvancedHealthComponent>(bodies["Imperial"]);
            var wound = new Wound { Type = WoundType.Cut, ExternalBleedingRate = 3f };
            patient.BodyParts[BodyPartSlot.LeftForearm].Wounds.Add(wound);
            var silk = new RacialTargetActionEvent
            {
                Performer = molei,
                Target = bodies["Imperial"],
                Ability = RacialTargetAbility.MoleiSilk,
            };
            var silkAction = actions.GetActions(molei).Single(action =>
                SComp<MetaDataComponent>(action.Owner).EntityPrototype?.ID == "ActionMoleiSilk");
            actions.PerformAction(molei, silkAction, silk, predicted: false);
            Assert.That(silk.Handled, Is.True);
            Assert.That(wound.ExternalBleedingRate, Is.EqualTo(1f));
        });
    }

    [Test]
    public async Task EnsureValidLoading()
    {
        var pair = Pair;
        var server = pair.Server;

        await server.WaitIdleAsync();

        await server.WaitAssertion(() =>
        {
            var entityManager = server.ResolveDependency<IEntityManager>();
            var humanoidProfile = entityManager.System<HumanoidProfileSystem>();
            var human = entityManager.Spawn("MobHuman");
            humanoidProfile.ApplyProfileTo(human, new HumanoidCharacterProfile()
                .WithSex(Sex.Female)
                .WithAge(67)
                .WithGender(Gender.Neuter)
                .WithSpecies(Vox));
            var humanoidComponent = entityManager.GetComponent<HumanoidProfileComponent>(human);
            var voiceComponent = entityManager.GetComponent<VocalComponent>(human);

            Assert.That(humanoidComponent.Age, Is.EqualTo(67));
            Assert.That(humanoidComponent.Sex, Is.EqualTo(Sex.Female));
            Assert.That(humanoidComponent.Gender, Is.EqualTo(Gender.Neuter));
            Assert.That(humanoidComponent.Species, Is.EqualTo(Vox));

            Assert.That(voiceComponent.Sounds, Is.Not.Null, message: "the MobHuman spawned by this test needs to have sex-specific sound set");
            Assert.That(voiceComponent.Sounds![Sex.Female], Is.EqualTo(voiceComponent.EmoteSounds));
        });
    }

    [Test]
    public async Task AdvantagesAndRoleplayTraitsApplyAtSpawn()
    {
        await Server.WaitAssertion(() =>
        {
            var human = SEntMan.Spawn("MobHuman");
            var stamina = SEntMan.GetComponent<StaminaComponent>(human);
            SEntMan.System<TraitSystem>().ApplyTraits(human, new HashSet<ProtoId<TraitPrototype>>
            {
                "Endurance", "StrongBody", "StrongLiver", "Sober", "ClearDiction", "LightStep", "Tenacity", "PocketMusician",
            });

            var status = SEntMan.System<StatusEffectsSystem>();
            Assert.That(stamina.CritThreshold, Is.EqualTo(stamina.BaseCritThreshold * 1.2f).Within(0.01f));
            Assert.That(status.HasStatusEffect(human, "TraitStatusEffectStrongBody"), Is.True);
            Assert.That(status.HasStatusEffect(human, "TraitStatusEffectStrongLiver"), Is.True);
            Assert.That(SEntMan.HasComponent<ClearDictionComponent>(human), Is.True);
            Assert.That(SEntMan.HasComponent<LightStepComponent>(human), Is.True);
            Assert.That(SEntMan.HasComponent<LightweightDrunkComponent>(human), Is.True);

            var stand = new GetStandUpTimeEvent(TimeSpan.FromSeconds(4));
            SEntMan.EventBus.RaiseLocalEvent(human, ref stand);
            Assert.That(stand.DoAfterTime, Is.EqualTo(TimeSpan.FromSeconds(3)));
            var harmonica = SEntMan.System<SharedHandsSystem>().EnumerateHeld(human).Single();
            Assert.That(SEntMan.GetComponent<MetaDataComponent>(harmonica).EntityPrototype?.ID, Is.EqualTo("HarmonicaInstrument"));
            SEntMan.DeleteEntity(human);
        });
    }

    [Test]
    public async Task TraitRebalancePreservesFreeChoicesInExistingProfiles()
    {
        await Server.WaitAssertion(() =>
        {
            const int maxPoints = 6;
            var freeTraits = new ProtoId<TraitPrototype>[] { "Snoring", "LanguageGalacticCommon", "LanguageTradeband" };
            var profile = new HumanoidCharacterProfile();
            foreach (var trait in freeTraits)
                profile = profile.WithTraitPreference(trait, SProtoMan, maxPoints);

            Assert.That(profile.TraitPreferences, Is.EquivalentTo(freeTraits));
            Assert.That(HumanoidCharacterProfile.TryCalculateTraitPoints(
                profile.TraitPreferences, SProtoMan, maxPoints, out var gained, out var spent, out _), Is.True);
            Assert.That((gained, spent), Is.EqualTo((0, 0)));

            var retainedTraits = freeTraits.Concat(new ProtoId<TraitPrototype>[] { "PoorVision", "Paracusia" });
            var oldChoices = retainedTraits.Append(new ProtoId<TraitPrototype>("StrongBody"));
            Assert.That(profile.GetValidTraits(oldChoices, SProtoMan, maxPoints), Is.EquivalentTo(retainedTraits));

            profile = profile.WithTraitPreference("Pacifist", SProtoMan, maxPoints)
                .WithTraitPreference("StrongBody", SProtoMan, maxPoints);
            Assert.That(profile.TraitPreferences, Does.Contain(new ProtoId<TraitPrototype>("StrongBody")));
        });
    }

    [Test]
    public async Task ServerOnlyVoiceCanBeSelectedRegardlessOfSex()
    {
        const string voiceId = "TestServerOnlyVoice";
        const string yaml = """
            - type: ttsVoice
              id: TestServerOnlyVoice
              name: Test voice
              sex: Female
              speaker: test
            """;
        var serverPrefs = Server.Resolve<IServerPreferencesManager>();
        var clientPrefs = Client.Resolve<IClientPreferencesManager>();
        var player = Pair.Player!;
        var original = serverPrefs.GetPreferences(player.UserId).Characters[0].Clone();
        var changed = new Dictionary<Type, HashSet<string>>();
        try
        {
            await Server.WaitPost(() =>
            {
                SProtoMan.LoadString(yaml, changed: changed);
                SProtoMan.ReloadPrototypes(changed);
                serverPrefs.FinishLoad(player);
            });
            await Pair.RunTicksSync(5);
            await Client.WaitAssertion(() =>
            {
                Assert.That(CProtoMan.TryIndex<TTSVoicePrototype>(voiceId, out _), Is.False);
                Assert.That(clientPrefs.Settings!.TtsVoices.Single(voice => voice.Id == voiceId),
                    Is.EqualTo(new TTSVoiceOption(voiceId, "Test voice", "Test voice", true)));
                clientPrefs.UpdateCharacter(original.WithSpecies(HumanoidCharacterProfile.DefaultSpecies)
                    .WithSex(Sex.Male).WithVoice(voiceId), 0);
            });

            await PoolManager.WaitUntil(Server, () => serverPrefs.GetPreferences(player.UserId).Characters[0].Voice == voiceId, maxTicks: 60);
            await Server.WaitAssertion(() => Assert.That(serverPrefs.GetPreferences(player.UserId).Characters[0].Sex, Is.EqualTo(Sex.Male)));
        }
        finally
        {
            await Server.WaitPost(() =>
            {
                serverPrefs.SetProfile(player.UserId, 0, original).GetAwaiter().GetResult();
                SProtoMan.RemoveString(yaml);
                SProtoMan.ReloadPrototypes(new(), changed);
                serverPrefs.FinishLoad(player);
            });
            await Pair.RunTicksSync(5);
        }
    }

}
