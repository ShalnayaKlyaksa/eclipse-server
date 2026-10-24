using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public sealed partial class IndustrialFabricationDoAfterEvent : DoAfterEvent
{
    public string RecipeId;

    public IndustrialFabricationDoAfterEvent(string recipeId)
    {
        RecipeId = recipeId;
    }

    public override DoAfterEvent Clone() => this;
}
