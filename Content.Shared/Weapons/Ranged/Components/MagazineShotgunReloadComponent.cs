using Content.Shared.DoAfter;
using Content.Shared.Weapons.Ranged.Systems;

namespace Content.Shared.Weapons.Ranged.Components;

[RegisterComponent, Access(typeof(SharedGunSystem))]
public sealed partial class MagazineShotgunReloadComponent : Component
{
    [DataField]
    public float RunningReloadDelayMultiplier = 2f;

    [DataField]
    public float RunningSpeedThreshold = 0.5f;

    public DoAfterId? ReloadDoAfter;
}
