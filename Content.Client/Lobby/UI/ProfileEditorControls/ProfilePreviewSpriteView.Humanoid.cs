using System.Linq;
using Content.Client.Humanoid;
using Content.Client.Station;
using Content.Shared.Body;
using Content.Shared.Clothing;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI.ProfileEditorControls;

public sealed partial class ProfilePreviewSpriteView
{
    /// <summary>
    /// A slim reload that only updates the entity itself and not any of the job entities, etc.
    /// </summary>
    private void ReloadHumanoidEntity(HumanoidCharacterProfile humanoid)
    {
        if (!EntMan.EntityExists(PreviewDummy) ||
            !EntMan.HasComponent<VisualBodyComponent>(PreviewDummy))
            return;

        EntMan.System<SharedVisualBodySystem>().ApplyProfileTo(PreviewDummy, humanoid);
        EntMan.System<HumanoidProfileSystem>().ApplyProfileTo(PreviewDummy, humanoid);
    }

    /// <summary>
    /// Loads the profile onto a dummy entity.
    /// </summary>
    private void LoadHumanoidEntity(HumanoidCharacterProfile? humanoid, JobPrototype? job, bool jobClothes, bool underwear = true)
    {
        EntProtoId? previewEntity = null;
        if (humanoid != null && jobClothes)
        {
            job ??= GetPreferredJob(humanoid);

            previewEntity = job.JobPreviewEntity ?? (EntProtoId?)job?.JobEntity;
        }

        if (previewEntity != null)
        {
            // Special type like borg or AI, do not spawn a human just spawn the entity.
            PreviewDummy = EntMan.SpawnEntity(previewEntity, MapCoordinates.Nullspace);
        }
        else if (humanoid is not null)
        {
            var dummy = _prototypeManager.Index(humanoid.Species).DollPrototype;
            PreviewDummy = EntMan.SpawnEntity(dummy, MapCoordinates.Nullspace);
            ReloadHumanoidEntity(humanoid);
        }
        else
        {
            PreviewDummy = EntMan.SpawnEntity(_prototypeManager.Index(HumanoidCharacterProfile.DefaultSpecies).DollPrototype, MapCoordinates.Nullspace);
        }

        if (humanoid == null || (!jobClothes && !underwear))
            return;

        // Eclipse - underwear can be shown on its own, so the job still has to be resolved even without clothes.
        job ??= GetPreferredJob(humanoid);

        if (jobClothes)
            GiveDummyJobClothes(humanoid, job, underwear);

        if (_prototypeManager.HasIndex<RoleLoadoutPrototype>(LoadoutSystem.GetJobPrototype(job.ID)))
        {
            var loadout = humanoid.GetLoadoutOrDefault(LoadoutSystem.GetJobPrototype(job.ID), _playerManager.LocalSession, humanoid.Species, EntMan, _prototypeManager);
            GiveDummyLoadout(loadout, jobClothes, underwear);
        }
    }

    /// <summary>
    /// Gets the highest priority job for the profile.
    /// </summary>
    private JobPrototype GetPreferredJob(HumanoidCharacterProfile profile)
    {
        var highPriorityJob = profile.JobPriorities.FirstOrDefault(p => p.Value == JobPriority.High).Key;
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract (what is resharper smoking?)
        return _prototypeManager.Index<JobPrototype>(highPriorityJob.Id ?? SharedGameTicker.FallbackOverflowJob);
    }

    private void GiveDummyLoadout(RoleLoadout? roleLoadout, bool clothes = true, bool underwear = true)
    {
        if (roleLoadout == null)
            return;

        var spawnSys = EntMan.System<StationSpawningSystem>();
        EntMan.System<InventorySystem>().TryGetSlots(PreviewDummy, out var slots);

        foreach (var group in roleLoadout.SelectedLoadouts.Values)
        {
            foreach (var loadout in group)
            {
                if (!_prototypeManager.Resolve(loadout.Prototype, out var loadoutProto))
                    continue;

                // Eclipse - the editor's two toggles decide which half of the loadout the dummy wears.
                var isUnderwear = slots != null && IsUnderwearLoadout(loadoutProto, slots);
                if (isUnderwear ? !underwear : !clothes)
                    continue;

                spawnSys.EquipStartingGear(PreviewDummy, loadoutProto);
            }
        }
    }

