using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Mind;
using Content.Server.Bible.Components;
using Content.Shared._Eclipse.Poltergeist;
using Content.Shared.ActionBlocker;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Mind.Components;
using Content.Shared.Speech;
using Content.Shared.Storage.Components;
using Content.Shared.Bible;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Light.Components;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Network;
using Robust.Client.GameObjects;
using PoltergeistSystem = Content.Server._Eclipse.Poltergeist.PoltergeistSystem;

namespace Content.IntegrationTests.Tests.GameRules;

public sealed class PoltergeistTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task ClientReceivesVesselControlsAndActions()
    {
        var map = await Pair.CreateTestMap();
        EntityUid spirit = default;
        NetEntity vessel = default;
        await Server.WaitAssertion(() =>
        {
            spirit = SEntMan.SpawnEntity("MobPoltergeist", map.MapCoords);
            var host = SEntMan.SpawnEntity("Crowbar", map.MapCoords);
            vessel = SEntMan.GetNetEntity(host);
            var minds = SEntMan.System<MindSystem>();
            var mind = minds.GetOrCreateMind(ServerSession!.UserId);
            minds.TransferTo(mind, spirit);
            var state = SEntMan.GetComponent<PoltergeistComponent>(spirit);
            Assert.That(SEntMan.System<PoltergeistSystem>().TryPossess((spirit, state), host), Is.True);
        });
        await Pair.RunTicksSync(15);
        await Client.WaitAssertion(() =>
        {
            var host = CEntMan.GetEntity(vessel);
            Assert.That(Client.AttachedEntity, Is.EqualTo(host));
            Assert.That(CEntMan.GetComponent<InputComponent>(host).ContextName, Is.EqualTo("ghost"));
            var actions = CEntMan.GetComponent<ActionsComponent>(host);
            Assert.That(actions.Actions.Count, Is.GreaterThanOrEqualTo(7));
            foreach (var action in actions.Actions)
                Assert.That(CEntMan.EntityExists(action), Is.True, "Possession actions must be replicated to their controller.");
            var knock = actions.Actions.Single(uid => CEntMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ActionPoltergeistKnock");
            Client.System<Content.Client.Actions.ActionsSystem>().TriggerAction((knock, CEntMan.GetComponent<ActionComponent>(knock)));
        });
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            var host = SEntMan.GetEntity(vessel);
            Assert.That(SEntMan.GetComponent<PoltergeistPossessedComponent>(host).RevealedUntil,
                Is.GreaterThan(Server.ResolveDependency<IGameTiming>().CurTime), "A normal client request must execute the haunted object's action.");
        });
        await Server.WaitPost(() => SEntMan.System<PoltergeistSystem>().Leave((spirit, SEntMan.GetComponent<PoltergeistComponent>(spirit))));
        await Pair.RunTicksSync(5);
        await Client.WaitAssertion(() =>
        {
            var host = CEntMan.GetEntity(vessel);
            Assert.That(CEntMan.HasComponent<PoltergeistPossessedComponent>(host), Is.False);
            Assert.That(CEntMan.HasComponent<InputComponent>(host), Is.False);
        });
        EntityUid observer = default;
        await Server.WaitAssertion(() =>
        {
            var host = SEntMan.GetEntity(vessel);
            var state = SEntMan.GetComponent<PoltergeistComponent>(spirit);
            state.PossessAfter = default;
            Assert.That(SEntMan.System<PoltergeistSystem>().TryPossess((spirit, state), host), Is.True);
            var minds = SEntMan.System<MindSystem>();
            observer = SEntMan.SpawnEntity("MobObserver", map.MapCoords);
            minds.TransferTo(minds.GetMind(host)!.Value, observer);
        });
        await Pair.RunTicksSync(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(observer), "An external mind transfer must not be undone by possession cleanup.");
            Assert.That(SEntMan.GetComponent<PoltergeistComponent>(spirit).Host, Is.Null);
            Assert.That(SEntMan.HasComponent<PoltergeistPossessedComponent>(SEntMan.GetEntity(vessel)), Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PossessionRestoresObjectsAndCanBeCountered(bool useBible)
    {
        var map = await Pair.CreateTestMap();
        EntityUid spirit = default;
        EntityUid host = default;
        EntityUid mind = default;
        EntityUid scanner = default;
        EntityUid chaplain = default;
        await Server.WaitAssertion(() =>
        {
            spirit = SEntMan.SpawnEntity("MobPoltergeist", map.MapCoords);
            host = SEntMan.SpawnEntity("Crowbar", map.MapCoords);
            var minds = SEntMan.System<MindSystem>();
            mind = minds.CreateMind(null).Owner;
            minds.TransferTo(mind, spirit);
            var system = SEntMan.System<PoltergeistSystem>();
            var state = SEntMan.GetComponent<PoltergeistComponent>(spirit);
            var blocker = SEntMan.System<ActionBlockerSystem>();
            Assert.That(blocker.CanSpeak(spirit), Is.False);
            Assert.That(system.TryPossess((spirit, state), host), Is.True);
            Assert.That(minds.GetMind(host), Is.EqualTo(mind));
            Assert.That(blocker.CanSpeak(host), Is.True);
            Assert.That(blocker.CanInteract(host, null), Is.False);
            Assert.That(blocker.CanPickup(host, spirit), Is.False);
            Assert.That(system.CanPossess(host), Is.False, "A second spirit must not steal an occupied object.");
            var originalMass = SEntMan.GetComponent<PhysicsComponent>(host).Mass;

            var knock = Perform<PoltergeistKnockActionEvent>(spirit, new());
            Assert.That(knock.Handled, Is.True, "Actions must still reach the spirit while controlling an object.");
            Assert.That(SEntMan.GetComponent<PoltergeistPossessedComponent>(host).RevealedUntil,
                Is.GreaterThan(Server.ResolveDependency<IGameTiming>().CurTime));
            system.Leave((spirit, state));
            Assert.That(minds.GetMind(spirit), Is.EqualTo(mind));
            Assert.That(SEntMan.HasComponent<PoltergeistPossessedComponent>(host), Is.False);
            Assert.That(SEntMan.HasComponent<MindContainerComponent>(host), Is.False);
            Assert.That(SEntMan.HasComponent<SpeechComponent>(host), Is.False);
            Assert.That(SEntMan.HasComponent<HandsComponent>(host), Is.False);
            Assert.That(SEntMan.HasComponent<ActionsComponent>(host), Is.False);
            Assert.That(SEntMan.GetComponent<PhysicsComponent>(host).Mass, Is.EqualTo(originalMass));

            // Native components must survive possession, and closet operation must respect real storage rules.
            var closet = SEntMan.SpawnEntity("ClosetTool", map.MapCoords);
            var storage = SEntMan.GetComponent<EntityStorageComponent>(closet);
            state.PossessAfter = default;
            Assert.That(system.TryPossess((spirit, state), closet), Is.True);
            var wasOpen = storage.Open;
            Assert.That(Perform<PoltergeistUseActionEvent>(spirit, new()).Handled, Is.True);
            Assert.That(storage.Open, Is.EqualTo(!wasOpen));
            system.Leave((spirit, state));
            Assert.That(SEntMan.GetComponent<EntityStorageComponent>(closet), Is.SameAs(storage));

            var flashlight = SEntMan.SpawnEntity("FlashlightLantern", map.MapCoords);
            var light = SEntMan.GetComponent<HandheldLightComponent>(flashlight);
            var hadActions = SEntMan.HasComponent<ActionsComponent>(flashlight);
            state.PossessAfter = default;
            Assert.That(system.TryPossess((spirit, state), flashlight), Is.True);
            Assert.That(light.Activated, Is.False);
            Assert.That(Perform<PoltergeistUseActionEvent>(spirit, new()).Handled, Is.True);
            Assert.That(light.Activated, Is.True);
            Assert.That(SEntMan.System<SharedPointLightSystem>().TryGetLight(flashlight, out var lamp), Is.True);
            Assert.That(lamp!.Enabled, Is.True, "Turning on a light must enable its actual light, not only its visual prefix.");
            system.Leave((spirit, state));
            Assert.That(SEntMan.GetComponent<HandheldLightComponent>(flashlight), Is.SameAs(light));
            Assert.That(SEntMan.HasComponent<ActionsComponent>(flashlight), Is.EqualTo(hadActions), "Native flashlight components must be preserved.");

            state.PossessAfter = default;
            Assert.That(system.TryPossess((spirit, state), host), Is.True);
            SEntMan.DeleteEntity(host);
            Assert.That(state.Host, Is.Null, "Destroying a vessel must release its spirit.");
            Assert.That(minds.GetMind(spirit), Is.EqualTo(mind));

            host = SEntMan.SpawnEntity("Crowbar", map.MapCoords);
            var living = SEntMan.SpawnEntity("MobHuman", map.MapCoords);
            Assert.That(system.CanPossess(living), Is.False);
            state.PossessAfter = default;
            Assert.That(system.TryPossess((spirit, state), host), Is.True);
            scanner = SEntMan.SpawnEntity("PoltergeistDetector", map.MapCoords);
            chaplain = living;
            var seal = new AfterInteractEvent(chaplain, scanner, host, SEntMan.GetComponent<TransformComponent>(host).Coordinates, true);
            SEntMan.EventBus.RaiseLocalEvent(scanner, seal);
            Assert.That(seal.Handled, Is.True);
            var possessed = SEntMan.GetComponent<PoltergeistPossessedComponent>(host);
            Assert.That(possessed.SealedUntil, Is.GreaterThan(Server.ResolveDependency<IGameTiming>().CurTime));
            Assert.That(Perform<PoltergeistLeaveActionEvent>(spirit, new()).Handled, Is.False);
            Assert.That(Perform<PoltergeistKnockActionEvent>(spirit, new()).Handled, Is.False);
            var other = SEntMan.SpawnEntity("Crowbar", map.MapCoords);
            state.PossessAfter = default;
            Assert.That(system.TryPossess((spirit, state), other), Is.False);

            if (useBible)
            {
                SEntMan.EnsureComponent<BibleUserComponent>(chaplain);
                var bible = SEntMan.SpawnEntity("Bible", map.MapCoords);
                Assert.That(SEntMan.System<SharedHandsSystem>().TryPickupAnyHand(chaplain, bible), Is.True);
                var ritual = new InteractUsingEvent(chaplain, bible, host, SEntMan.GetComponent<TransformComponent>(host).Coordinates);
                SEntMan.EventBus.RaiseLocalEvent(host, ritual);
                Assert.That(ritual.Handled, Is.True);
            }
            else
            {
                // Everyone can perform a slow ritual after a detector has trapped the spirit.
                var verbs = new GetVerbsEvent<AlternativeVerb>(chaplain, host, null, null, true, true, true, new());
                SEntMan.EventBus.RaiseLocalEvent(host, verbs);
                Assert.That(verbs.Verbs.Count, Is.EqualTo(1));
                verbs.Verbs.Single().Act!();
            }
        });

        await Pair.RunSeconds(useBible ? 5 : 9);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.EntityExists(spirit), Is.False, "The completed ritual must banish the spirit.");
            Assert.That(SEntMan.EntityExists(host), Is.True, "Exorcism must preserve the vessel.");
            Assert.That(SEntMan.HasComponent<PoltergeistPossessedComponent>(host), Is.False);
            Assert.That(SEntMan.HasComponent<MindContainerComponent>(host), Is.False);
        });
    }

    private T Perform<T>(EntityUid spirit, T ev) where T : BaseActionEvent
    {
        var state = SEntMan.GetComponent<PoltergeistComponent>(spirit);
        var actions = SEntMan.System<SharedActionsSystem>();
        var action = state.Actions.First(uid => actions.GetEvent(uid) is T);
        actions.PerformAction(state.Host ?? spirit, (action, SEntMan.GetComponent<ActionComponent>(action)), ev);
        return ev;
    }
}
