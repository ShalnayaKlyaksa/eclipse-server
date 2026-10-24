using Content.Shared.Examine;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Input;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Movement.Components;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;
using Robust.Shared.Input.Binding;
using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared.Weapons.Ranged.Systems;

public abstract partial class SharedGunSystem
{
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;

    private static readonly TimeSpan BeltMagazineReloadDelay = TimeSpan.FromSeconds(1);

    protected virtual void InitializeMagazine()
    {
        CommandBinds.Builder
            .Bind(ContentKeyFunctions.ReloadFromBelt, InputCmdHandler.FromDelegate(HandleReloadFromBelt, handle: false, outsidePrediction: false))
            .Register<SharedGunSystem>();

        SubscribeLocalEvent<MagazineAmmoProviderComponent, MapInitEvent>(OnMagazineMapInit);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, TakeAmmoEvent>(OnMagazineTakeAmmo);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, GetAmmoCountEvent>(OnMagazineAmmoCount);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, GetVerbsEvent<AlternativeVerb>>(OnMagazineVerb);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, EntInsertedIntoContainerMessage>(OnMagazineSlotChange);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, EntRemovedFromContainerMessage>(OnMagazineSlotChange);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, UseInHandEvent>(OnMagazineUse);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, ExaminedEvent>(OnMagazineExamine);
        SubscribeLocalEvent<MagazineAmmoProviderComponent, BeltMagazineReloadDoAfterEvent>(OnBeltMagazineReloadDoAfter);
        SubscribeLocalEvent<ChamberMagazineAmmoProviderComponent, BeltMagazineReloadDoAfterEvent>(OnBeltChamberMagazineReloadDoAfter);
        SubscribeLocalEvent<MagazineShotgunReloadComponent, ShotAttemptedEvent>(OnMagazineShotgunShotAttempted);
        SubscribeLocalEvent<MagazineShotgunReloadComponent, BeltMagazineReloadDoAfterEvent>(OnMagazineShotgunReloadDoAfter);
    }

    public override void Shutdown()
    {
        CommandBinds.Unregister<SharedGunSystem>();
        base.Shutdown();
    }

    private void HandleReloadFromBelt(ICommonSession? session)
    {
        if (session?.AttachedEntity is not { Valid: true } user || !Exists(user))
            return;

        if (!TryGetGunForReload(user, out var gun) ||
            !HasMagazineReloadProvider(gun) ||
            !_actionBlockerSystem.CanInteract(user, gun))
        {
            return;
        }

        var hasHeldMagazine = TryFindHeldMagazine(gun, user, out var heldMagazine, out var heldAmmo);
        var hasInventoryMagazine = TryFindInventoryMagazine(gun, user, out var storageEnt, out _, out _, out var inventoryAmmo);

        if (hasHeldMagazine && (!hasInventoryMagazine || heldAmmo >= inventoryAmmo))
        {
            if (!_slots.TryGetSlot(gun, MagazineSlot, out var magazineSlot))
            {
                return;
            }

            var oldMagazine = magazineSlot.Item;
            if (oldMagazine != null &&
                !_slots.TryEject(gun, magazineSlot, user, out _, excludeUserAudio: true))
            {
                return;
            }

            if (!_hands.TryDrop(user, heldMagazine, checkActionBlocker: false) ||
                !_slots.TryInsert(gun, magazineSlot, heldMagazine, user, excludeUserAudio: true))
            {
                _hands.TryPickupAnyHand(user, heldMagazine, checkActionBlocker: false);

                if (oldMagazine != null)
                    _slots.TryInsert(gun, magazineSlot, oldMagazine.Value, user, excludeUserAudio: true);

                return;
            }

            if (oldMagazine != null)
                _hands.TryPickupAnyHand(user, oldMagazine.Value, checkActionBlocker: false);

            return;
        }

        if (!hasInventoryMagazine)
        {
            PopupSystem.PopupClient(Loc.GetString("gun-magazine-reload-no-magazine"), user, user);
            return;
        }

        var delay = GetBeltMagazineReloadDelay(gun, user);
        var doAfterArgs = new DoAfterArgs(EntityManager,
            user,
            delay,
            new BeltMagazineReloadDoAfterEvent(),
            gun,
            target: storageEnt,
            used: gun)
        {
            BreakOnMove = false,
            BreakOnDamage = false,
            NeedHand = true,
        };

        if (_doAfter.TryStartDoAfter(doAfterArgs, out var doAfterId) &&
            TryComp<MagazineShotgunReloadComponent>(gun, out var magazineShotgunReload))
        {
            magazineShotgunReload.ReloadDoAfter = doAfterId;
        }
    }

