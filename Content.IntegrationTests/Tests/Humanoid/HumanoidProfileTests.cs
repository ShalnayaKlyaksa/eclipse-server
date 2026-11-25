using System.Collections.Generic;
using System.Linq;
using Content.Client.Lobby;
using Content.IntegrationTests.Fixtures;
using Content.Server.Preferences.Managers;
using Content.Server.Traits;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared._Eclipse.Species;
using Content.Shared.Actions;
using Content.Shared.Body;
using Content.Shared.Corvax.TTS;
using Content.Shared.Damage.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Speech.Components;
using Content.Shared.StatusEffectNew;
using Content.Shared.Stunnable;
using Content.Shared.Traits;
using Content.Shared.Traits.Assorted;
using Robust.Client.GameObjects;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Humanoid;

[TestFixture]
[TestOf(typeof(HumanoidProfileSystem))]
public sealed class HumanoidProfileTests : GameTest
{
    private static readonly ProtoId<SpeciesPrototype> Vox = "Vox";

    [Test]
    public async Task LavriteTailsRenderBehindTheBodyWithPaletteGradients()
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
                Assert.That(sprites.LayerMapTryGet(body, HumanoidVisualLayers.Chest, out var torso, false), Is.True);
                Assert.That(sprites.LayerMapTryGet(body, HumanoidVisualLayers.LLeg, out var leg, false), Is.True);
                Assert.That(sprites.LayerMapTryGet(body, "pants", out var clothes, false), Is.True);
                Assert.That(tail, Is.LessThan(torso).And.LessThan(leg).And.LessThan(clothes));
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
                ["Lavrite"] = ["ActionLavriteVentHeat", "ActionLavriteCoolTouch"],
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
            Assert.That(SComp<AdvancedHealthComponent>(slime).NeedsOxygen, Is.False);
            var organs = SComp<InitialBodyComponent>(slime).Organs;
            Assert.That(organs.ContainsKey("Lungs"), Is.False);
            Assert.That(SComp<AdvancedHealthComponent>(bodies["Lavrite"]).NormalBodyTemperature, Is.EqualTo(323.15f));
            Assert.That(SProtoMan.Index<SpeciesPrototype>("Vulpkanin").RoundStart, Is.False);
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
            SEntMan.EventBus.RaiseLocalEvent(molei, silk);
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
