using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Players.PlayTimeTracking;
using Content.Server.Roles;
using Content.Server.Roles.Jobs;
using Content.Shared.CharacterInfo;
using Content.Shared.Eclipse.Progression;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Objectives;
using Content.Shared.Objectives.Components;
using Content.Shared.Objectives.Systems;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.CharacterInfo;

public sealed class CharacterInfoSystem : EntitySystem
{
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly JobSystem _jobs = default!;
    [Dependency] private readonly MindSystem _minds = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PlayTimeTrackingManager _playTime = default!;
    [Dependency] private readonly RoleSystem _roles = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedObjectivesSystem _objectives = default!;
    [Dependency] private readonly Eclipse.Progression.EclipseShiftStatsSystem _shiftStats = default!;

    private const string DefaultIcon = "/Textures/Interface/VerbIcons/examine.svg.192dpi.png";
    private const string EscapeIcon = "/Textures/Interface/VerbIcons/eject.svg.192dpi.png";
    private const string PickupIcon = "/Textures/Interface/VerbIcons/pickup.svg.192dpi.png";
    private const string SecurityIcon = "/Textures/Interface/VerbIcons/lock.svg.192dpi.png";
    private const string EngineeringIcon = "/Textures/Interface/VerbIcons/settings.svg.192dpi.png";
    private const string MedicalIcon = "/Textures/Interface/VerbIcons/rejuvenate.svg.192dpi.png";
    private const int PersonalTasksVersion = 3;

