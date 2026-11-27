using Robust.Shared.Serialization;
using Content.Shared.DoAfter;

namespace Content.Shared.PDA;

/// <summary>All account references and invoices are resolved by the server, within one station.</summary>
[RegisterComponent]
public sealed partial class PaymentTerminalComponent : Component
{
    [DataField] public string Department = "Service";
    [DataField] public string OwnerJob = "";
    [DataField] public int Price = 30;
    [DataField] public int Commission = 5;
    [DataField] public string CommissionDepartment = "Cargo";
    public EntityUid? Domain;
    public string OwnerUsername = "";
    public string Recipient = "";
    public bool Personal;
    public string Description = "";
    public int Version;
    public bool BillOpen;
    public bool Hacked;
    public bool PanelOpen;
}

[Serializable, NetSerializable]
public enum PaymentTerminalUiKey : byte { Key }
[Serializable, NetSerializable]
public enum PaymentTerminalAction : byte { Refresh, Claim, Configure, LoadItem, ReturnItem, Pay }

[Serializable, NetSerializable]
public sealed class PaymentTerminalMessage(PaymentTerminalAction action, int version = 0) : BoundUserInterfaceMessage
{
    public PaymentTerminalAction Action = action;
    public int Version = version;
    public bool Personal;
    public string Recipient = "";
    public string Department = "Service";
    public int Price;
    public int Commission;
    public string CommissionDepartment = "Cargo";
    public string Description = "";
}

[Serializable, NetSerializable]
public sealed class PaymentTerminalState : BoundUserInterfaceMessage
{
    public string Owner = "";
    public bool CanManage;
    public bool CanClaim;
    public bool Personal;
    public string Recipient = "";
    public string Department = "Service";
    public string[] Departments = [];
    public int Price;
    public int Commission;
    public string CommissionDepartment = "Cargo";
    public string Description = "";
    public string Item = "";
    public bool BillOpen;
    public bool Hacked;
    public int Version;
}

[Serializable, NetSerializable]
public sealed class PaymentTerminalFeedback(string text) : BoundUserInterfaceMessage
{
    public string Text = text;
}

/// <summary>Server-side payment gate, after stock/power validation and before stock mutation.</summary>
[ByRefEvent]
public record struct BeforePaidVendEvent(EntityUid? Buyer, bool Cancelled = false);

[Serializable, NetSerializable]
public sealed partial class PaymentTerminalHackEvent : SimpleDoAfterEvent;
