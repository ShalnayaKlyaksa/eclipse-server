using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Robust.Shared.Player;

namespace Content.Server.Eclipse.Rewards;

public sealed class CreditsRewardHandler : IEclipseRewardHandler
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly ISharedPlayerManager _players = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    public string RewardType => "Credits";

    public Task<bool> TryGiveReward(EntityUid player, string rewardData)
    {
        var sawmill = _logManager.GetSawmill("eclipse.rewards");
        sawmill.Info("Credits reward accepted for {Player}; currency backend is not implemented yet.", player);

        if (_players.TryGetSessionByEntity(player, out var session))
            _chat.DispatchServerMessage(session, "Кредиты получены. Игровая валюта будет подключена позже.", suppressLog: true);

        return Task.FromResult(true);
    }
}
