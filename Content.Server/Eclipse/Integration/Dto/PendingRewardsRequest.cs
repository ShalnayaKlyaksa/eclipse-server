using System.Text.Json.Serialization;

namespace Content.Server.Eclipse.Integration.Dto;

public sealed class PendingRewardsRequest
{
    [JsonPropertyName("ss14UserId")]
    public string Ss14UserId { get; init; } = string.Empty;

    [JsonPropertyName("serverSecret")]
    public string ServerSecret { get; init; } = string.Empty;
}
