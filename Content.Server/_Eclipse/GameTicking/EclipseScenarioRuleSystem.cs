using Content.Server._Eclipse.GameTicking.Components;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Server._Eclipse.GameTicking;

public sealed class EclipseScenarioRuleSystem : GameRuleSystem<EclipseScenarioRuleComponent>
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    protected override void Started(EntityUid uid, EclipseScenarioRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        var difficulty = PickDifficulty(component);
        component.SelectedDifficulty = difficulty;

        var rules = GetRules(component, difficulty);
        foreach (var rule in rules)
        {
            if (GameTicker.StartGameRule(rule, out var ruleUid))
                component.SpawnedRules.Add(ruleUid);
        }

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(GetAnnouncement(component, difficulty)),
            sender: component.AnnouncementSender,
            playSound: false,
            colorOverride: GetColor(component, difficulty));
    }

    protected override void Ended(EntityUid uid, EclipseScenarioRuleComponent component, GameRuleComponent gameRule, GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        foreach (var rule in component.SpawnedRules)
        {
            if (Deleted(rule))
                continue;

            GameTicker.EndGameRule(rule);
        }

        component.SpawnedRules.Clear();
    }

    protected override void AppendRoundEndText(EntityUid uid, EclipseScenarioRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        if (component.SelectedDifficulty == 0)
            return;

        args.AddLine(Loc.GetString("eclipse-extended-round-end-difficulty",
            ("difficulty", component.SelectedDifficulty)));
    }

    private int PickDifficulty(EclipseScenarioRuleComponent component)
    {
        var total = Math.Max(1, component.DifficultyOneWeight + component.DifficultyTwoWeight + component.DifficultyThreeWeight);
        var roll = _random.Next(1, total + 1);

        if (roll <= component.DifficultyOneWeight)
            return 1;

        if (roll <= component.DifficultyOneWeight + component.DifficultyTwoWeight)
            return 2;

        return 3;
    }

    private IReadOnlyList<EntProtoId> GetRules(EclipseScenarioRuleComponent component, int difficulty)
    {
        return difficulty switch
        {
            1 => component.DifficultyOneRules,
            2 => component.DifficultyTwoRules,
            _ => component.DifficultyThreeRules,
        };
    }

    private string GetAnnouncement(EclipseScenarioRuleComponent component, int difficulty)
    {
        return difficulty switch
        {
            1 => component.DifficultyOneAnnouncement,
            2 => component.DifficultyTwoAnnouncement,
            _ => component.DifficultyThreeAnnouncement,
        };
    }

    private Color GetColor(EclipseScenarioRuleComponent component, int difficulty)
    {
        return difficulty switch
        {
            1 => component.DifficultyOneColor,
            2 => component.DifficultyTwoColor,
            _ => component.DifficultyThreeColor,
        };
    }
}
