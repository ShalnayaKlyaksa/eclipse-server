namespace Content.Server._Eclipse.Industrial;

/// <summary>Shared by a station, so rebuilding a research server does not reset rewards for identical samples.</summary>
[RegisterComponent]
public sealed partial class IndustrialSampleLedgerComponent : Component
{
    [DataField] public Dictionary<string, int> Studied = new();
}
