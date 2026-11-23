using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Eclipse.Industrial.Orders;

[Serializable, NetSerializable]
public enum FabricationOrderUiKey : byte { Key }
[Serializable, NetSerializable]
public enum FabricationDepartment : byte { Engineering, Medical, Science, Security, Cargo, Service, Command }
[Serializable, NetSerializable]
public enum FabricationOrderStatus : byte { New, InProgress, WaitingMaterials, WaitingResearch, Ready, Completed, Cancelled, Rejected }
[Serializable, NetSerializable]
public enum FabricationOrderAction : byte { Accept, WaitMaterials, WaitResearch, Ready, Complete, Cancel, Reject, Update }

[RegisterComponent]
public sealed partial class FabricationOrderConsoleComponent : Component
{
    [DataField] public bool Dispatcher;
    [DataField] public FabricationDepartment Department;
    [DataField] public string LastNotice = "";
}

/// <summary>Curated finished products, with licensing taken from actual factory/workbench recipes.</summary>
[Prototype]
public sealed partial class FabricationOrderProductPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public EntProtoId Product;
    [DataField(required: true)] public string Category = "";
    [DataField] public int MaxQuantity = 50;
}

[Serializable, NetSerializable]
public sealed class FabricationOrderProductInfo(string id, string product, string name, string category, int maximum, bool researched, string? technology)
{
    public string Id = id;
    public string Product = product;
    public string Name = name;
    public string Category = category;
    public int Maximum = maximum;
    public bool Researched = researched;
    public string? Technology = technology;
}

[Serializable, NetSerializable]
public sealed class FabricationOrderInfo(int id, int revision, FabricationDepartment department, string product, string name,
    int quantity, string requester, string reason, bool urgent, int priority, FabricationOrderStatus status,
    string assignee, string response, int ageSeconds, bool researched, string? technology)
{
    public int Id = id;
    public int Revision = revision;
    public FabricationDepartment Department = department;
    public string Product = product;
    public string Name = name;
    public int Quantity = quantity;
    public string Requester = requester;
    public string Reason = reason;
    public bool Urgent = urgent;
    public int Priority = priority;
    public FabricationOrderStatus Status = status;
    public string Assignee = assignee;
    public string Response = response;
    public int AgeSeconds = ageSeconds;
    public bool Researched = researched;
    public string? Technology = technology;
    public static bool Closed(FabricationOrderStatus status) => status is FabricationOrderStatus.Completed or FabricationOrderStatus.Cancelled or FabricationOrderStatus.Rejected;
}

[Serializable, NetSerializable]
public sealed class FabricationOrderState(bool dispatcher, FabricationDepartment department, bool connected,
    FabricationOrderProductInfo[] products, FabricationOrderInfo[] orders) : BoundUserInterfaceState
{
    public bool Dispatcher = dispatcher;
    public FabricationDepartment Department = department;
    public bool Connected = connected;
    public FabricationOrderProductInfo[] Products = products;
    public FabricationOrderInfo[] Orders = orders;
    public string Feedback = "";
}

[Serializable, NetSerializable]
public sealed class FabricationOrderSubmitMessage(string product, int quantity, string reason, bool urgent) : BoundUserInterfaceMessage
{
    public string Product = product;
    public int Quantity = quantity;
    public string Reason = reason;
    public bool Urgent = urgent;
}

[Serializable, NetSerializable]
public sealed class FabricationOrderActionMessage(int id, int revision, FabricationOrderAction action, int priority, string response) : BoundUserInterfaceMessage
{
    public int Id = id;
    public int Revision = revision;
    public FabricationOrderAction Action = action;
    public int Priority = priority;
    public string Response = response;
}
