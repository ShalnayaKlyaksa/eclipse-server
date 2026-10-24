using System.Text.Json.Serialization;

namespace Content.Server.Eclipse.Integration.Dto;

public sealed class PendingRewardDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("rewardType")]
    public string RewardType { get; init; } = string.Empty;

    [JsonPropertyName("rewardData")]
    public string RewardData { get; init; } = string.Empty;
}
