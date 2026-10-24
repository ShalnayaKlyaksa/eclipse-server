using Content.Shared.Objectives;
using Robust.Shared.Serialization;

namespace Content.Shared.CharacterInfo;

[Serializable, NetSerializable]
public readonly record struct PersonalTaskInfo(
    string Title,
    string Description,
    string Reward,
    string Icon,
    bool Highlighted,
    bool Completed,
    bool Rewarded,
    PersonalTaskCondition Condition,
    string? TargetPrototype,
    int ExperienceReward,
    int CreditsReward);

[Serializable, NetSerializable]
public enum PersonalTaskCondition : byte
{
    SurviveRound,
    HaveItem,
}

[Serializable, NetSerializable]
public sealed class RequestCharacterInfoEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;

    public RequestCharacterInfoEvent(NetEntity netEntity)
    {
        NetEntity = netEntity;
    }
}

[Serializable, NetSerializable]
public sealed class CharacterInfoEvent : EntityEventArgs
{
    public readonly NetEntity NetEntity;
    public readonly string JobTitle;
    public readonly Dictionary<string, List<ObjectiveInfo>> Objectives;
    public readonly string? Briefing;
    public readonly List<PersonalTaskInfo> PersonalTasks;

    public CharacterInfoEvent(
        NetEntity netEntity,
        string jobTitle,
        Dictionary<string, List<ObjectiveInfo>> objectives,
        string? briefing,
        List<PersonalTaskInfo> personalTasks)
    {
        NetEntity = netEntity;
        JobTitle = jobTitle;
        Objectives = objectives;
        Briefing = briefing;
        PersonalTasks = personalTasks;
    }
}
