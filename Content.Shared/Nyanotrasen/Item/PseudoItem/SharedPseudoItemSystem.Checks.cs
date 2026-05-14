// SPDX-FileCopyrightText: 2024 Aidenkrz <aiden@djkraz.com>
// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Item;
using Content.Shared.Storage;

namespace Content.Shared.Nyanotrasen.Item.PseudoItem;

public partial class SharedPseudoItemSystem
{
    /// <summary>
    ///   Checks if the pseudo-item can be inserted into the specified storage entity.
    /// </summary>
    /// <remarks>
    ///   This function creates and uses a fake item component if the entity doesn't have one.
    /// </remarks>
    public bool CheckItemFits(Entity<PseudoItemComponent?> itemEnt, Entity<StorageComponent?> storageEnt)
    {
        if (!Resolve(itemEnt, ref itemEnt.Comp) || !Resolve(storageEnt, ref storageEnt.Comp))
            return false;

        var hadItem = TryComp(itemEnt.Owner, out ItemComponent? item);
        item ??= EnsureComp<ItemComponent>(itemEnt.Owner);
        _item.SetShape(itemEnt, itemEnt.Comp.Shape, item);
        _item.SetSize(itemEnt, itemEnt.Comp.Size, item);
        _item.SetStoredOffset(itemEnt, itemEnt.Comp.StoredOffset, item);

        var canInsert = _storage.CanInsert(storageEnt, itemEnt, out _, storageEnt.Comp, item, ignoreStacks: true);
        if (!hadItem)
            RemComp<ItemComponent>(itemEnt.Owner);

        return canInsert;
    }
}