    private static readonly Dictionary<string, PersonalTaskTemplate[]> DepartmentTasks = new()
    {
        ["Command"] =
        [
            new("Вернуть капитанскую саблю", "К концу смены эвакуируйтесь на СИУ живым с личным активом капитана: {item}.", SecurityIcon, "CaptainSabre", "капитанская сабля"),
            new("Защитить блюспейс-телепорт", "К концу смены эвакуируйтесь на СИУ живым с ценным устройством командования: {item}.", EngineeringIcon, "HandTeleporter", "ручной телепорт"),
            new("Эвакуировать командный канал", "К концу смены эвакуируйтесь на СИУ живым с ключом связи командования: {item}.", SecurityIcon, "EncryptionKeyCommand", "командный ключ шифрования"),
        ],
        ["Security"] =
        [
            new("Сохранить оружейный актив", "К концу смены эвакуируйтесь на СИУ живым с табельным вооружением: {item}.", SecurityIcon, "WeaponLaserCarbine", "лазерный карабин"),
            new("Эвакуировать криминалистику", "К концу смены эвакуируйтесь на СИУ живым с оборудованием расследований: {item}.", SecurityIcon, "ForensicScanner", "криминалистический сканер"),
            new("Сохранить хардсьют СБ", "К концу смены эвакуируйтесь на СИУ живым с защитным снаряжением отдела: {item}.", SecurityIcon, "ClothingOuterHardsuitSecurity", "хардсьют СБ"),
            new("Вернуть визор охраны", "К концу смены эвакуируйтесь на СИУ живым с рабочим визором службы безопасности: {item}.", SecurityIcon, "ClothingEyesHudSecurity", "визор СБ"),
            new("Сохранить канал СБ", "К концу смены эвакуируйтесь на СИУ живым с ключом связи службы безопасности: {item}.", SecurityIcon, "EncryptionKeySecurity", "ключ шифрования СБ"),
        ],
        ["Engineering"] =
        [
            new("Эвакуировать RCD", "К концу смены эвакуируйтесь на СИУ живым с главным ремонтным устройством: {item}.", EngineeringIcon, "RCD", "RCD"),
            new("Сохранить челюсти жизни", "К концу смены эвакуируйтесь на СИУ живым с аварийным инструментом отдела: {item}.", EngineeringIcon, "JawsOfLife", "челюсти жизни"),
            new("Вернуть инженерный хардсьют", "К концу смены эвакуируйтесь на СИУ живым с защитным снаряжением инженерии: {item}.", EngineeringIcon, "ClothingOuterHardsuitEngineering", "инженерный хардсьют"),
            new("Эвакуировать магнитные ботинки", "К концу смены эвакуируйтесь на СИУ живым с оборудованием для внешних работ: {item}.", EngineeringIcon, "ClothingShoesBootsMag", "магнитные ботинки"),
            new("Сохранить канал инженерии", "К концу смены эвакуируйтесь на СИУ живым с ключом связи инженерии: {item}.", SecurityIcon, "EncryptionKeyEngineering", "инженерный ключ шифрования"),
        ],
        ["Medical"] =
        [
            new("Сохранить гипоспрей", "К концу смены эвакуируйтесь на СИУ живым с ценным медицинским устройством: {item}.", MedicalIcon, "Hypospray", "гипоспрей"),
            new("Эвакуировать компактный дефибриллятор", "К концу смены эвакуируйтесь на СИУ живым с реанимационным оборудованием: {item}.", MedicalIcon, "DefibrillatorCompact", "компактный дефибриллятор"),
            new("Вернуть продвинутую аптечку", "К концу смены эвакуируйтесь на СИУ живым с расширенным набором лечения: {item}.", MedicalIcon, "MedkitAdvanced", "продвинутая аптечка"),
            new("Сохранить медицинский хардсьют", "К концу смены эвакуируйтесь на СИУ живым с защитным снаряжением медбея: {item}.", MedicalIcon, "ClothingOuterHardsuitMedical", "медицинский хардсьют"),
            new("Сохранить канал медбея", "К концу смены эвакуируйтесь на СИУ живым с ключом связи медицинского отдела: {item}.", SecurityIcon, "EncryptionKeyMedical", "медицинский ключ шифрования"),
        ],
        ["Science"] =
        [
            new("Эвакуировать диск технологий", "К концу смены эвакуируйтесь на СИУ живым с исследовательским носителем: {item}.", PickupIcon, "TechnologyDisk", "диск технологий"),
            new("Сохранить аномальный сканер", "К концу смены эвакуируйтесь на СИУ живым с оборудованием ксеноархеологии: {item}.", EngineeringIcon, "AnomalyScanner", "сканер аномалий"),
            new("Вернуть сканер узлов", "К концу смены эвакуируйтесь на СИУ живым с научным анализатором: {item}.", EngineeringIcon, "NodeScanner", "сканер узлов"),
            new("Эвакуировать контейнер артефактов", "К концу смены эвакуируйтесь на СИУ живым с контейнером для опасных образцов: {item}.", PickupIcon, "HandheldArtifactContainer", "ручной контейнер артефактов"),
            new("Сохранить канал науки", "К концу смены эвакуируйтесь на СИУ живым с ключом связи научного отдела: {item}.", SecurityIcon, "EncryptionKeyScience", "научный ключ шифрования"),
        ],
        ["Cargo"] =
        [
            new("Сохранить рудный ящик", "К концу смены эвакуируйтесь на СИУ живым с грузовым активом шахтёров: {item}.", PickupIcon, "CargoOreBox", "рудный ящик"),
            new("Эвакуировать спасательный хардсьют", "К концу смены эвакуируйтесь на СИУ живым с ценным снаряжением снабжения: {item}.", PickupIcon, "ClothingOuterHardsuitSpatio", "скафандр утилизаторов"),
            new("Вернуть грузовую маркировку", "К концу смены эвакуируйтесь на СИУ живым с оборудованием учёта поставок: {item}.", EngineeringIcon, "HandLabeler", "этикетировщик"),
            new("Сохранить канал снабжения", "К концу смены эвакуируйтесь на СИУ живым с ключом связи снабжения: {item}.", SecurityIcon, "EncryptionKeyCargo", "ключ шифрования снабжения"),
            new("Доставить аварийный кислород", "К концу смены эвакуируйтесь на СИУ живым с резервным баллоном: {item}.", PickupIcon, "EmergencyOxygenTank", "аварийный кислородный баллон"),
        ],
        ["Civilian"] =
        [
            new("Эвакуировать сервисный запас", "К концу смены эвакуируйтесь на СИУ живым с готовой едой для экипажа: {item}.", PickupIcon, "FoodSnackChocolate", "еда"),
            new("Сохранить пожарный резерв", "К концу смены эвакуируйтесь на СИУ живым с аварийным средством сервиса: {item}.", EngineeringIcon, "FireExtinguisher", "огнетушитель"),
            new("Доставить питьевой запас", "К концу смены эвакуируйтесь на СИУ живым с запасом воды: {item}.", PickupIcon, "DrinkWaterBottleFull", "бутылка воды"),
            new("Сохранить канал сервиса", "К концу смены эвакуируйтесь на СИУ живым с ключом связи сервиса: {item}.", SecurityIcon, "EncryptionKeyService", "ключ шифрования сервиса"),
            new("Вернуть универсальный инструмент", "К концу смены эвакуируйтесь на СИУ живым с полезным инструментом для ремонта: {item}.", EngineeringIcon, "Multitool", "мультитул"),
        ],
        ["Silicon"] =
        [
            new("Сохранить диагностический визор", "К концу смены эвакуируйтесь на СИУ живым с диагностическим оборудованием: {item}.", EngineeringIcon, "ClothingEyesHudDiagnostic", "диагностический визор"),
            new("Эвакуировать мультитул", "К концу смены эвакуируйтесь на СИУ живым с инструментом технической поддержки: {item}.", EngineeringIcon, "Multitool", "мультитул"),
            new("Доставить медицинский анализатор", "К концу смены эвакуируйтесь на СИУ живым с устройством диагностики экипажа: {item}.", MedicalIcon, "HandheldHealthAnalyzer", "анализатор здоровья"),
            new("Сохранить пожарный резерв", "К концу смены эвакуируйтесь на СИУ живым со средством тушения: {item}.", EngineeringIcon, "FireExtinguisher", "огнетушитель"),
            new("Доставить резерв питания", "К концу смены эвакуируйтесь на СИУ живым с батареей для модулей: {item}.", EngineeringIcon, "PowerCellSmall", "малая батарея"),
        ],
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<RequestCharacterInfoEvent>(OnRequestCharacterInfoEvent);
        SubscribeLocalEvent<RoundEndTextAppendEvent>(OnRoundEndTextAppend);
    }

