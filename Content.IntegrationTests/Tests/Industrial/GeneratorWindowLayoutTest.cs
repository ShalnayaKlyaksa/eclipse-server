using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client.Power.Generator;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.Power;
using Content.Shared.Power.Generator;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Localization;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class GeneratorWindowLayoutTest : GameTest
{
    [TestCase("IndustrialCoalGenerator", false, false)]
    [TestCase("IndustrialMicroBlackHoleGenerator", true, false)]
    [TestCase("IndustrialFusionReactor", false, true)]
    public async Task SettingsDoNotOverlapTelemetry(string prototype, bool gravity, bool fusion)
    {
        await Client.WaitAssertion(() =>
        {
            var localization = Client.ResolveDependency<ILocalizationManager>();
            var previousCulture = localization.DefaultCulture;
            localization.SetCulture(CultureInfo.GetCultureInfo("ru-RU"));
            var entity = CEntMan.Spawn(prototype);
            try
            {
                using var window = new GeneratorWindow();
                window.SetEntity(entity);
                var fuel = CEntMan.GetComponent<FuelGeneratorComponent>(entity);
                window.Update(new PortableGeneratorComponentBuiState(fuel, 6.5f, false, (12000, 22000))
                {
                    On = true,
                    Operating = new PowerPlantOperatingState
                    {
                        Condition = PowerPlantCondition.Warming,
                        GravitySource = gravity,
                        FusionNominalPower = fusion ? 3000f : null,
                        RequiredFields = gravity ? 4 : 0,
                        FieldCount = gravity ? 4 : 0,
                        Temperature = 390f,
                        ShutdownTemperature = 850f,
                        Warmup = 0.5f,
                    },
                });

                var dimensions = window.MinSize;
                window.SetSize = dimensions;
                window.Measure(dimensions);
                window.Arrange(new UIBox2(Vector2.Zero, dimensions));

                var settings = window.FindControl<BoxContainer>("GeneratorSettings");
                var telemetry = window.FindControl<PanelContainer>("OperatingPanel");
                Assert.That(settings.GlobalRect.Bottom, Is.LessThan(telemetry.GlobalRect.Top));
                Assert.That(telemetry.Size.Y, Is.GreaterThan(0));

                foreach (var row in settings.Children.Where(child => child.Visible))
                {
                    var children = row.Children.Where(child => child.Visible).ToArray();
                    for (var i = 1; i < children.Length; i++)
                        Assert.That(children[i].GlobalRect.Left, Is.GreaterThanOrEqualTo(children[i - 1].GlobalRect.Right));
                    Assert.That(children[^1].GlobalRect.Right, Is.LessThanOrEqualTo(settings.GlobalRect.Right + 1));
                }
            }
            finally
            {
                CEntMan.DeleteEntity(entity);
                localization.SetCulture(previousCulture);
            }
        });
    }
}
