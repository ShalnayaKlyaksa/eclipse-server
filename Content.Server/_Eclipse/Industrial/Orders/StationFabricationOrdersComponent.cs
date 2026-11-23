using Content.Shared._Eclipse.Industrial.Orders;

namespace Content.Server._Eclipse.Industrial.Orders;

/// <summary>Lives on the station, not on any replaceable terminal.</summary>
[RegisterComponent]
public sealed partial class StationFabricationOrdersComponent : Component
{
    [DataField] public int NextId = 1;
    [DataField] public List<FabricationOrderRecord> Orders = new();
    [DataField] public Dictionary<FabricationDepartment, TimeSpan> NextRequest = new();
}

[DataDefinition]
public sealed partial class FabricationOrderRecord
{
    [DataField] public int Id;
    [DataField] public int Revision;
    [DataField] public FabricationDepartment Department;
    [DataField] public string Product = "";
    [DataField] public int Quantity;
    [DataField] public string Requester = "";
    [DataField] public string Reason = "";
    [DataField] public bool Urgent;
    [DataField] public int Priority = 1;
    [DataField] public FabricationOrderStatus Status;
    [DataField] public string Assignee = "";
    [DataField] public string Response = "";
    [DataField] public TimeSpan Created;
}
