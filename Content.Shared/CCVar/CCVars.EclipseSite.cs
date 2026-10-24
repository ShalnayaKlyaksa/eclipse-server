using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<string> EclipseSiteBaseUrl =
        CVarDef.Create("EclipseSite.BaseUrl", "https://eclipse-station.online", CVar.SERVERONLY);

    public static readonly CVarDef<string> EclipseSiteServerSecret =
        CVarDef.Create("EclipseSite.ServerSecret", "change_me", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    public static readonly CVarDef<bool> EclipseSiteEnableAccountLinking =
        CVarDef.Create("EclipseSite.EnableAccountLinking", true, CVar.SERVERONLY);

    public static readonly CVarDef<bool> EclipseSiteEnableRewards =
        CVarDef.Create("EclipseSite.EnableRewards", true, CVar.SERVERONLY);

    public static readonly CVarDef<bool> EclipseSiteCheckRewardsOnJoin =
        CVarDef.Create("EclipseSite.CheckRewardsOnJoin", true, CVar.SERVERONLY);
}
