using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Fixtures;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Commands;
using Content.Shared.CCVar;
using Robust.Client.State;
using Robust.Server.Console;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Commands
{
    [TestFixture]
    [TestOf(typeof(RestartRoundNowCommand))]
    public sealed class RestartRoundNowTest : GameTest
    {
        public override PoolSettings PoolSettings => new PoolSettings
        {
            DummyTicker = false,
            Dirty = true
        };

        [Test]
        [TestCase(true)]
        [TestCase(false)]
        public async Task RestartRoundAfterStart(bool lobbyEnabled)
        {
            var pair = Pair;
            var server = pair.Server;

            var configManager = server.ResolveDependency<IConfigurationManager>();
            var entityManager = server.ResolveDependency<IEntityManager>();
            var gameTicker = entityManager.System<GameTicker>();

            await pair.RunUntilSynced();

            GameTick tickBeforeRestart = default;

            await server.WaitAssertion(() =>
            {
                Assert.That(configManager.GetCVar(CCVars.GameLobbyEnabled), Is.EqualTo(false));
                configManager.SetCVar(CCVars.GameLobbyEnabled, lobbyEnabled);

                Assert.That(gameTicker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

                tickBeforeRestart = entityManager.CurrentTick;

                gameTicker.RestartRound();

                if (lobbyEnabled)
                {
                    Assert.That(gameTicker.RunLevel, Is.Not.EqualTo(GameRunLevel.InRound));
                }
            });

            await pair.RunTicksSync(15);

            await server.WaitAssertion(() =>
            {
                var tickAfterRestart = entityManager.CurrentTick;

                Assert.That(tickBeforeRestart, Is.LessThan(tickAfterRestart));
            });

            await pair.RunUntilSynced();
        }
    }

    [TestOf(typeof(GoLobbyCommand))]
    public sealed class GoLobbyTest : GameTest
    {
        public override PoolSettings PoolSettings => new() { Dirty = true };

        [Test]
        public async Task CharacterSetupLoadsOnDemand()
        {
            var stateManager = Client.Resolve<IStateManager>();
            var console = Server.Resolve<IServerConsoleHost>();
            var ticker = Server.System<GameTicker>();
            await Server.WaitPost(() =>
            {
                // Register a round without loading a station or joining a player before it exists.
                Server.CfgMan.SetCVar(CCVars.GameLobbyEnabled, true);
                Server.CfgMan.SetCVar(CCVars.GameDummyTicker, false);
                ticker.RestartRound();
                Server.CfgMan.SetCVar(CCVars.GameDummyTicker, true);
                Server.CfgMan.SetCVar(CCVars.GameLobbyEnabled, false);
            });
            await Pair.Connect();

            var map = await Pair.CreateTestMap();
            await Server.WaitPost(() =>
            {
                var player = Pair.Player!;
                Server.PlayerMan.SetAttachedEntity(player, SSpawnAtPosition("MobImperial", map.GridCoords));
                ticker.PlayerJoinGame(player, silent: true);
            });
            await Pair.RunTicksSync(15);
            Assert.That(stateManager.CurrentState, Is.TypeOf<GameplayState>());

            await Server.WaitPost(() =>
            {
                Server.CfgMan.SetCVar(CCVars.GameDummyTicker, false);
                console.ExecuteCommand("golobby");
            });
            await Pair.RunTicksSync(15);

            await Client.WaitAssertion(() =>
            {
                Assert.That(stateManager.CurrentState, Is.TypeOf<LobbyState>());
                var lobby = (LobbyState) stateManager.CurrentState;
                var setup = lobby.Lobby!.CharacterSetupState;
                Assert.That(setup.ChildCount, Is.Zero);

                lobby.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);

                Assert.That(setup.ChildCount, Is.EqualTo(1));
                Assert.That(setup.Visible, Is.True);
            });
            await Pair.Disconnect();
        }
    }
}
