using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Robust.Shared.Player;

namespace Content.Server.Eclipse.Rewards;

public sealed class RoleUnlockRewardHandler : IEclipseRewardHandler
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    public string RewardType => "RoleUnlock";

    public Task<bool> TryGiveReward(EntityUid player, string rewardData)
    {
        var sawmill = _logManager.GetSawmill("eclipse.rewards");
        sawmill.Info("RoleUnlock reward accepted for {Player}; role unlock backend is not implemented yet.", player);

        if (_players.TryGetSessionByEntity(player, out var session))
            _chat.DispatchServerMessage(session, "Разблокировка роли получена. Система ролей не изменена.", suppressLog: true);

        return Task.FromResult(true);
    }
}
