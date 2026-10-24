using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._EinsteinEngines.DirectedEmotes;

public enum DirectedEmotesMessageType : byte
{
    Whisper = 0,
    Voice = 1,
    Emotion = 2,
}

public sealed class DirectedEmotesStartMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public NetEntity Target;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Target = buffer.ReadNetEntity();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Target);
    }
}

public sealed class DirectedEmotesSendMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public int ConversationId;
    public string Message = string.Empty;
    public DirectedEmotesMessageType MessageType = DirectedEmotesMessageType.Voice;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        ConversationId = buffer.ReadVariableInt32();
        Message = buffer.ReadString();
        MessageType = (DirectedEmotesMessageType) buffer.ReadByte();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(ConversationId);
        buffer.Write(Message);
        buffer.Write((byte) MessageType);
    }
}

public sealed class DirectedEmotesAddParticipantMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public int ConversationId;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        ConversationId = buffer.ReadVariableInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(ConversationId);
    }
}

public sealed class DirectedEmotesLeaveMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public int ConversationId;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        ConversationId = buffer.ReadVariableInt32();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(ConversationId);
    }
}

public sealed class DirectedEmotesStateMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public int ConversationId;
    public bool CanAddParticipant;
    public DirectedEmotesParticipantState[] Participants = [];

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        ConversationId = buffer.ReadVariableInt32();
        CanAddParticipant = buffer.ReadBoolean();
        buffer.ReadPadBits();

        Participants = new DirectedEmotesParticipantState[buffer.ReadByte()];
        for (var i = 0; i < Participants.Length; i++)
        {
            Participants[i] = new DirectedEmotesParticipantState(
                buffer.ReadString(),
                buffer.ReadBoolean());
            buffer.ReadPadBits();
        }
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(ConversationId);
        buffer.Write(CanAddParticipant);
        buffer.WritePadBits();

        buffer.Write((byte) Participants.Length);
        foreach (var participant in Participants)
        {
            buffer.Write(participant.Name);
            buffer.Write(participant.InRange);
            buffer.WritePadBits();
        }
    }
}

public sealed class DirectedEmotesChatMessage : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;
    public int ConversationId;
    public string Sender = string.Empty;
    public string Message = string.Empty;
    public bool SystemMessage;
    public DirectedEmotesMessageType MessageType = DirectedEmotesMessageType.Voice;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        ConversationId = buffer.ReadVariableInt32();
        Sender = buffer.ReadString();
        Message = buffer.ReadString();
        SystemMessage = buffer.ReadBoolean();
        MessageType = (DirectedEmotesMessageType) buffer.ReadByte();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.WriteVariableInt32(ConversationId);
        buffer.Write(Sender);
        buffer.Write(Message);
        buffer.Write(SystemMessage);
        buffer.Write((byte) MessageType);
    }
}

public readonly record struct DirectedEmotesParticipantState(string Name, bool InRange);
