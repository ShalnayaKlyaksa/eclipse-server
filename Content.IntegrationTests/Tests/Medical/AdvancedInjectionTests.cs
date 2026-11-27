#nullable enable annotations

using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Server._Eclipse.AdvancedHealth;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.FixedPoint;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.Map;
using Content.Client._Eclipse.AdvancedHealth;
using Content.Shared.Buckle;
using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class AdvancedInjectionTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task ExamineHealthUsesSelectedBodyPartAndUnlocksChipOnOperatingTable()
    {
        var map = await Pair.CreateTestMap();
        EntityUid patient = default;
        EntityUid medic = default;
        EntityUid table = default;
        NetEntity patientNet = default;
        await Server.WaitAssertion(() =>
        {
            medic = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            table = SEntMan.SpawnEntity("OperatingTable", map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(Server.PlayerMan.Sessions.Single(), medic);
            patientNet = SEntMan.GetNetEntity(patient);
            var part = SEntMan.GetComponent<AdvancedHealthComponent>(patient).BodyParts[BodyPartSlot.LeftForearm];
            part.ForeignBodyCount = 1;
            part.Wounds.Add(new Wound { Type = WoundType.Gunshot, HasForeignBody = true });
        });
        await Pair.RunTicksSync(10);
        AdvancedHealthFieldWindow? window = null;
        Button? chip = null;
        await Client.WaitAssertion(() =>
        {
            var target = CEntMan.GetEntity(patientNet);
            var user = Client.ResolveDependency<Robust.Client.Player.IPlayerManager>().LocalEntity!.Value;
            var verbs = new GetVerbsEvent<ExamineVerb>(user, target, null, null, true, true, true, new());
            CEntMan.EventBus.RaiseLocalEvent(target, verbs);
            var health = verbs.Verbs.Single(v => v.Text == Client.ResolveDependency<Robust.Shared.Localization.ILocalizationManager>().GetString("advanced-health-examine-verb"));
            Assert.That(health.ClientExclusive, Is.True);
            Assert.That(health.Icon, Is.Not.Null);
            Assert.That(health.ShowOnExamineTooltip, Is.True);
            health.Act!();
            window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<AdvancedHealthFieldWindow>().Single();
            CEntMan.System<AdvancedHealthClientSystem>().SetAimTarget(BodyPartTarget.LeftForearm);
            var selected = Find<Label>(window, "MedicalSelectedPart");
            Assert.That(selected.Text, Is.Not.Empty);
            Assert.That(FindOrNull<Button>(window, "MedicalTreatment-ForeignBodyRemoval"), Is.Null, "Examination must not have a procedure button list.");
            chip = Find<Button>(window, "MedicalChipReadings");
            Assert.That(chip.Disabled, Is.True, "Field examination must not open full chip data.");
        });
        await Server.WaitAssertion(() => Assert.That(SEntMan.System<SharedBuckleSystem>().TryBuckle(patient, medic, table), Is.True));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            window!.SelectPart(BodyPartTarget.LeftForearm);
            Assert.That(chip!.Disabled, Is.False, "Buckling the patient to an operating table unlocks full readings.");
            window.Close();
        });
    }

    private static T Find<T>(Control parent, string name) where T : Control
    {
        if (parent is T match && parent.Name == name) return match;
        foreach (var child in parent.Children)
        {
            var result = FindOrNull<T>(child, name);
            if (result != null) return result;
        }
        throw new System.InvalidOperationException("Missing control " + name);
    }

    private static T? FindOrNull<T>(Control parent, string name) where T : Control
    {
        if (parent is T match && parent.Name == name) return match;
        foreach (var child in parent.Children)
            if (FindOrNull<T>(child, name) is { } result) return result;
        return null;
    }

    [Test]
    public async Task ClothActionAppearsOnlyForBleedingPartAndIsAvailableInQuickSelector()
    {
        var map = await Pair.CreateTestMap();
        NetEntity userNet = default;
        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            userNet = SEntMan.GetNetEntity(user);
            Server.PlayerMan.SetAttachedEntity(Server.PlayerMan.Sessions.Single(), user);
            var part = SEntMan.GetComponent<AdvancedHealthComponent>(user).BodyParts[BodyPartSlot.LeftForearm];
            part.Wounds.Add(new Wound { Type = WoundType.Cut, ExternalBleedingRate = 2 });
            var cloth = SEntMan.SpawnEntity("MaterialCloth1", map.MapCoords);
            var hands = SEntMan.GetComponent<HandsComponent>(user);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(user, cloth, hands.ActiveHandId!), Is.True);
            Assert.That(SEntMan.HasComponent<AdvancedBandageRollComponent>(cloth), Is.True);
        });
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var user = CEntMan.GetEntity(userNet);
            var client = CEntMan.System<AdvancedHealthClientSystem>();
            Assert.That(client.TryGetPartAction(user, user, BodyPartSlot.LeftForearm, out var tool, out var label), Is.True);
            Assert.That(tool, Is.Not.Null);
            Assert.That(client.TryGetPartAction(user, user, BodyPartSlot.RightForearm, out _, out _), Is.False);
            var popup = new AdvancedHealthAimSelectorPopup();
            popup.OpenAtCursor(new Vector2(500, 400));
            var doll = typeof(AdvancedHealthAimSelectorPopup).GetField("_doll", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popup)!;
            doll.GetType().GetProperty("HoveredSlot")!.SetValue(doll, BodyPartSlot.LeftForearm);
            typeof(AdvancedHealthAimSelectorPopup).GetMethod("UpdateTreatment", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(popup, null);
            var action = Find<Button>(popup, "MedicalPartToolAction");
            Assert.That(action.Visible, Is.True);
            Assert.That(action.Text, Is.EqualTo(label));
            popup.Close();
        });
    }

    [Test]
    public async Task ContinuousInjectionTransfersPartialThenEntireSyringeAndRefillsFromVial()
    {
        var map = await Pair.CreateTestMap();
        EntityUid user = default;
        EntityUid syringe = default;
        NetEntity syringeNet = default;
        NetEntity userNet = default;
        AdvancedHealthInjectionStatusEvent? status = null;
        AdvancedSyringeInjectionSystem system = default!;
        AdvancedHealthInjectionInputEvent Input(int id, InjectionInputAction action, float depth = 0, bool held = false)
            => new(id, userNet, syringeNet, BodyPartSlot.Chest, action, depth, held);
        await Server.WaitPost(() => Server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>().SetCVar(Robust.Shared.CVars.NetTickrate, 60));
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            user = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(Server.PlayerMan.Sessions.Single(), user);
            syringe = SEntMan.SpawnEntity("SyringeInaprovaline", map.MapCoords);
            syringeNet = SEntMan.GetNetEntity(syringe);
            userNet = SEntMan.GetNetEntity(user);
            system = SEntMan.System<AdvancedSyringeInjectionSystem>();
            Assert.That(SEntMan.System<InjectorSystem>().TryGetInjectionVolume(syringe, out var volume), Is.True);
            Assert.That(volume.Float(), Is.EqualTo(15), "The minigame uses the whole syringe, regardless of the old 5-unit setting.");
            Assert.That(system.HandleInput(user, Input(1, InjectionInputAction.Start))!.Failed, Is.True);
            var hands = SEntMan.GetComponent<HandsComponent>(user);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(user, syringe, hands.ActiveHandId!), Is.True);
            status = system.HandleInput(user, Input(2, InjectionInputAction.Start));
            Assert.That(status!.Failed, Is.False);
            system.HandleInput(user, Input(2, InjectionInputAction.Adjust, 32, true));
        });
        for (var i = 0; i < 5; i++)
        {
            await Pair.RunTicksSync(6);
            await Server.WaitAssertion(() =>
            {
                status = system.HandleInput(user, Input(2, InjectionInputAction.Adjust, 32, true));
                Assert.That(status, Is.Not.Null, "The injection session must survive normal input intervals.");
                Assert.That(status!.Failed, Is.False);
            });
        }
        await Server.WaitAssertion(() =>
        {
            Assert.That(status!.Injected, Is.InRange(0.5f, 10));
            Assert.That(status.Injected + status.Remaining, Is.EqualTo(15).Within(0.01));
            var previous = status.Injected;
            for (var i = 0; i < 50; i++) status = system.HandleInput(user, Input(2, InjectionInputAction.Adjust, 32, true));
            Assert.That(status!.Injected, Is.EqualTo(previous), "Repeated packets cannot manufacture server time.");
            status = system.HandleInput(user, Input(2, InjectionInputAction.Stop));
        });
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var local = CEntMan.GetEntity(syringeNet);
            Assert.That(CEntMan.System<InjectorSystem>().TryGetInjectionVolume(local, out var remaining), Is.True);
            Assert.That(remaining.Float(), Is.EqualTo(status!.Remaining).Within(0.01));
        });
        await Server.WaitAssertion(() =>
        {
            status = system.HandleInput(user, Input(3, InjectionInputAction.Start));
            system.HandleInput(user, Input(3, InjectionInputAction.Adjust, 36, true));
        });
        for (var i = 0; i < 100 && !status!.Stopped; i++)
        {
            await Pair.RunTicksSync(6);
            await Server.WaitAssertion(() => status = system.HandleInput(user, Input(3, InjectionInputAction.Adjust, 36, true)));
        }
        EntityUid vial = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(status!.Failed, Is.False, $"The final transfer failed after {status.Injected} units; {status.Remaining} left.");
            Assert.That(status.Stopped, Is.True);
            Assert.That(status.Remaining, Is.Zero);
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(syringe, "injector", out _, out var solution);
            Assert.That(solution!.Volume, Is.EqualTo(FixedPoint2.Zero));
            vial = SEntMan.SpawnEntity("AdvancedMedicineVialInaprovaline", map.MapCoords);
            var interact = new AfterInteractEvent(user, syringe, vial, new EntityCoordinates(vial, Vector2.Zero), true);
            SEntMan.EventBus.RaiseLocalEvent(syringe, interact);
            Assert.That(interact.Handled, Is.True);
        });
        await Pair.RunSeconds(3);
        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(syringe, "injector", out _, out var loaded);
            solutions.TryGetSolution(vial, "drink", out _, out var stock);
            Assert.That(loaded!.Volume.Float(), Is.EqualTo(5));
            Assert.That(stock!.Volume.Float(), Is.EqualTo(25));
        });
    }

    [Test]
    public async Task ClickingLoadedSyringeOpensMinigameAtChestOrChosenPartWithoutInjecting()
    {
        var map = await Pair.CreateTestMap();
        EntityUid medic = default;
        EntityUid patient = default;
        EntityUid syringe = default;
        await Server.WaitAssertion(() =>
        {
            medic = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            syringe = SEntMan.SpawnEntity("SyringeInaprovaline", map.MapCoords);
            Server.PlayerMan.SetAttachedEntity(Server.PlayerMan.Sessions.Single(), medic);
            var hands = SEntMan.GetComponent<HandsComponent>(medic);
            SEntMan.System<SharedHandsSystem>().TryPickup(medic, syringe, hands.ActiveHandId!);
        });
        foreach (var target in new[] { BodyPartTarget.Auto, BodyPartTarget.LeftForearm })
        {
            await Server.WaitPost(() =>
            {
                SEntMan.EnsureComponent<AimTargetComponent>(medic).CurrentTarget = target;
                var interact = new AfterInteractEvent(medic, syringe, patient, new EntityCoordinates(patient, Vector2.Zero), true);
                SEntMan.EventBus.RaiseLocalEvent(syringe, interact);
                Assert.That(interact.Handled, Is.True);
            });
            await Pair.RunTicksSync(12);
            await Client.WaitAssertion(() =>
            {
                var window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<AdvancedHealthTreatmentMinigameWindow>().Single();
                var slot = (BodyPartSlot) typeof(AdvancedHealthTreatmentMinigameWindow).GetField("_slot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                Assert.That(slot, Is.EqualTo(target == BodyPartTarget.Auto ? BodyPartSlot.Chest : BodyPartSlot.LeftForearm));
                window.Close();
            });
        }
        await Server.WaitAssertion(() =>
        {
            SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(syringe, "injector", out _, out var solution);
            Assert.That(solution!.Volume.Float(), Is.EqualTo(15), "Opening or cancelling the minigame cannot inject anything.");
        });
    }

    [Test]
    public async Task BloodDrawRequiresEmptySterileSyringeAndForearm()
    {
        var map = await Pair.CreateTestMap();
        EntityUid user = default;
        EntityUid syringe = default;
        await Server.WaitAssertion(() =>
        {
            user = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            syringe = SEntMan.SpawnEntity("Syringe", map.MapCoords);
            var hands = SEntMan.GetComponent<HandsComponent>(user);
            SEntMan.System<SharedHandsSystem>().TryPickup(user, syringe, hands.ActiveHandId!);
            var injectors = SEntMan.System<InjectorSystem>();
            Assert.That(injectors.TryStartTreatmentBloodDraw(syringe, user, user, BodyPartSlot.Chest), Is.False);
            Assert.That(injectors.TryStartTreatmentBloodDraw(syringe, user, user, BodyPartSlot.LeftForearm), Is.True);
        });
        await Pair.RunSeconds(4);
        await Server.WaitAssertion(() =>
        {
            var solutions = SEntMan.System<SharedSolutionContainerSystem>();
            solutions.TryGetSolution(syringe, "injector", out var entity, out var solution);
            Assert.That(solution!.Volume.Float(), Is.EqualTo(5));
            solutions.SplitSolution(entity!.Value, solution.Volume);
            Assert.That(SEntMan.System<InjectorSystem>().TryGetBloodDrawCapacity(syringe, out _), Is.False,
                "Emptying a used syringe does not make it sterile again.");
        });
    }

    [TestCase("ShardGlass")]
    [TestCase("ShardGlassReinforced")]
    public async Task GlassStepCreatesGlassPunctureNotBullet(string prototype)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var patient = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var glass = SEntMan.SpawnEntity(prototype, map.MapCoords);
            var damage = new DamageSpecifier();
            damage.DamageDict["Piercing"] = FixedPoint2.New(5);
            SEntMan.System<DamageableSystem>().TryChangeDamage(patient, damage, ignoreResistances: true, origin: glass);
            var wounds = SEntMan.GetComponent<AdvancedHealthComponent>(patient).BodyParts.Values.SelectMany(p => p.Wounds).ToArray();
            Assert.That(wounds.Any(w => w.HasForeignBody && w.ForeignBody == ForeignBodyKind.Glass), Is.True);
            Assert.That(wounds.Any(w => w.Type == WoundType.Gunshot || w.ForeignBody == ForeignBodyKind.Bullet), Is.False);
        });
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task BreakingTheNeedleLeavesARealForeignBodyAndDisablesTheSyringe(bool empty)
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var syringe = SEntMan.SpawnEntity("SyringeInaprovaline", map.MapCoords);
            var hands = SEntMan.GetComponent<HandsComponent>(user);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(user, syringe, hands.ActiveHandId!), Is.True);
            var system = SEntMan.System<AdvancedSyringeInjectionSystem>();
            var injectors = SEntMan.System<InjectorSystem>();
            AdvancedHealthInjectionInputEvent Input(InjectionInputAction action, float depth = 0)
                => new(30, SEntMan.GetNetEntity(user), SEntMan.GetNetEntity(syringe), BodyPartSlot.LeftForearm, action, depth);
            system.HandleInput(user, Input(InjectionInputAction.Start));
            Assert.That(system.HandleInput(user, Input(InjectionInputAction.BreakNeedle))!.NeedleBroken, Is.False,
                "A syringe outside tissue cannot leave a needle inside the patient.");
            system.HandleInput(user, Input(InjectionInputAction.Adjust, 32));
            if (empty)
            {
                Assert.That(injectors.TryInjectTreatmentAmount(syringe, user, user, FixedPoint2.New(15), out _), Is.True);
                Assert.That(system.HandleInput(user, Input(InjectionInputAction.Adjust, 32))!.Stopped, Is.True);
            }
            var result = system.HandleInput(user, Input(InjectionInputAction.BreakNeedle));
            Assert.That(result!.NeedleBroken, Is.True, "The needle can still break after the last medicine has left it.");
            Assert.That(result.Stopped, Is.True);
            var injector = SEntMan.GetComponent<Content.Shared.Chemistry.Components.InjectorComponent>(syringe);
            Assert.That(injector.NeedleBroken, Is.True);
            Assert.That(injector.Sterile, Is.False);
            Assert.That(injectors.TryGetInjectionVolume(syringe, out _), Is.False);
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(user);
            var part = health.BodyParts[BodyPartSlot.LeftForearm];
            var needle = part.Wounds.Single(wound => wound.ForeignBody == ForeignBodyKind.Needle);
            Assert.That(needle.HasForeignBody, Is.True);
            Assert.That(needle.ExternalBleedingRate, Is.GreaterThan(0));
            Assert.That(needle.Pain, Is.GreaterThan(0));
            Assert.That(part.ForeignBodyCount, Is.EqualTo(1));
            system.HandleInput(user, Input(InjectionInputAction.BreakNeedle));
            Assert.That(part.ForeignBodyCount, Is.EqualTo(1), "Duplicate messages cannot embed duplicate needles.");
            Assert.That(system.HandleInput(user, new(31, SEntMan.GetNetEntity(user), SEntMan.GetNetEntity(syringe), part.Slot,
                InjectionInputAction.Start))!.Failed, Is.True);
        });
    }

    [Test]
    public async Task ExtractionWindowHasNoTutorialOrFinishButtonAndClosesOnPhysicalCompletion()
    {
        var map = await Pair.CreateTestMap();
        NetEntity net = default;
        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            net = SEntMan.GetNetEntity(user);
            Server.PlayerMan.SetAttachedEntity(Server.PlayerMan.Sessions.Single(), user);
            var part = SEntMan.GetComponent<AdvancedHealthComponent>(user).BodyParts[BodyPartSlot.LeftForearm];
            part.ForeignBodyCount = 1;
            part.Wounds.Add(new Wound { Type = WoundType.Puncture, HasForeignBody = true, ForeignBody = ForeignBodyKind.Glass });
        });
        await Pair.RunTicksSync(10);
        AdvancedHealthTreatmentMinigameWindow window = default!;
        await Client.WaitAssertion(() =>
        {
            var uid = CEntMan.GetEntity(net);
            window = new AdvancedHealthTreatmentMinigameWindow(uid, BodyPartSlot.LeftForearm,
                AdvancedTreatmentType.ForeignBodyRemoval, null, 0);
            window.OpenCentered();
            Control[] Contents(Control control) => control.Children.SelectMany(child => new[] { child }.Concat(Contents(child))).ToArray();
            var contents = Contents(window);
            Assert.That(contents.OfType<Button>().Select(button => button.Text), Is.EqualTo(new[] { "×" }));
            Assert.That(contents.OfType<RichTextLabel>(), Is.Empty);
            var game = Find<TreatmentMinigameControl>(window, "TreatmentGame").State;
            game.Press(game.ShardPosition);
            for (var i = 1; i <= 180; i++)
            {
                game.Update(1 / 60f);
                game.Move(game.ExtractionPoint(i / 180f));
            }
            Assert.That(window.IsOpen, Is.True);
            game.Release();
            Assert.That(game.Success, Is.True);
        });
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() => Assert.That(window.IsOpen, Is.False));
        await Server.WaitAssertion(() =>
            Assert.That(SEntMan.GetComponent<AdvancedHealthComponent>(SEntMan.GetEntity(net))
                .BodyParts[BodyPartSlot.LeftForearm].ForeignBodyCount, Is.Zero));
    }

    [Test]
    public async Task SyringeWindowDrainsTheWholeSolutionButWaitsForWithdrawalBeforeClosing()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitPost(() => Server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>().SetCVar(Robust.Shared.CVars.NetTickrate, 60));
        await Pair.RunTicksSync(10);
        NetEntity userNet = default;
        NetEntity syringeNet = default;
        await Server.WaitAssertion(() =>
        {
            var user = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            var syringe = SEntMan.SpawnEntity("SyringeInaprovaline", map.MapCoords);
            userNet = SEntMan.GetNetEntity(user);
            syringeNet = SEntMan.GetNetEntity(syringe);
            Server.PlayerMan.SetAttachedEntity(Server.PlayerMan.Sessions.Single(), user);
            var hands = SEntMan.GetComponent<HandsComponent>(user);
            Assert.That(SEntMan.System<SharedHandsSystem>().TryPickup(user, syringe, hands.ActiveHandId!), Is.True);
        });
        await Pair.RunTicksSync(10);
        AdvancedHealthTreatmentMinigameWindow window = default!;
        TreatmentMinigameState game = default!;
        await Client.WaitAssertion(() =>
        {
            var user = CEntMan.GetEntity(userNet);
            window = new AdvancedHealthTreatmentMinigameWindow(user, BodyPartSlot.Chest,
                AdvancedTreatmentType.Injection, CEntMan.GetEntity(syringeNet), 0);
            window.OpenCentered();
            game = Find<TreatmentMinigameControl>(window, "TreatmentGame").State;
            game.Press(game.SyringeGrip);
            game.Move(game.SyringeGrip - new Vector2(0, 50));
            game.Update(0.02f);
            game.Move(game.SyringeGrip + new Vector2(0, 80));
            game.Move(game.Cursor - new Vector2(0, 12));
            game.Release();
            Assert.That(game.NeedleInserted, Is.True);
            game.Press(game.PlungerGrip);
            game.Move(game.PlungerGrip + new Vector2(0, 65));
            Assert.That(game.PressingPlunger, Is.True);
        });
        await Pair.RunTicksSync(300);
        await Client.WaitAssertion(() =>
        {
            Assert.That(window.IsOpen, Is.True, "An empty barrel does not mean the needle has been withdrawn.");
            Assert.That(game.NeedleInserted, Is.True);
            Assert.That(game.RemainingVolume, Is.Zero);
            Assert.That(game.PistonTravel, Is.EqualTo(TreatmentMinigameState.PistonStroke));
            game.Release();
            game.Press(game.SyringeGrip);
            game.Move(game.SyringeGrip - new Vector2(0, 85));
            Assert.That(game.Success, Is.True);
        });
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() => Assert.That(window.IsOpen, Is.False));
        await Server.WaitAssertion(() =>
        {
            var tool = SEntMan.GetEntity(syringeNet);
            SEntMan.System<SharedSolutionContainerSystem>().TryGetSolution(tool, "injector", out _, out var contents);
            Assert.That(contents!.Volume.Float(), Is.Zero);
            Assert.That(SEntMan.GetComponent<Content.Shared.Chemistry.Components.InjectorComponent>(tool).NeedleBroken, Is.False);
        });
    }

}
