using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    /// Enables the lobby tutorial lessons and Avi's automatic welcome.
    /// </summary>
    public static readonly CVarDef<bool> TutorialEnabled =
        CVarDef.Create("tutorial.enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);
}
