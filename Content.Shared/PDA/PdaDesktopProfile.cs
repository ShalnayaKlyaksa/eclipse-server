using Robust.Shared.Serialization;

namespace Content.Shared.PDA;

/// <summary>Account cosmetics use the existing Eclipse Merits / Shards wallet.</summary>
public static class PdaCosmetics
{
    public static readonly PdaCosmetic[] Catalog =
    [
        new("orbit", 0, 0),
        new("aurora", 120, 3),
        new("sunset", 160, 4),
        new("blueprint", 80, 2),
        new("studio", 250, 6),
    ];

    public static PdaCosmetic? Find(string id) => Array.Find(Catalog, item => item.Id == id);
}

public sealed record PdaCosmetic(string Id, int Merits, int Shards);

[Serializable, NetSerializable]
public sealed class PdaDesktopProfile
{
    public string Wallpaper = "orbit";
    public string PublicId = "";
    public string[] Owned = ["orbit"];
    // Stable application IDs map to row * 8 + column; -1 hides a shortcut.
    public Dictionary<string, int> Positions = new();
    public bool Card;
    public bool Decor;
    public bool Photo;
    public bool CustomWallpaper;
    public string CardText = "";
}

[Serializable, NetSerializable]
public sealed class PdaDesktopRequestMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PdaDesktopSaveMessage(PdaDesktopProfile profile) : BoundUserInterfaceMessage
{
    public PdaDesktopProfile Profile = profile;
}

[Serializable, NetSerializable]
public sealed class PdaCosmeticBuyEvent(string id, bool shards) : EntityEventArgs
{
    public string Id = id;
    public bool Shards = shards;
}

[Serializable, NetSerializable]
public sealed class PdaCosmeticsRequestEvent : EntityEventArgs;

[Serializable, NetSerializable]
public sealed class PdaRequestPublicIdEvent(int slot) : EntityEventArgs
{
    public readonly int Slot = slot;
}

[Serializable, NetSerializable]
public sealed class PdaSetPublicIdEvent(int slot, string id) : EntityEventArgs
{
    public readonly int Slot = slot;
    public readonly string Id = id;
}

[Serializable, NetSerializable]
public sealed class PdaPublicIdStateEvent(int slot, string id, bool canEdit, string? feedback) : EntityEventArgs
{
    public readonly int Slot = slot;
    public readonly string Id = id;
    public readonly bool CanEdit = canEdit;
    public readonly string? Feedback = feedback;
}

public static class PdaPublicId
{
    public static bool Valid(string? id) => id is { Length: >= 3 and <= 24 } &&
        Array.TrueForAll(id.ToCharArray(), c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.');
}

[Serializable, NetSerializable]
public sealed class PdaCosmeticsStateEvent(PdaDesktopProfile profile, int merits, int shards, string? feedback = null) : EntityEventArgs
{
    public PdaDesktopProfile Profile = profile;
    public int Merits = merits;
    public int Shards = shards;
    public string? Feedback = feedback;
}

[Serializable, NetSerializable]
public sealed class PdaDesktopProfileMessage(PdaDesktopProfile profile, int merits, int shards, string? feedback = null)
    : BoundUserInterfaceMessage
{
    public PdaDesktopProfile Profile = profile;
    public int Merits = merits;
    public int Shards = shards;
    public string? Feedback = feedback;
}
