using System.Threading.Tasks;
using Content.Server.Chat.Managers;
using Content.Server.Eclipse.Integration;
using Content.Server.Eclipse.Integration.Dto;
using Content.Shared.CCVar;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server.Eclipse.Rewards;

public sealed class EclipseRewardManager : EntitySystem
{
    private readonly Dictionary<string, IEclipseRewardHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);

    [Dependency] private readonly EclipseSiteClient _siteClient = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("eclipse.rewards");

        RegisterHandler(new CreditsRewardHandler());
        RegisterHandler(new CosmeticRewardHandler());
        RegisterHandler(new RoleUnlockRewardHandler());
        RegisterHandler(new ItemRewardHandler());

        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;
    }

    public async Task<int> ClaimPendingRewardsAsync(ICommonSession session)
    {
        if (!_cfg.GetCVar(CCVars.EclipseSiteEnableRewards))
        {
            _chat.DispatchServerMessage(session, "Получение наград временно отключено.", suppressLog: true);
            return 0;
        }

        if (!IsAuthorized(session))
        {
            _chat.DispatchServerMessage(session, "Для получения наград нужен авторизованный SS14 аккаунт.", suppressLog: true);
            return 0;
        }

        if (session.AttachedEntity is not { Valid: true } player)
        {
            _chat.DispatchServerMessage(session, "Награды можно получить только после входа в игру.", suppressLog: true);
            return 0;
        }

        var pending = await _siteClient.GetPendingRewardsAsync(session.UserId.ToString());
        if (!pending.Success)
        {
            _chat.DispatchServerMessage(session, pending.Message ?? "Сайт Eclipse Station временно недоступен. Попробуйте позже.", suppressLog: true);
            return 0;
        }

        if (pending.Rewards.Count == 0)
        {
            _chat.DispatchServerMessage(session, "У вас нет ожидающих наград.", suppressLog: true);
            return 0;
        }

        var claimed = 0;
        var failed = 0;

        foreach (var reward in pending.Rewards)
        {
            if (!await TryGiveReward(player, reward))
            {
                failed++;
                continue;
            }

            var claim = await _siteClient.ClaimRewardAsync(reward.Id, session.UserId.ToString());
            if (!claim.Success)
            {
                failed++;
                _sawmill.Warning("Reward {RewardId} was given to {UserId}, but site claim failed: {Message}",
                    reward.Id,
                    session.UserId,
                    claim.Message ?? "no message");
                continue;
            }

            claimed++;
        }

        if (claimed > 0)
            _chat.DispatchServerMessage(session, $"Получено наград: {claimed}.", suppressLog: true);

        if (failed > 0)
            _chat.DispatchServerMessage(session, "Некоторые награды не удалось выдать. Они останутся в ожидании.", suppressLog: true);

        return claimed;
    }

    public async Task<int> CountPendingRewardsAsync(ICommonSession session)
    {
        if (!_cfg.GetCVar(CCVars.EclipseSiteEnableRewards) || !IsAuthorized(session))
            return 0;

        var pending = await _siteClient.GetPendingRewardsAsync(session.UserId.ToString());
        if (!pending.Success)
            return 0;

        return pending.Rewards.Count;
    }

    private async Task<bool> TryGiveReward(EntityUid player, PendingRewardDto reward)
    {
        if (string.IsNullOrWhiteSpace(reward.RewardType))
        {
            _sawmill.Warning("Eclipse reward {RewardId} has no reward type.", reward.Id);
            return false;
        }

        if (!_handlers.TryGetValue(reward.RewardType, out var handler))
        {
            _sawmill.Warning("No handler registered for Eclipse reward type {RewardType} (reward {RewardId}).",
                reward.RewardType,
                reward.Id);
            return false;
        }

        try
        {
            return await handler.TryGiveReward(player, reward.RewardData ?? string.Empty);
        }
        catch (Exception e)
        {
            _sawmill.Error("Failed to give Eclipse reward {RewardId} of type {RewardType}: {Exception}",
                reward.Id,
                reward.RewardType,
                e);
            return false;
        }
    }

    private void RegisterHandler(IEclipseRewardHandler handler)
    {
        IoCManager.InjectDependencies(handler);
        _handlers[handler.RewardType] = handler;
    }

    private async void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus != SessionStatus.InGame ||
            !_cfg.GetCVar(CCVars.EclipseSiteCheckRewardsOnJoin) ||
            !_cfg.GetCVar(CCVars.EclipseSiteEnableRewards))
        {
            return;
        }

        var session = args.Session;
        await Task.Delay(TimeSpan.FromSeconds(5));

        if (session.Status != SessionStatus.InGame)
            return;

        var pending = await CountPendingRewardsAsync(session);
        if (pending > 0 && session.Status == SessionStatus.InGame)
            _chat.DispatchServerMessage(session, "У вас есть ожидающие награды. Введите claimrewards, чтобы получить их.", suppressLog: true);
    }

    private static bool IsAuthorized(ICommonSession session)
    {
        return session.AuthType == LoginType.LoggedIn;
    }
}
