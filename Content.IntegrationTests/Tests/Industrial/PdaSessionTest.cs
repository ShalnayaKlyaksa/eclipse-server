using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.PDA;
using Content.Client.CartridgeLoader.Cartridges;
using Content.IntegrationTests.Fixtures;
using Content.Server.CartridgeLoader;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;
using ServerUi = Robust.Server.GameObjects.UserInterfaceSystem;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class PdaSessionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T) target.GetType().GetField(name, Private)!.GetValue(target)!;
    private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);

    [Test]
    public async Task ReopeningTheActualPdaPreservesRunningAppsCalculatorAndCartridgeDrafts()
    {
        var map = await Pair.CreateTestMap();
        EntityUid device = default; EntityUid actor = default; NetEntity netDevice = default; NetEntity netActor = default; NetEntity netNotes = default;
        PdaClientSession cached = null; PdaCalculatorControl calculator = null; NotekeeperUiFragment notes = null;
        try
        {
            await Server.WaitAssertion(() =>
            {
                actor = SEntMan.SpawnEntity("MobHuman", map.GridCoords); device = SEntMan.SpawnEntity("PassengerPDA", map.GridCoords);
                Server.PlayerMan.SetAttachedEntity(ServerSession!, actor);
                netDevice = SEntMan.GetNetEntity(device); netActor = SEntMan.GetNetEntity(actor);
                var program = SEntMan.System<CartridgeLoaderSystem>().GetInstalled(device).First(uid => SEntMan.GetComponent<CartridgeComponent>(uid).ProgramName == "notekeeper-program-name");
                netNotes = SEntMan.GetNetEntity(program);
                Assert.That(SEntMan.System<ServerUi>().TryOpenUi(device, PdaUiKey.Key, actor), Is.True);
            });
            await Pair.RunTicksSync(25);
            await Client.WaitAssertion(() =>
            {
                cached = CEntMan.System<PdaSystem>().GetSession(CEntMan.GetEntity(netDevice), CEntMan.GetEntity(netActor));
                Call(cached.Menu, "OpenCartridgeApp", CEntMan.GetEntity(netNotes), "Notes");
            });
            await Pair.RunTicksSync(25);
            await Client.WaitAssertion(() =>
            {
                notes = (NotekeeperUiFragment) cached.Programs[CEntMan.GetEntity(netNotes)].GetUIFragmentRoot();
                notes.FindControl<TextEdit>("Editor").InsertAtCursor("Unsaved note survives power cycling");
                Call(cached.Menu, "OpenCalculator");
                calculator = (PdaCalculatorControl) Field<Dictionary<string, Control>>(cached.Menu, "_builtins")["calculator"];
                calculator.Press("7"); calculator.Press("+"); calculator.Press("2"); calculator.Evaluate();
                Assert.That(Field<LineEdit>(calculator, "_expression").Text, Is.EqualTo("9"));
                Assert.That(cached.Menu.RunningAppCount, Is.EqualTo(2));
                cached.Menu.Close();
            });
            await Pair.RunTicksSync(25);
            await Server.WaitAssertion(() => Assert.That(SEntMan.System<ServerUi>().IsUiOpen(device, PdaUiKey.Key, actor), Is.False));
            await Server.WaitAssertion(() => Assert.That(SEntMan.System<ServerUi>().TryOpenUi(device, PdaUiKey.Key, actor), Is.True));
            await Pair.RunTicksSync(25);
            await Client.WaitAssertion(() =>
            {
                var reopened = CEntMan.System<PdaSystem>().GetSession(CEntMan.GetEntity(netDevice), CEntMan.GetEntity(netActor));
                Assert.That(reopened, Is.SameAs(cached));
                Assert.That(reopened.Menu.RunningAppCount, Is.EqualTo(2));
                Assert.That(Field<Dictionary<string, Control>>(reopened.Menu, "_builtins")["calculator"], Is.SameAs(calculator));
                Assert.That(Field<LineEdit>(calculator, "_expression").Text, Is.EqualTo("9"));
                Assert.That(Rope.Collapse(notes.FindControl<TextEdit>("Editor").TextRope), Does.Contain("survives power cycling"));
                Call(reopened.Menu, "OpenBank"); Call(reopened.Menu, "OpenMessenger"); Call(reopened.Menu, "OpenPersonal");
                Call(reopened.Menu, "OpenBuiltin", "flappy", new PocketFlappyControl());
                Assert.That(reopened.Menu.RunningAppCount, Is.EqualTo(6));
                Call(reopened.Menu, "OpenBuiltin", "blocks", new PocketBlockinatorControl());
                Assert.That(reopened.Menu.RunningAppCount, Is.EqualTo(6));
                Assert.That(Field<Dictionary<string, Control>>(reopened.Menu, "_builtins").ContainsKey("blocks"), Is.False);
                Call(reopened.Menu, "CloseTask", "bank");
                Call(reopened.Menu, "OpenBuiltin", "blocks", new PocketBlockinatorControl());
                Assert.That(reopened.Menu.RunningAppCount, Is.EqualTo(6));
                Assert.That(Field<Dictionary<string, Control>>(reopened.Menu, "_builtins").ContainsKey("blocks"), Is.True);
                Call(reopened.Menu, "OpenCartridgeApp", CEntMan.GetEntity(netNotes), "Notes");
                Call(notes, "Save");
            });
            await Pair.RunTicksSync(25);
            await Server.WaitAssertion(() => Assert.That(SEntMan.GetComponent<Content.Server.CartridgeLoader.Cartridges.NotekeeperCartridgeComponent>(SEntMan.GetEntity(netNotes)).Entries.Single().Body, Does.Contain("survives power cycling"), "A cached note editor must still send through the new BUI after reopening."));
        }
        catch (Exception error) { TestContext.Progress.WriteLine(error); throw; }
    }
}
