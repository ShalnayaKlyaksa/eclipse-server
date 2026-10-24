using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server.Eclipse.Rewards;

public sealed class ItemRewardHandler : IEclipseRewardHandler
{
    private const int MaxRewardAmount = 100;

    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    public string RewardType => "Item";

    public Task<bool> TryGiveReward(EntityUid player, string rewardData)
    {
        var sawmill = _logManager.GetSawmill("eclipse.rewards");

        if (!_entityManager.EntityExists(player))
        {
            sawmill.Warning("Cannot give item reward: player entity {Player} does not exist.", player);
            return Task.FromResult(false);
        }

        var mobState = _entityManager.System<MobStateSystem>();
        if (!mobState.IsAlive(player))
        {
            sawmill.Warning("Cannot give item reward: player entity {Player} is not alive.", player);
            return Task.FromResult(false);
        }

        ItemRewardData? data;
        try
        {
            data = JsonSerializer.Deserialize<ItemRewardData>(rewardData, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException e)
        {
            sawmill.Warning("Cannot give item reward: invalid rewardData JSON: {Message}", e.Message);
            return Task.FromResult(false);
        }

        if (data == null || string.IsNullOrWhiteSpace(data.PrototypeId))
        {
            sawmill.Warning("Cannot give item reward: rewardData has no prototypeId.");
            return Task.FromResult(false);
        }

        if (!_prototype.HasIndex<EntityPrototype>(data.PrototypeId))
        {
            sawmill.Warning("Cannot give item reward: prototype {PrototypeId} was not found.", data.PrototypeId);
            return Task.FromResult(false);
        }

        if (!_entityManager.TryGetComponent<TransformComponent>(player, out var transform))
        {
            sawmill.Warning("Cannot give item reward: player entity {Player} has no transform.", player);
            return Task.FromResult(false);
        }

        var amount = Math.Clamp(data.Amount <= 0 ? 1 : data.Amount, 1, MaxRewardAmount);
        var hands = _entityManager.System<SharedHandsSystem>();

        for (var i = 0; i < amount; i++)
        {
            var item = _entityManager.SpawnEntity(data.PrototypeId, transform.Coordinates);
            hands.PickupOrDrop(player, item);
        }

        return Task.FromResult(true);
    }

    private sealed class ItemRewardData
    {
        [JsonPropertyName("prototypeId")]
        public string PrototypeId { get; init; } = string.Empty;

        [JsonPropertyName("amount")]
        public int Amount { get; init; } = 1;
    }
}
