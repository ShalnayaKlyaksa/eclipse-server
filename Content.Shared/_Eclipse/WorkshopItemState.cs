using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse;

/// <summary>One physical item shown in a workstation's input or output container.</summary>
[Serializable, NetSerializable]
public sealed class WorkshopItemState(int index, string? prototype, string name, int count = 1)
{
    public int Index = index;
    public string? Prototype = prototype;
    public string Name = name;
    public int Count = count;
    public string? Role;
}
