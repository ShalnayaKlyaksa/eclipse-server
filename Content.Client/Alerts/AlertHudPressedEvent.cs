using Content.Shared.Alert;
using Robust.Shared.Prototypes;

namespace Content.Client.Alerts;

/// <summary>
/// Client-local broadcast raised whenever an alert is pressed in the HUD, before any type-specific
/// handling. Lets other client systems (e.g. the tutorial) react to alert clicks even when the click is
/// consumed locally and never sent to the server.
/// </summary>
public sealed class AlertHudPressedEvent : EntityEventArgs
{
    public readonly ProtoId<AlertPrototype> Type;

    public AlertHudPressedEvent(ProtoId<AlertPrototype> type)
    {
        Type = type;
    }
}