    /// <summary>
    /// Eclipse - whether a loadout only fills underwear slots, and so belongs to the underwear toggle.
    /// </summary>
    private bool IsUnderwearLoadout(LoadoutPrototype loadout, SlotDefinition[] slots)
    {
        var gear = _prototypeManager.Resolve(loadout.StartingGear, out var startingGear)
            ? (IEquipmentLoadout) startingGear
            : loadout;

        var fillsAnySlot = false;
        foreach (var slot in slots)
        {
            if (gear.GetGear(slot.Name) == string.Empty)
                continue;

            if ((slot.SlotFlags & SlotFlags.UNDERWEAR) == SlotFlags.NONE)
                return false;

            fillsAnySlot = true;
        }

        return fillsAnySlot;
    }

    /// <summary>
    /// Applies the specified job's clothes to the dummy.
    /// </summary>
    private void GiveDummyJobClothes(HumanoidCharacterProfile profile, JobPrototype job, bool underwear = true)
    {
        var inventorySys = EntMan.System<InventorySystem>();
        if (!inventorySys.TryGetSlots(PreviewDummy, out var slots))
            return;

        // Eclipse - with underwear hidden, leave those slots alone entirely.
        if (!underwear)
            slots = slots.Where(slot => (slot.SlotFlags & SlotFlags.UNDERWEAR) == SlotFlags.NONE).ToArray();

        // Apply loadout
        if (profile.Loadouts.TryGetValue(job.ID, out var jobLoadout))
        {
            foreach (var loadouts in jobLoadout.SelectedLoadouts.Values)
            {
                foreach (var loadout in loadouts)
                {
                    if (!_prototypeManager.Resolve(loadout.Prototype, out var loadoutProto))
                        continue;

                    // TODO: Need some way to apply starting gear to an entity and replace existing stuff coz holy fucking shit dude.
                    foreach (var slot in slots)
                    {
                        // Try startinggear first
                        if (_prototypeManager.Resolve(loadoutProto.StartingGear, out var loadoutGear))
                        {
                            var itemType = ((IEquipmentLoadout) loadoutGear).GetGear(slot.Name);

                            if (inventorySys.TryUnequip(PreviewDummy, slot.Name, out var unequippedItem, silent: true, force: true, reparent: false))
                            {
                                EntMan.DeleteEntity(unequippedItem.Value);
                            }

                            if (itemType != string.Empty)
                            {
                                var item = EntMan.SpawnEntity(itemType, MapCoordinates.Nullspace);
                                inventorySys.TryEquip(PreviewDummy, item, slot.Name, true, true);
                            }
                        }
                        else
                        {
                            var itemType = ((IEquipmentLoadout) loadoutProto).GetGear(slot.Name);

                            if (inventorySys.TryUnequip(PreviewDummy, slot.Name, out var unequippedItem, silent: true, force: true, reparent: false))
                            {
                                EntMan.DeleteEntity(unequippedItem.Value);
                            }

                            if (itemType != string.Empty)
                            {
                                var item = EntMan.SpawnEntity(itemType, MapCoordinates.Nullspace);
                                inventorySys.TryEquip(PreviewDummy, item, slot.Name, true, true);
                            }
                        }
                    }
                }
            }
        }

        if (!_prototypeManager.Resolve(job.StartingGear, out var gear))
            return;

        foreach (var slot in slots)
        {
            var itemType = ((IEquipmentLoadout) gear).GetGear(slot.Name);

            if (inventorySys.TryUnequip(PreviewDummy, slot.Name, out var unequippedItem, silent: true, force: true, reparent: false))
            {
                EntMan.DeleteEntity(unequippedItem.Value);
            }

            if (itemType != string.Empty)
            {
                var item = EntMan.SpawnEntity(itemType, MapCoordinates.Nullspace);
                inventorySys.TryEquip(PreviewDummy, item, slot.Name, true, true);
            }
        }
    }
}
