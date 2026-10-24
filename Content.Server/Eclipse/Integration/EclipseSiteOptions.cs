namespace Content.Server.Eclipse.Integration;

public sealed class EclipseSiteOptions
{
    public string BaseUrl { get; init; } = "https://eclipse-station.online";
    public string ServerSecret { get; init; } = "change_me";
    public bool EnableAccountLinking { get; init; } = true;
    public bool EnableRewards { get; init; } = true;
    public bool CheckRewardsOnJoin { get; init; } = true;
}
