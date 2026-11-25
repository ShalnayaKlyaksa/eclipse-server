using Robust.Shared.GameStates;

namespace Content.Shared.Traits.Assorted;

[RegisterComponent]
public sealed partial class ClearDictionComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class LightStepComponent : Component;

[RegisterComponent, NetworkedComponent]
public sealed partial class TenacityComponent : Component;
