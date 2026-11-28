using System.Linq;
using Content.Client._Eclipse.Tutorial;
using Content.IntegrationTests.Fixtures;
using Content.Shared.CCVar;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests.Lobby;

public sealed class TutorialMaintenanceTest : GameTest
{
    [Test]
    public async Task ServerSettingRestoresLessonList()
    {
        var configuration = Server.ResolveDependency<IConfigurationManager>();

        await Client.WaitAssertion(() =>
        {
            using var window = new TutorialWindow(CProtoMan, _ => { });
            var content = window.Contents.Children.Single();
            Assert.That(content, Is.TypeOf<Label>());
            Assert.That(((Label) content).Text, Is.EqualTo("На тех. работах :("));
        });

        try
        {
            await Server.WaitPost(() => configuration.SetCVar(CCVars.TutorialEnabled, true));
            await Pair.RunTicksSync(5);

            await Client.WaitAssertion(() =>
            {
                using var window = new TutorialWindow(CProtoMan, _ => { });
                Assert.That(window.Contents.Children.Single(), Is.TypeOf<ScrollContainer>());
            });
        }
        finally
        {
            await Server.WaitPost(() => configuration.SetCVar(CCVars.TutorialEnabled, false));
            await Pair.RunTicksSync(5);
        }
    }
}
