using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<int> PdaFlappyBest = CVarDef.Create("pda.flappy_best", 0, CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<int> PdaBlocksBest = CVarDef.Create("pda.blocks_best", 0, CVar.CLIENTONLY | CVar.ARCHIVE);
    public static readonly CVarDef<bool> PdaDisplayEffects = CVarDef.Create("pda.display_effects", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