    private TimeSpan GetBeltMagazineReloadDelay(EntityUid gun, EntityUid user)
    {
        if (!TryComp<MagazineShotgunReloadComponent>(gun, out var magazineShotgunReload) ||
            !IsUserRunning(user, magazineShotgunReload))
        {
            return BeltMagazineReloadDelay;
        }

        return TimeSpan.FromSeconds(BeltMagazineReloadDelay.TotalSeconds * magazineShotgunReload.RunningReloadDelayMultiplier);
    }

    private bool IsUserRunning(EntityUid user, MagazineShotgunReloadComponent component)
    {
        var walkSpeed = MovementSpeedModifierComponent.DefaultBaseWalkSpeed;
        var sprintSpeed = MovementSpeedModifierComponent.DefaultBaseSprintSpeed;

        if (TryComp<MovementSpeedModifierComponent>(user, out var movement))
        {
            walkSpeed = movement.CurrentWalkSpeed;
            sprintSpeed = movement.CurrentSprintSpeed;
        }

        var runningThreshold = walkSpeed + (sprintSpeed - walkSpeed) * component.RunningSpeedThreshold;
        return Physics.GetMapLinearVelocity(user).Length() > runningThreshold;
    }

    private bool TryGetGunForReload(EntityUid user, out Entity<GunComponent> gun)
    {
        if (TryGetGun(user, out gun) && HasMagazineReloadProvider(gun))
            return true;

        if (!TryComp<HandsComponent>(user, out var hands))
            return false;

        foreach (var held in _hands.EnumerateHeld((user, hands)))
        {
            if (TryComp(held, out GunComponent? gunComp) && HasMagazineReloadProvider(held))
            {
                gun = (held, gunComp);
                return true;
            }
        }

        return false;
    }

    private bool HasMagazineReloadProvider(EntityUid gun)
    {
        return HasComp<MagazineAmmoProviderComponent>(gun) ||
               HasComp<ChamberMagazineAmmoProviderComponent>(gun);
    }

    private bool TryFindHeldMagazine(EntityUid gun, EntityUid user, out EntityUid magazine, out int ammoCount)
    {
        magazine = default;
        ammoCount = -1;

        if (!TryComp<HandsComponent>(user, out var hands) ||
            !_slots.TryGetSlot(gun, MagazineSlot, out var magazineSlot))
        {
            return false;
        }

        foreach (var held in _hands.EnumerateHeld((user, hands)))
        {
            if (held == gun ||
                !_slots.CanInsert(gun, held, user, magazineSlot, swap: true))
            {
                continue;
            }

            var count = GetReloadMagazineAmmoCount(held);
            if (count <= ammoCount)
                continue;

            magazine = held;
            ammoCount = count;
        }

        return ammoCount >= 0;
    }

    private bool TryFindInventoryMagazine(
        EntityUid gun,
        EntityUid user,
        out EntityUid storageEnt,
        out StorageComponent storage,
        out EntityUid magazine,
        out int ammoCount)
    {
        storageEnt = default;
        storage = default!;
        magazine = default;
        ammoCount = -1;

        TryFindMagazineInSlot(gun, user, "belt", out storageEnt, out storage, out magazine, out ammoCount);

        if (_inventorySystem.TryGetContainerSlotEnumerator(user, out var enumerator))
        {
            while (enumerator.MoveNext(out var container, out var slot))
            {
                if (slot.Name == "belt" ||
                    container.ContainedEntity is not { } contained ||
                    !TryComp<StorageComponent>(contained, out var storageComp) ||
                    !TryFindMagazineInStorage(gun, user, (contained, storageComp), out var candidate, out var candidateAmmo) ||
                    candidateAmmo <= ammoCount)
                {
                    continue;
                }

                storageEnt = contained;
                storage = storageComp;
                magazine = candidate;
                ammoCount = candidateAmmo;
            }
        }

        return ammoCount >= 0;
    }

    private bool TryFindMagazineInSlot(
        EntityUid gun,
        EntityUid user,
        string slot,
        out EntityUid storageEnt,
        out StorageComponent storage,
        out EntityUid magazine,
        out int ammoCount)
    {
        storageEnt = default;
        storage = default!;
        magazine = default;
        ammoCount = -1;

        if (!_inventorySystem.TryGetSlotEntity(user, slot, out var slotEnt) ||
            slotEnt is not { } storageUid ||
            !TryComp<StorageComponent>(storageUid, out var storageComp) ||
            !TryFindMagazineInStorage(gun, user, (storageUid, storageComp), out magazine, out ammoCount))
        {
            return false;
        }

        storageEnt = storageUid;
        storage = storageComp;
        return true;
    }

