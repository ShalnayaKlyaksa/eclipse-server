using Robust.Shared.Serialization;
using Content.Shared.CartridgeLoader;
namespace Content.Shared.Research.Components;
[Serializable, NetSerializable]
public sealed class ResearchMailRefreshMessage : CartridgeMessageEvent;
