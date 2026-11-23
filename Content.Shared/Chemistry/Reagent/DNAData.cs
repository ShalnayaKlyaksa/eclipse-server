using Robust.Shared.Serialization;

namespace Content.Shared.Chemistry.Reagent;

[ImplicitDataDefinitionForInheritors, Serializable, NetSerializable]
public sealed partial class DnaData : ReagentData
{
    [DataField]
    public string DNA = string.Empty;

    public override ReagentData Clone()
    {
        return new DnaData
        {
            DNA = DNA,
        };
    }

    public override bool Equals(ReagentData? other)
    {
        if (other == null)
        {
            return false;
        }

        return other is DnaData dna && dna.DNA == DNA;
    }

    public override int GetHashCode()
    {
        return DNA.GetHashCode();
    }
}