    private bool TryFindMagazineInStorage(
        EntityUid gun,
        EntityUid user,
        Entity<StorageComponent> storage,
        out EntityUid magazine,
        out int ammoCount)
    {
        magazine = default;
        ammoCount = -1;

        if (!_slots.TryGetSlot(gun, MagazineSlot, out var magazineSlot))
            return false;

        for (var i = storage.Comp.Container.ContainedEntities.Count - 1; i >= 0; i--)
        {
            var candidate = storage.Comp.Container.ContainedEntities[i];

            if (!_slots.CanInsert(gun, candidate, user, magazineSlot, swap: true))
                continue;

            var count = GetReloadMagazineAmmoCount(candidate);
            if (count <= ammoCount)
                continue;

            magazine = candidate;
            ammoCount = count;
        }

        return ammoCount >= 0;
    }

    private int GetReloadMagazineAmmoCount(EntityUid magazine)
    {
        var ev = new GetAmmoCountEvent();
        RaiseLocalEvent(magazine, ref ev, false);
        return ev.Count;
    }

    private void OnBeltMagazineReloadDoAfter(EntityUid uid, MagazineAmmoProviderComponent component, BeltMagazineReloadDoAfterEvent args)
    {
        OnMagazineReloadDoAfter(uid, args);
    }

    private void OnBeltChamberMagazineReloadDoAfter(EntityUid uid, ChamberMagazineAmmoProviderComponent component, BeltMagazineReloadDoAfterEvent args)
    {
        OnMagazineReloadDoAfter(uid, args);
    }

    private void OnMagazineShotgunShotAttempted(EntityUid uid, MagazineShotgunReloadComponent component, ref ShotAttemptedEvent args)
    {
        if (component.ReloadDoAfter == null)
            return;

        _doAfter.Cancel(component.ReloadDoAfter, force: true);
        component.ReloadDoAfter = null;
    }

    private void OnMagazineShotgunReloadDoAfter(EntityUid uid, MagazineShotgunReloadComponent component, BeltMagazineReloadDoAfterEvent args)
    {
        component.ReloadDoAfter = null;
    }

    private void OnMagazineReloadDoAfter(EntityUid uid, BeltMagazineReloadDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled)
            return;

        args.Handled = true;

        if (!TryGetGunForReload(args.User, out var gun) ||
            gun.Owner != uid ||
            args.Target is not { Valid: true } storageEnt ||
            !TryComp<StorageComponent>(storageEnt, out var storage) ||
            !_slots.TryGetSlot(uid, MagazineSlot, out var magazineSlot) ||
            !TryFindMagazineInStorage(uid, args.User, (storageEnt, storage), out var newMagazine, out _))
        {
            return;
        }

        if (!Containers.Remove(newMagazine, storage.Container))
            return;

        var oldMagazine = magazineSlot.Item;
        if (oldMagazine != null)
        {
            if (!_slots.TryEject(uid, magazineSlot, args.User, out var ejected, excludeUserAudio: true) ||
                !_storage.Insert(storageEnt, ejected.Value, out _, args.User, storage, playSound: false))
            {
                _storage.Insert(storageEnt, newMagazine, out _, args.User, storage, playSound: false);
                return;
            }
        }

