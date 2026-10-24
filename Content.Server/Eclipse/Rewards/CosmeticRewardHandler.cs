using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Robust.Shared.Player;

namespace Content.Server.Eclipse.Rewards;

public sealed class CosmeticRewardHandler : IEclipseRewardHandler
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    public string RewardType => "Cosmetic";

    public Task<bool> TryGiveReward(EntityUid player, string rewardData)
    {
        var sawmill = _logManager.GetSawmill("eclipse.rewards");
        sawmill.Info("Cosmetic reward accepted for {Player}; cosmetic backend is not implemented yet.", player);

        if (_players.TryGetSessionByEntity(player, out var session))
            _chat.DispatchServerMessage(session, "Косметическая награда получена.", suppressLog: true);

        return Task.FromResult(true);
    }
}