    private void OnRequestCharacterInfoEvent(RequestCharacterInfoEvent msg, EntitySessionEventArgs args)
    {
        if (!args.SenderSession.AttachedEntity.HasValue
            || args.SenderSession.AttachedEntity != GetEntity(msg.NetEntity))
            return;

        var entity = args.SenderSession.AttachedEntity.Value;

        var objectives = new Dictionary<string, List<ObjectiveInfo>>();
        var jobTitle = Loc.GetString("character-info-no-profession");
        string? briefing = null;
        var personalTasks = new List<PersonalTaskInfo>();
        if (_minds.TryGetMind(entity, out var mindId, out var mind))
        {
            foreach (var objective in mind.Objectives)
            {
                var info = _objectives.GetInfo(objective, mindId, mind);
                if (info == null)
                    continue;

                var issuer = Comp<ObjectiveComponent>(objective).LocIssuer;
                if (!objectives.ContainsKey(issuer))
                    objectives[issuer] = new List<ObjectiveInfo>();
                objectives[issuer].Add(info.Value);
            }

            if (_jobs.MindTryGetJobName(mindId, out var jobName))
                jobTitle = jobName;

            personalTasks = GetOrGeneratePersonalTasks(mindId);
            UpdatePersonalTasks(mindId, mind, entity, roundEnd: false);
            if (TryComp<PersonalTasksComponent>(mindId, out var tasks))
                personalTasks = tasks.Tasks;

            briefing = _roles.MindGetBriefing(mindId);
        }

        RaiseNetworkEvent(new CharacterInfoEvent(GetNetEntity(entity), jobTitle, objectives, briefing, personalTasks), args.SenderSession);
    }

    private void OnRoundEndTextAppend(RoundEndTextAppendEvent ev)
    {
        var query = EntityQueryEnumerator<MindComponent, PersonalTasksComponent>();
        while (query.MoveNext(out var mindId, out var mind, out _))
        {
            if (mind.CurrentEntity is not { } player)
                continue;

            UpdatePersonalTasks(mindId, mind, player, roundEnd: true);
        }
    }

    private List<PersonalTaskInfo> GetOrGeneratePersonalTasks(EntityUid mindId)
    {
        if (!_jobs.MindTryGetJobId(mindId, out var jobId) || jobId == null)
            return new List<PersonalTaskInfo>();

        var job = jobId.Value.ToString();
        var tasks = EnsureComp<PersonalTasksComponent>(mindId);
        if (tasks.JobId == job && tasks.Version == PersonalTasksVersion && tasks.Tasks.Count > 0)
            return tasks.Tasks;

        tasks.JobId = job;
        tasks.Version = PersonalTasksVersion;
        tasks.Tasks = GeneratePersonalTasks(job);
        return tasks.Tasks;
    }

    private List<PersonalTaskInfo> GeneratePersonalTasks(string jobId)
    {
        var result = new List<PersonalTaskInfo>
        {
            CreateTask(
                "Успешно завершите смену",
                "Улетите на СИУ живым к концу смены.",
                EscapeIcon,
                true,
                PersonalTaskCondition.SurviveRound,
                null,
                80,
                30),
        };

        var departmentId = GetDepartmentId(jobId);
        var templates = DepartmentTasks.TryGetValue(departmentId, out var departmentTasks)
            ? departmentTasks
            : DepartmentTasks["Civilian"];

        var taskCount = _random.Next(1, 3);
        var picked = templates.ToList();
        _random.Shuffle(picked);

        foreach (var template in picked.Take(taskCount))
            result.Add(template.Generate(_random));

        return result;
    }

    private void UpdatePersonalTasks(EntityUid mindId, MindComponent mind, EntityUid player, bool roundEnd)
    {
        if (!TryComp<PersonalTasksComponent>(mindId, out var tasks))
            return;

        var changed = false;
        for (var i = 0; i < tasks.Tasks.Count; i++)
        {
            var task = tasks.Tasks[i];
            if (!task.Completed && IsTaskComplete(task, player, roundEnd))
            {
                task = task with { Completed = true };
                changed = true;
            }

            if (task.Completed && !task.Rewarded)
            {
                GiveReward(mind, player, task);
                task = task with { Rewarded = true };
                changed = true;
            }

            tasks.Tasks[i] = task;
        }

        if (changed)
            Dirty(mindId, tasks);
    }

