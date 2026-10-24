namespace Content.Shared.CartridgeLoader;

/// <summary>
/// Marks a physical PDA cartridge that is consumed after its program is installed.
/// The installed program entity is not consumed.
/// </summary>
[RegisterComponent]
public sealed partial class OneTimeCartridgeComponent : Component;
