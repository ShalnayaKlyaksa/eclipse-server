using Robust.Shared.Serialization;

namespace Content.Shared.PDA;

[RegisterComponent]
public sealed partial class PdaAtmComponent : Component
{
    public TimeSpan NextOperation;
}

[Serializable, NetSerializable]
public enum PdaAtmUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class PdaAtmRequestMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class PdaAtmOperationMessage(bool withdraw, int amount, Guid requestId) : BoundUserInterfaceMessage
{
    public bool Withdraw = withdraw;
    public int Amount = amount;
    public Guid RequestId = requestId;
}

[Serializable, NetSerializable]
public sealed class PdaAtmStateMessage(string id, string name, int balance, string? feedback = null) : BoundUserInterfaceMessage
{
    public string Id = id;
    public string Name = name;
    public int Balance = balance;
    public string? Feedback = feedback;
}
