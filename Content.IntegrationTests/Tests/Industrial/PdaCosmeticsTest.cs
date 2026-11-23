using System;
using System.Text.Json;
using Content.IntegrationTests.Fixtures;
using Content.Server.Eclipse.Economy;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Industrial;

public sealed class PdaCosmeticsTest : GameTest
{
    [Test]
    public async Task CosmeticsDebitOnceAndProfilesCannotGrantPaidContent()
    {
        await Server.WaitAssertion(() =>
        {
            var wallet = SEntMan.System<EclipseCurrencyManager>();
            var user = new NetUserId(Guid.NewGuid());
            wallet.AddCurrency(user, 500, 10);
            Assert.That(wallet.SetPdaPublicId(user, 0, "test.user"), Is.True);
            Assert.That(wallet.SetPdaPublicId(user, 1, "test.user"), Is.False);
            Assert.That(wallet.SetPdaPublicId(user, 1, "test.other"), Is.True);
            var secondUser = new NetUserId(Guid.NewGuid());
            wallet.AddCurrency(secondUser, 1, 0);
            Assert.That(wallet.SetPdaPublicId(secondUser, 0, "test.user"), Is.False);
            Assert.That(wallet.SetPdaPublicId(user, 0, "invalid id"), Is.False);
            Assert.That(wallet.TrySpend(user, -10, 0), Is.False);
            Assert.That(wallet.TryBuyDesktopCosmetic(user, "missing", false), Is.False);
            Assert.That(wallet.TryBuyDesktopCosmetic(user, "aurora", false), Is.True);
            Assert.That(wallet.GetBalance(user), Is.EqualTo((380, 10)));
            Assert.That(wallet.TryBuyDesktopCosmetic(user, "aurora", true), Is.False);
            Assert.That(wallet.GetBalance(user), Is.EqualTo((380, 10)));
            var forged = new PdaDesktopProfile { Wallpaper = "sunset", Owned = ["sunset", "studio"], Card = true };
            Assert.That(wallet.SaveDesktop(user, forged), Is.False);
            forged.Wallpaper = "aurora";
            Assert.That(wallet.SaveDesktop(user, forged), Is.True);
            Assert.That(wallet.GetPdaPublicId(user, 0), Is.EqualTo("test.user"));
            Assert.That(wallet.GetPdaPublicId(user, 1), Is.EqualTo("test.other"));
            Assert.That(wallet.GetDesktop(user).Card, Is.False);
            Assert.That(wallet.GetDesktop(user).Owned, Does.Not.Contain("studio"));
            Assert.That(wallet.TryBuyDesktopCosmetic(user, "studio", true), Is.True);
            Assert.That(wallet.GetBalance(user), Is.EqualTo((380, 4)));
            Assert.That(wallet.SaveDesktop(user, forged), Is.True);
            Assert.That(wallet.GetDesktop(user).Card, Is.True);
            forged.Positions["bank"] = 19;
            forged.Positions["calculator"] = -1;
            Assert.That(wallet.SaveDesktop(user, forged), Is.True);
            // Verify the on-disk account snapshot, not just the manager's in-memory object.
            using (var file = Server.ResolveDependency<IResourceManager>().UserData.OpenRead(new ResPath("/eclipse_currency.json")))
            using (var json = JsonDocument.Parse(file))
            {
                var desktop = json.RootElement.GetProperty(user.UserId.ToString()).GetProperty("Desktop");
                Assert.That(desktop.GetProperty("Positions").GetProperty("bank").GetInt32(), Is.EqualTo(19));
                Assert.That(desktop.GetProperty("Positions").GetProperty("calculator").GetInt32(), Is.EqualTo(-1));
            }
            forged.Positions["bank"] = 2;
            Assert.That(wallet.GetDesktop(user).Positions["bank"], Is.EqualTo(19));
            forged.Positions["bank"] = 64;
            Assert.That(wallet.SaveDesktop(user, forged), Is.False);
            var empty = new NetUserId(Guid.NewGuid());
            Assert.That(wallet.TryBuyDesktopCosmetic(empty, "sunset", false), Is.False);
        });
    }
}
