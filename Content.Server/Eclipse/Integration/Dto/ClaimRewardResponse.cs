using System.Text.Json.Serialization;

namespace Content.Server.Eclipse.Integration.Dto;

public sealed class ClaimRewardResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