        if (!_slots.TryInsert(uid, magazineSlot, newMagazine, args.User, excludeUserAudio: true))
        {
            _storage.Insert(storageEnt, newMagazine, out _, args.User, storage, playSound: false);
            if (oldMagazine != null)
                _slots.TryInsert(uid, magazineSlot, oldMagazine.Value, args.User, excludeUserAudio: true);
        }
    }

    private void OnMagazineMapInit(Entity<MagazineAmmoProviderComponent> ent, ref MapInitEvent args)
    {
        MagazineSlotChanged(ent);
    }

    private void OnMagazineExamine(EntityUid uid, MagazineAmmoProviderComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var (count, _) = GetMagazineCountCapacity(uid, component);
        args.PushMarkup(Loc.GetString("gun-magazine-examine", ("color", AmmoExamineColor), ("count", count)));
    }

    private void OnMagazineUse(EntityUid uid, MagazineAmmoProviderComponent component, UseInHandEvent args)
    {
        // not checking for args.Handled or marking as such because we only relay the event to the magazine entity

        var magEnt = GetMagazineEntity(uid);

        if (magEnt == null)
            return;

        RaiseLocalEvent(magEnt.Value, args);
        UpdateAmmoCount(uid);
        UpdateMagazineAppearance(uid, component, magEnt.Value);
    }

    private void OnMagazineVerb(EntityUid uid, MagazineAmmoProviderComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        var magEnt = GetMagazineEntity(uid);

        if (magEnt != null)
        {
            RaiseLocalEvent(magEnt.Value, args);
            UpdateMagazineAppearance(magEnt.Value, component, magEnt.Value);
        }
    }

    protected virtual void OnMagazineSlotChange(EntityUid uid, MagazineAmmoProviderComponent component, ContainerModifiedMessage args)
    {
        if (MagazineSlot != args.Container.ID)
            return;

        MagazineSlotChanged((uid, component));
    }

    private void MagazineSlotChanged(Entity<MagazineAmmoProviderComponent> ent)
    {
        UpdateAmmoCount(ent);
        if (!TryComp<AppearanceComponent>(ent, out var appearance))
            return;

        var magEnt = GetMagazineEntity(ent);
        Appearance.SetData(ent, AmmoVisuals.MagLoaded, magEnt != null, appearance);

        if (magEnt != null)
        {
            UpdateMagazineAppearance(ent, ent, magEnt.Value);
        }
    }

    protected (int, int) GetMagazineCountCapacity(EntityUid uid, MagazineAmmoProviderComponent component)
    {
        var count = 0;
        var capacity = 1;
        var magEnt = GetMagazineEntity(uid);

        if (magEnt != null)
        {
            var ev = new GetAmmoCountEvent();
            RaiseLocalEvent(magEnt.Value, ref ev, false);
            count += ev.Count;
            capacity += ev.Capacity;
        }

        return (count, capacity);
    }

    protected EntityUid? GetMagazineEntity(EntityUid uid)
    {
        if (!Containers.TryGetContainer(uid, MagazineSlot, out var container) ||
            container is not ContainerSlot slot)
        {
            return null;
        }

        return slot.ContainedEntity;
    }

    private void OnMagazineAmmoCount(EntityUid uid, MagazineAmmoProviderComponent component, ref GetAmmoCountEvent args)
    {
        var magEntity = GetMagazineEntity(uid);

        if (magEntity == null)
            return;

        RaiseLocalEvent(magEntity.Value, ref args);
    }

    private void OnMagazineTakeAmmo(EntityUid uid, MagazineAmmoProviderComponent component, TakeAmmoEvent args)
    {
        var magEntity = GetMagazineEntity(uid);
        TryComp<AppearanceComponent>(uid, out var appearance);

        if (magEntity == null)
        {
            Appearance.SetData(uid, AmmoVisuals.MagLoaded, false, appearance);
            return;
        }

        // Pass the event onwards.
        RaiseLocalEvent(magEntity.Value, args);
        // Should be Dirtied by what other ammoprovider is handling it.

        var ammoEv = new GetAmmoCountEvent();
        RaiseLocalEvent(magEntity.Value, ref ammoEv);
        FinaliseMagazineTakeAmmo(uid, component, ammoEv.Count, ammoEv.Capacity, args.User, appearance);
    }

    private void FinaliseMagazineTakeAmmo(EntityUid uid, MagazineAmmoProviderComponent component, int count, int capacity, EntityUid? user, AppearanceComponent? appearance)
    {
        // If no ammo then check for autoeject
        var ejectMag = component.AutoEject && count == 0;
        if (ejectMag)
        {
            EjectMagazine(uid, component);
            Audio.PlayPredicted(component.SoundAutoEject, uid, user);
        }

        UpdateMagazineAppearance(uid, appearance, !ejectMag, count, capacity);
    }

    private void UpdateMagazineAppearance(EntityUid uid, MagazineAmmoProviderComponent component, EntityUid magEnt)
    {
        TryComp<AppearanceComponent>(uid, out var appearance);

        var count = 0;
        var capacity = 0;

        if (TryComp<AppearanceComponent>(magEnt, out var magAppearance))
        {
            Appearance.TryGetData<int>(magEnt, AmmoVisuals.AmmoCount, out var addCount, magAppearance);
            Appearance.TryGetData<int>(magEnt, AmmoVisuals.AmmoMax, out var addCapacity, magAppearance);
            count += addCount;
            capacity += addCapacity;
        }

        UpdateMagazineAppearance(uid, appearance, true, count, capacity);
    }

    private void UpdateMagazineAppearance(EntityUid uid, AppearanceComponent? appearance, bool magLoaded, int count, int capacity)
    {
        if (appearance == null)
            return;

        // Copy the magazine's appearance data
        Appearance.SetData(uid, AmmoVisuals.MagLoaded, magLoaded, appearance);
        Appearance.SetData(uid, AmmoVisuals.HasAmmo, count != 0, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoCount, count, appearance);
        Appearance.SetData(uid, AmmoVisuals.AmmoMax, capacity, appearance);
    }

    private void EjectMagazine(EntityUid uid, MagazineAmmoProviderComponent component)
    {
        var ent = GetMagazineEntity(uid);

        if (ent == null)
            return;

        _slots.TryEject(uid, MagazineSlot, null, out var a, excludeUserAudio: true);
    }

    [Serializable, NetSerializable]
    private sealed partial class BeltMagazineReloadDoAfterEvent : SimpleDoAfterEvent;
}
