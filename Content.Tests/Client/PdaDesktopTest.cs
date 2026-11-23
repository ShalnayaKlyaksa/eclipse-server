using System.Text.Json;
using System.Collections.Generic;
using Content.Client.PDA;
using Content.Shared.PDA;
using NUnit.Framework;

namespace Content.Tests.Client;

[TestFixture]
public sealed class PdaDesktopTest
{
    [TestCase("xaoser", true)]
    [TestCase("crew-17.omega", true)]
    [TestCase("ab", false)]
    [TestCase("Капитан", false)]
    [TestCase("with spaces", false)]
    [TestCase("Alice", false)]
    public void PublicIdsHaveAConsistentWireFormat(string id, bool valid) => Assert.That(PdaPublicId.Valid(id), Is.EqualTo(valid));

    [Test]
    public void FlightCanRestoreItsBestAndRestartWithoutDiscardingIt()
    {
        var game = new PocketFlappyModel();
        game.LoadBest(23);
        game.LoadBest(-10);
        game.Flap(); game.Tick(.02f);
        Assert.That(game.Distance, Is.GreaterThan(0));
        game.Restart();
        Assert.That(game.Best, Is.EqualTo(23));
        Assert.That(game.Distance, Is.Zero);
        game.Tick(10);
        Assert.That(game.Y, Is.EqualTo(140));
    }

    [Test]
    public void DesktopMoveSwapsOccupiedCellsAndSurvivesReloadAndResize()
    {
        var saved = new Dictionary<string, int> { ["bank"] = 0, ["calculator"] = 1, ["mail"] = 19, ["hidden"] = -1 };
        string[] apps = ["new-app", "bank", "calculator", "mail", "hidden"];
        var visible = PdaDesktopLayout.Arrange(apps, saved, 5, 4);
        Assert.That(visible["new-app"], Is.EqualTo(2));
        Assert.That(visible.ContainsKey("hidden"), Is.False);
        PdaDesktopLayout.Move(saved, visible, "bank", 1);
        Assert.That(saved["calculator"], Is.Zero);
        Assert.That(saved["bank"], Is.EqualTo(1));
        var restored = JsonSerializer.Deserialize<Dictionary<string, int>>(JsonSerializer.Serialize(saved))!;
        var narrow = PdaDesktopLayout.Arrange(apps, restored, 2, 4);
        Assert.That(narrow["mail"], Is.Not.EqualTo(19));
        Assert.That(restored["mail"], Is.EqualTo(19));
        var reopened = PdaDesktopLayout.Arrange(apps, restored, 5, 4);
        Assert.That(reopened["bank"], Is.EqualTo(1));
        Assert.That(reopened["calculator"], Is.Zero);
        Assert.That(reopened["mail"], Is.EqualTo(19));
    }

    [Test]
    public void FlightWaitsForInputScoresOnceAndKeepsBestAfterRestart()
    {
        var game = new PocketFlappyModel();
        game.Tick(1);
        Assert.That(game.Y, Is.EqualTo(140));
        game.Pipes.Add(new PocketFlappyModel.Pipe(0, 140));
        game.Flap();
        game.Tick(.01f);
        game.Tick(.01f);
        Assert.That(game.Score, Is.EqualTo(1));
        for (var i = 0; i < 300; i++) game.Tick(.02f);
        Assert.That(game.GameOver, Is.True);
        var y = game.Y;
        game.Tick(.1f);
        Assert.That(game.Y, Is.EqualTo(y));
        game.Restart();
        Assert.That(game.Best, Is.EqualTo(1));
        Assert.That(game.Score, Is.Zero);
        Assert.That(game.Pipes, Is.Empty);
        Assert.That(game.Started, Is.False);
    }

    [Test]
    public void DesktopProfileRoundTripsAccountEntitlementsAndHiddenShortcuts()
    {
        var profile = new PdaDesktopProfile
        {
            Wallpaper = "aurora", Owned = ["orbit", "aurora", "studio"],
            Card = true, Photo = true, CardText = "Personal note",
            Positions = new() { ["bank"] = 3, ["calculator"] = -1 },
        };
        var options = new JsonSerializerOptions { IncludeFields = true };
        var restored = JsonSerializer.Deserialize<PdaDesktopProfile>(JsonSerializer.Serialize(profile, options), options);
        Assert.That(restored.Wallpaper, Is.EqualTo("aurora"));
        Assert.That(restored.Owned, Is.EqualTo(profile.Owned));
        Assert.That(restored.Positions["calculator"], Is.EqualTo(-1));
        Assert.That(restored.CardText, Is.EqualTo(profile.CardText));
        Assert.That(restored.Photo, Is.True);
    }
}
