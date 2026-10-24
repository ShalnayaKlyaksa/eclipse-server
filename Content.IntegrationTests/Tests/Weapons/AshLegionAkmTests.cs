using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Weapons;

public sealed class AshLegionAkmTests : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    [Test]
    public async Task OwnProjectileIsNotDelayedByPvsSpawnBudget()
    {
        var gun = await PlaceInHands("WeaponRifleAshLegionAKM");
        var serverConfig = Server.ResolveDependency<INetConfigurationManager>();
        var clientConfig = Client.ResolveDependency<IConfigurationManager>();
        await Server.WaitPost(() => serverConfig.SetCVar(CVars.NetPVS, true));
        await RunTicks(5);
        await Client.WaitPost(() => clientConfig.SetCVar(CVars.NetPVSEntityBudget, 0));
        await RunTicks(5);

        NetEntity bullet = default;
        await Server.WaitAssertion(() =>
        {
            Assert.That(serverConfig.GetClientCVar(ServerSession.Channel, CVars.NetPVSEntityBudget), Is.Zero);
            var origin = SEntMan.GetCoordinates(PlayerCoords);
            var projectile = SEntMan.SpawnEntity("BulletLightRifle", origin);
            bullet = SEntMan.GetNetEntity(projectile);
            var serverGun = ToServer(gun);
            SGun.Shoot((serverGun, SEntMan.GetComponent<GunComponent>(serverGun)), projectile,
                origin, origin.Offset(new Vector2(100, 0)), out _, SPlayer);
        });

        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            Assert.That(CEntMan.TryGetEntity(bullet, out var entity)
                && CEntMan.HasComponent<ProjectileComponent>(entity), Is.True,
                "The shooter's own projectile was queued behind unrelated PVS entity spawns.");
        });
    }

    [TestCase(1f, 0f)]
    [TestCase(2f, 0f)]
    [TestCase(6f, 0f)]
    [TestCase(0f, 4f)]
    [TestCase(3f, 3f)]
    public async Task ShootRequestFiresPromptlyAndHits(float x, float y)
    {
        await AddAtmosphere();
        var coordinates = SEntMan.GetCoordinates(PlayerCoords).Offset(new Vector2(x, y));
        var target = await SpawnTarget("MobHuman", SEntMan.GetNetCoordinates(coordinates));
        var gun = await PlaceInHands("WeaponRifleAshLegionAKM");
        var serverGun = ToServer(gun);
        await Server.WaitAssertion(() =>
        {
            SGun.UseChambered(serverGun, SEntMan.GetComponent<ChamberMagazineAmmoProviderComponent>(serverGun));
            Assert.That(SEntMan.System<SharedWieldableSystem>().TryWield(serverGun,
                SEntMan.GetComponent<WieldableComponent>(serverGun), SPlayer), Is.True);
        });
        await SetCombatMode(true);
        await RunSeconds(0.5f); // Let the normal pickup cooldown expire and synchronize the loaded gun.

        var ammoBefore = SGun.GetAmmoCount(serverGun);
        var requestTime = STiming.CurTime;
        var targetCoordinates = SEntMan.GetNetCoordinates(coordinates);
        await Client.WaitPost(() => CEntMan.EntityNetManager.SendSystemNetworkMessage(new RequestShootEvent
        {
            Gun = gun,
            Coordinates = targetCoordinates,
            Target = target,
        }));

        for (var tick = 0; tick < 10 && SGun.GetAmmoCount(serverGun) == ammoBefore; tick++)
            await RunTicks(1);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SGun.GetAmmoCount(serverGun), Is.LessThan(ammoBefore),
                $"No shot within {(STiming.CurTime - requestTime).TotalSeconds:F3} seconds of the client request.");
        });

        await RunSeconds(0.4f);
        await Server.WaitAssertion(() =>
        {
            var damage = SEntMan.System<DamageableSystem>().GetTotalDamage(ToServer(target));
            Assert.That(damage, Is.GreaterThan(FixedPoint2.Zero), $"AKM missed a stationary target at ({x}, {y}).");
            var health = SEntMan.GetComponent<AdvancedHealthComponent>(ToServer(target));
            Assert.That(health.BodyParts.Values.Any(part => part.Wounds.Any(wound => wound.Type == WoundType.Gunshot)),
                Is.True, "A registered bullet hit did not produce a gunshot wound.");
        });
    }
}