    private bool IsTaskComplete(PersonalTaskInfo task, EntityUid player, bool roundEnd)
    {
        return task.Condition switch
        {
            PersonalTaskCondition.SurviveRound => roundEnd && _mobState.IsAlive(player),
            PersonalTaskCondition.HaveItem => roundEnd && _mobState.IsAlive(player) && task.TargetPrototype != null && HasPrototypeInPossession(player, task.TargetPrototype),
            _ => false,
        };
    }

    private bool HasPrototypeInPossession(EntityUid player, string prototype)
    {
        foreach (var item in _inventory.GetHandOrInventoryEntities((player, CompOrNull<HandsComponent>(player), CompOrNull<InventoryComponent>(player))))
        {
            if (EntityOrContentsMatches(item, prototype))
                return true;
        }

        return false;
    }

    private bool EntityOrContentsMatches(EntityUid uid, string prototype)
    {
        if (!Exists(uid))
            return false;

        if (MetaData(uid).EntityPrototype?.ID == prototype)
            return true;

        var stack = new Stack<EntityUid>();
        var visited = new HashSet<EntityUid>();
        stack.Push(uid);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current) || !TryComp<ContainerManagerComponent>(current, out var containerManager))
                continue;

            foreach (var container in _containers.GetAllContainers(current, containerManager))
            {
                foreach (var contained in container.ContainedEntities)
                {
                    if (!Exists(contained))
                        continue;

                    if (MetaData(contained).EntityPrototype?.ID == prototype)
                        return true;

                    stack.Push(contained);
                }
            }
        }

        return false;
    }

    private void GiveReward(MindComponent mind, EntityUid player, PersonalTaskInfo task)
    {
        if (mind.UserId is { } userId && _players.TryGetSessionById(userId, out var session))
        {
            if (task.ExperienceReward > 0)
            {
                var bonusTime = TimeSpan.FromMinutes((double) task.ExperienceReward / EclipseProgression.BonusExperiencePerMinute);
                _playTime.AddTimeToTracker(session, EclipseProgression.BonusExperienceTracker, bonusTime);
            }

            _shiftStats.RecordTaskReward(session, task.Title, task.ExperienceReward, task.CreditsReward);

            _chat.DispatchServerMessage(
                session,
                $"Задание выполнено: {task.Title}. Награда: {task.ExperienceReward} XP / {task.CreditsReward} Креды.",
                suppressLog: true);
        }
        else if (_players.TryGetSessionByEntity(player, out var entitySession))
        {
            _chat.DispatchServerMessage(
                entitySession,
                $"Задание выполнено: {task.Title}. Награда: {task.ExperienceReward} XP / {task.CreditsReward} Креды.",
                suppressLog: true);
        }
    }

    private string GetDepartmentId(string jobId)
    {
        if (_jobs.TryGetAllDepartments(jobId, out var departments) && departments.Count > 0)
        {
            departments.Sort(DepartmentUIComparer.Instance);
            return departments[0].ID;
        }

        return "Civilian";
    }

    private static PersonalTaskInfo CreateTask(
        string title,
        string description,
        string icon,
        bool highlighted,
        PersonalTaskCondition condition,
        string? targetPrototype,
        int xp,
        int credits)
    {
        return new PersonalTaskInfo(
            title,
            description,
            FormatReward(xp, credits),
            icon,
            highlighted,
            false,
            false,
            condition,
            targetPrototype,
            xp,
            credits);
    }

    private static string FormatReward(int xp, int credits)
    {
        return $"Награда: {xp} XP / {credits} Креды";
    }

    private sealed record PersonalTaskTemplate(
        string Title,
        string Description,
        string Icon,
        string TargetPrototype,
        string ItemName)
    {
        public PersonalTaskInfo Generate(IRobustRandom random)
        {
            var description = Description.Replace("{item}", ItemName);
            var xp = RollRounded(random, 40, 90, 10);
            var credits = RollRounded(random, 15, 45, 5);

            return CreateTask(
                Title,
                description,
                Icon,
                false,
                PersonalTaskCondition.HaveItem,
                TargetPrototype,
                xp,
                credits);
        }

        private static int RollRounded(IRobustRandom random, int min, int max, int step)
        {
            var minStep = (int) Math.Ceiling(min / (double) step);
            var maxStep = (int) Math.Floor(max / (double) step);
            return random.Next(minStep, maxStep + 1) * step;
        }
    }
}
