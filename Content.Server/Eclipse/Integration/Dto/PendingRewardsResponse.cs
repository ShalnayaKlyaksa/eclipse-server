using System.Text.Json.Serialization;

namespace Content.Server.Eclipse.Integration.Dto;

public sealed class PendingRewardsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("rewards")]
    public List<PendingRewardDto> Rewards { get; init; } = new();

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
