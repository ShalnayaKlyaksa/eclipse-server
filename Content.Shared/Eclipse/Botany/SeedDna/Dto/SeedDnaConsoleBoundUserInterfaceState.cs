using Robust.Shared.Serialization;
namespace Content.Shared.Eclipse.Botany.SeedDna;
// Only the qualitative report is networked; raw genes never leave the server.
[Serializable, NetSerializable]
public sealed class SeedDnaConsoleBoundUserInterfaceState(
    bool isSeedsPresent, string seedsName, bool isDnaDiskPresent, List<string> report) : BoundUserInterfaceState
{
    public readonly bool IsSeedsPresent = isSeedsPresent;
    public readonly string SeedsName = seedsName;
    public readonly bool IsDnaDiskPresent = isDnaDiskPresent;
    public readonly List<string> Report = report;
}
