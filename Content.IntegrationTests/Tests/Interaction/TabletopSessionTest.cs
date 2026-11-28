using System.Linq;
using System.Numerics;
using Content.Client.Tabletop.UI;
using Content.Client.Viewport;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Tabletop.Components;
using Content.Shared.Interaction;
using Content.Shared.Tabletop.Components;
using Robust.Client.ComponentTrees;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Interaction;

public sealed class TabletopSessionTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase("ChessBoard", "ChessBoardTabletop", 34)]
    [TestCase("BackgammonBoard", "BackgammonBoardTabletop", 30)]
    [TestCase("CheckerBoard", "CheckerBoardTabletop", 42)]
    [TestCase("ParchisBoard", "ParchisBoardTabletop", 16)]
    [EnsureCVar(Side.Server, typeof(CVars), nameof(CVars.NetPVS), true)]
    public async Task WindowReceivesBoardAndPiecesFromItsCamera(string game, string boardPrototype, int pieces)
    {
        var map = await Pair.CreateTestMap();
        EntityUid player = default;
        EntityUid gameItem = default;
        NetEntity camera = default;
        await Server.WaitAssertion(() =>
        {
            player = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            gameItem = SEntMan.SpawnEntity(game, map.GridCoords);
            var session = Server.PlayerMan.Sessions.Single();
            Server.PlayerMan.SetAttachedEntity(session, player);
        });
        await Pair.RunTicksSync(10);
        await Server.WaitAssertion(() =>
        {
            SEntMan.EventBus.RaiseLocalEvent(gameItem, new ActivateInWorldEvent(player, gameItem, true));
            var session = SEntMan.GetComponent<TabletopGameComponent>(gameItem).Session!;
            camera = SEntMan.GetNetEntity(session.Players[ServerSession!].Camera);
        });
        await Pair.RunTicksSync(40);
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var window = ui.WindowRoot.Children.OfType<TabletopWindow>().Single();
            Assert.That(window.IsOpen, Is.True);
            var viewport = window.FindControl<ScalingViewport>("ScalingVp");
            var eye = viewport.Eye!;
            Assert.That(eye, Is.SameAs(CEntMan.GetComponent<EyeComponent>(CEntMan.GetEntity(camera)).Eye));

            var tree = CEntMan.System<SpriteTreeSystem>();
            tree.FrameUpdate(0);
            var size = (Vector2) viewport.ViewportSize / EyeManager.PixelsPerMeter * eye.Zoom;
            var sprites = tree.QueryAabb(eye.Position.MapId, Box2.CenteredAround(eye.Position.Position, size));
            Assert.That(sprites.Count(sprite => CEntMan.GetComponent<MetaDataComponent>(sprite.Uid).EntityPrototype?.ID == boardPrototype), Is.EqualTo(1));
            Assert.That(sprites.Count(sprite => CEntMan.HasComponent<TabletopDraggableComponent>(sprite.Uid)), Is.EqualTo(pieces));
            window.Close();
        });
    }
}
