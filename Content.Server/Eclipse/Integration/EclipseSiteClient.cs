using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Eclipse.Integration.Dto;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Network;

namespace Content.Server.Eclipse.Integration;

public sealed class EclipseSiteClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IHttpClientHolder _httpClientHolder = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill? _sawmill;

    private ISawmill Sawmill => _sawmill ??= _logManager.GetSawmill("eclipse.site");

    public Task<ConfirmAccountLinkResponse> ConfirmAccountLinkAsync(
        string code,
        string ss14UserId,
        string ss14UserName)
    {
        var options = GetOptions();
        var request = new ConfirmAccountLinkRequest
        {
            LinkCode = code,
            Ss14UserId = ss14UserId,
            Ss14UserName = ss14UserName,
            ServerSecret = options.ServerSecret
        };

        return PostAsync<ConfirmAccountLinkRequest, ConfirmAccountLinkResponse>(
            options,
            "/api/ss14/link/confirm",
            request,
            new ConfirmAccountLinkResponse
            {
                Success = false,
                Message = "Сайт Eclipse Station временно недоступен. Попробуйте позже."
            });
    }

    public Task<PendingRewardsResponse> GetPendingRewardsAsync(string ss14UserId)
    {
        var options = GetOptions();
        var request = new PendingRewardsRequest
        {
            Ss14UserId = ss14UserId,
            ServerSecret = options.ServerSecret
        };

        return PostAsync<PendingRewardsRequest, PendingRewardsResponse>(
            options,
            "/api/ss14/rewards/pending",
            request,
            new PendingRewardsResponse
            {
                Success = false,
                Message = "Сайт Eclipse Station временно недоступен. Попробуйте позже."
            });
    }

    public Task<ClaimRewardResponse> ClaimRewardAsync(long rewardId, string ss14UserId)
    {
        var options = GetOptions();
        var request = new ClaimRewardRequest
        {
            RewardId = rewardId,
            Ss14UserId = ss14UserId,
            ServerSecret = options.ServerSecret
        };

        return PostAsync<ClaimRewardRequest, ClaimRewardResponse>(
            options,
            "/api/ss14/rewards/claim",
            request,
            new ClaimRewardResponse
            {
                Success = false,
                Message = "Сайт Eclipse Station временно недоступен. Попробуйте позже."
            });
    }

    public EclipseSiteOptions GetOptions()
    {
        return new EclipseSiteOptions
        {
            BaseUrl = _cfg.GetCVar(CCVars.EclipseSiteBaseUrl),
            ServerSecret = _cfg.GetCVar(CCVars.EclipseSiteServerSecret),
            EnableAccountLinking = _cfg.GetCVar(CCVars.EclipseSiteEnableAccountLinking),
            EnableRewards = _cfg.GetCVar(CCVars.EclipseSiteEnableRewards),
            CheckRewardsOnJoin = _cfg.GetCVar(CCVars.EclipseSiteCheckRewardsOnJoin)
        };
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        EclipseSiteOptions options,
        string path,
        TRequest request,
        TResponse failureResponse)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            Sawmill.Warning("Eclipse site request skipped: BaseUrl is empty.");
            return failureResponse;
        }

        using var cts = new CancellationTokenSource(RequestTimeout);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri(options.BaseUrl, path))
        {
            Content = new StringContent(JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json")
        };

        try
        {
            using var response = await _httpClientHolder.Client.SendAsync(httpRequest, cts.Token);
            var content = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                Sawmill.Warning(
                    "Eclipse site request to {Path} failed with HTTP {StatusCode}: {ReasonPhrase}",
                    path,
                    (int) response.StatusCode,
                    response.ReasonPhrase ?? string.Empty);

                return TryDeserialize(content, failureResponse) ?? failureResponse;
            }

            var parsed = TryDeserialize(content, failureResponse);
            if (parsed == null)
            {
                Sawmill.Warning("Eclipse site request to {Path} returned invalid JSON.", path);
                return failureResponse;
            }

            return parsed;
        }
        catch (OperationCanceledException)
        {
            Sawmill.Warning("Eclipse site request to {Path} timed out after {Timeout} seconds.", path, RequestTimeout.TotalSeconds);
            return failureResponse;
        }
        catch (HttpRequestException e)
        {
            Sawmill.Warning("Eclipse site request to {Path} failed: {Message}", path, e.Message);
            return failureResponse;
        }
        catch (Exception e)
        {
            Sawmill.Error("Unexpected Eclipse site request error at {Path}: {Exception}", path, e);
            return failureResponse;
        }
    }

    private static TResponse? TryDeserialize<TResponse>(string content, TResponse failureResponse)
    {
        if (string.IsNullOrWhiteSpace(content))
            return failureResponse;

        try
        {
            return JsonSerializer.Deserialize<TResponse>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        return new Uri($"{trimmedBase}{path}");
    }
}
