using Content.Shared.Atmos;

namespace Content.Server.Body.Components;

[RegisterComponent]
public sealed partial class MixedGasLungComponent : Component
{
    [DataField(required: true)]
    public HashSet<Gas> BreathableGases = new();

    public float GetSaturation(GasMixture gas, float sampleFraction = 1f)
    {
        var moles = 0f;
        foreach (var type in BreathableGases)
            moles += gas.GetMoles(type);

        return moles * sampleFraction * Atmospherics.BreathMolesToReagentMultiplier;
    }
}
