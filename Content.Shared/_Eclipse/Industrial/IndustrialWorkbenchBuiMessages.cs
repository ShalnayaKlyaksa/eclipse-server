using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial;

[Serializable, NetSerializable]
public sealed class IndustrialWorkbenchCraftBuiMessage : BoundUserInterfaceMessage
{
    public string RecipeId;
    public int Amount;

    public IndustrialWorkbenchCraftBuiMessage(string recipeId, int amount)
    {
        RecipeId = recipeId;
        Amount = amount;
    }
}
