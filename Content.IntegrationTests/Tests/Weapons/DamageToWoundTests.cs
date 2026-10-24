using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Weapons;

public sealed class DamageToWoundTests : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [TestCase(false)]
    [TestCase(true)]
    public async Task PhysiologicalDamageDoesNotHideGunshot(bool mixedDamage)
    {
        var map = await Pair.CreateTestMap();
        var entities = Pair.Server.ResolveDependency<IEntityManager>();
        await Pair.Server.WaitAssertion(() =>
        {
            var target = entities.SpawnEntity("MobHuman", map.MapCoords);
            var damage = entities.System<DamageableSystem>();
            damage.TryChangeDamage(target, new DamageSpecifier { DamageDict = new() { ["Poison"] = 30 } });

            var shot = new DamageSpecifier { DamageDict = new() { ["Piercing"] = 19 } };
            if (mixedDamage)
                shot.DamageDict["Poison"] = 30;

            damage.TryChangeDamage(target, shot, ignoreResistances: !mixedDamage);
            var health = entities.GetComponent<AdvancedHealthComponent>(target);
            Assert.That(health.BodyParts.Values.SelectMany(part => part.Wounds)
                .Any(wound => wound.Type == WoundType.Gunshot), Is.True,
                "Physiological damage suppressed an actual piercing injury.");
        });
    }
}
