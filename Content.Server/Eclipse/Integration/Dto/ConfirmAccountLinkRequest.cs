using System.Text.Json.Serialization;

namespace Content.Server.Eclipse.Integration.Dto;

public sealed class ConfirmAccountLinkRequest
{
    [JsonPropertyName("linkCode")]
    public string LinkCode { get; init; } = string.Empty;

    [JsonPropertyName("ss14UserId")]
    public string Ss14UserId { get; init; } = string.Empty;

    [JsonPropertyName("ss14UserName")]
    public string Ss14UserName { get; init; } = string.Empty;

    [JsonPropertyName("serverSecret")]
    public string ServerSecret { get; init; } = string.Empty;
}
