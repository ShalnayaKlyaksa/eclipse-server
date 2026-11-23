using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public sealed partial class IndustrialFabricationDoAfterEvent : DoAfterEvent
{
    public string RecipeId;
    public int Amount;

    public IndustrialFabricationDoAfterEvent(string recipeId, int amount = 1)
    {
        RecipeId = recipeId;
        Amount = amount;
    }

    public override DoAfterEvent Clone() => new IndustrialFabricationDoAfterEvent(RecipeId, Amount);
}
