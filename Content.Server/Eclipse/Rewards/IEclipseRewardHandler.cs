using System.Threading.Tasks;

namespace Content.Server.Eclipse.Rewards;

public interface IEclipseRewardHandler
{
    string RewardType { get; }

    Task<bool> TryGiveReward(EntityUid player, string rewardData);
}
