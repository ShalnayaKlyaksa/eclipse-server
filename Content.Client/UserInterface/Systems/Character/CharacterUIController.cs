using System.Linq;
using System.Numerics;
using Content.Client.CharacterInfo;
using Content.Client.Gameplay;
using Content.Client.MainMenu.UI;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Controls;
using Content.Client.UserInterface.Systems.Character.Controls;
using Content.Client.UserInterface.Systems.Character.Windows;
using Content.Client.UserInterface.Systems.Objectives.Controls;
using Content.Shared.Input;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Roles;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using static Content.Client.CharacterInfo.CharacterInfoSystem;
using static Robust.Client.UserInterface.Controls.BaseButton;

namespace Content.Client.UserInterface.Systems.Character;

[UsedImplicitly]
public sealed class CharacterUIController : UIController, IOnStateEntered<GameplayState>, IOnStateExited<GameplayState>, IOnSystemChanged<CharacterInfoSystem>
{
    [Dependency] private readonly IEntityManager _ent = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    [UISystemDependency] private readonly CharacterInfoSystem _characterInfo = default!;
    [UISystemDependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MindRoleTypeChangedEvent>(OnRoleTypeChanged);
    }

    private CharacterWindow? _window;
    private MenuButton? CharacterButton => UIManager.GetActiveUIWidgetOrNull<MenuBar.Widgets.GameTopMenuBar>()?.CharacterButton;

    public void OnStateEntered(GameplayState state)
    {
        DebugTools.Assert(_window == null);

        _window = UIManager.CreateWindow<CharacterWindow>();
        LayoutContainer.SetAnchorPreset(_window, LayoutContainer.LayoutPreset.CenterTop);

        _window.OnClose += DeactivateButton;
        _window.OnOpen += ActivateButton;

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.OpenCharacterMenu,
                InputCmdHandler.FromDelegate(_ => ToggleWindow()))
            .Register<CharacterUIController>();
    }

    public void OnStateExited(GameplayState state)
    {
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }

        CommandBinds.Unregister<CharacterUIController>();
    }

    public void OnSystemLoaded(CharacterInfoSystem system)
    {
        system.OnCharacterUpdate += CharacterUpdated;
        _player.LocalPlayerDetached += CharacterDetached;
    }

    public void OnSystemUnloaded(CharacterInfoSystem system)
    {
        system.OnCharacterUpdate -= CharacterUpdated;
        _player.LocalPlayerDetached -= CharacterDetached;
    }

    public void UnloadButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.OnPressed -= CharacterButtonPressed;
    }

    public void LoadButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.OnPressed += CharacterButtonPressed;
    }

    private void DeactivateButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.Pressed = false;
    }

    private void ActivateButton()
    {
        if (CharacterButton == null)
        {
            return;
        }

        CharacterButton.Pressed = true;
    }

    private void CharacterUpdated(CharacterData data)
    {
        if (_window == null)
        {
            return;
        }

        var (entity, job, objectives, briefing, entityName, personalTasks) = data;

        _window.SpriteView.SetEntity(entity);

        UpdateRoleType();

        _window.NameLabel.Text = entityName;
        _window.NameLabel.FontColorOverride = Color.White;
        _window.SubText.Text = string.IsNullOrWhiteSpace(job)
            ? "Должность не назначена"
            : job;
        _window.SubText.FontColorOverride = Color.FromHex("#D5C9AE");
        _window.RoleType.Visible = false;
        _window.Objectives.RemoveAllChildren();
        _window.PersonalTasks.RemoveAllChildren();
        _window.ObjectivesLabel.Visible = false;
        _window.ObjectivesScroll.Visible = false;
        _window.RoleEmptyLabel.Visible = true;

        foreach (var task in personalTasks)
        {
            AddPersonalTask(
                _window.PersonalTasks,
                task.Title,
                task.Description,
                task.Reward,
                task.Icon,
                task.Highlighted,
                task.Completed);
        }

        foreach (var (groupId, conditions) in objectives)
        {
            var objectiveControl = new CharacterObjectiveControl
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Modulate = Color.Gray
            };


            var objectiveText = new FormattedMessage();
            objectiveText.TryAddMarkup(groupId, out _);

            var objectiveLabel = new RichTextLabel
            {
                StyleClasses = { StyleClass.TooltipTitle }
            };
            objectiveLabel.SetMessage(objectiveText);

            objectiveControl.AddChild(objectiveLabel);

            foreach (var condition in conditions)
            {
                var conditionControl = new ObjectiveConditionsControl();
                conditionControl.ProgressTexture.Texture = _sprite.Frame0(condition.Icon);
                conditionControl.ProgressTexture.Progress = condition.Progress;
                var titleMessage = new FormattedMessage();
                var descriptionMessage = new FormattedMessage();
                titleMessage.AddText(condition.Title);
                descriptionMessage.AddText(condition.Description);

                conditionControl.Title.SetMessage(titleMessage);
                conditionControl.Description.SetMessage(descriptionMessage);

                objectiveControl.AddChild(conditionControl);
            }

            _window.Objectives.AddChild(objectiveControl);
        }

        if (briefing != null)
        {
            var briefingControl = new ObjectiveBriefingControl();
            var text = new FormattedMessage();
            text.PushColor(Color.Yellow);
            text.AddText(briefing);
            briefingControl.Label.SetMessage(text);
            _window.Objectives.AddChild(briefingControl);
        }

        var controls = _characterInfo.GetCharacterInfoControls(entity);
        foreach (var control in controls)
        {
            _window.Objectives.AddChild(control);
        }

        var hasObjectiveContent = objectives.Any() || briefing != null || controls.Any();
        _window.ObjectivesLabel.Visible = hasObjectiveContent;
        _window.ObjectivesScroll.Visible = hasObjectiveContent;
        _window.RoleEmptyLabel.Visible = !hasObjectiveContent;
        _window.RolePlaceholder.Visible = false;
    }

    private static void AddPersonalTask(
        BoxContainer container,
        string title,
        string description,
        string reward,
        string icon,
        bool highlighted,
        bool completed)
    {
        var panel = new PanelContainer
        {
            MinSize = new Vector2(0f, 126f),
            HorizontalExpand = true,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex(completed ? "#071A0DD8" : highlighted ? "#1A0D00D8" : "#070300D8"),
                BorderColor = Color.FromHex(completed ? "#62D17A99" : highlighted ? "#E6A11A99" : "#D5C9AE66"),
                BorderThickness = new Thickness(1),
            },
        };
        container.AddChild(panel);

        var card = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
        };
        panel.AddChild(card);

        card.AddChild(new PanelContainer
        {
            SetWidth = 8f,
            PanelOverride = new StyleBoxFlat
            {
                BackgroundColor = Color.FromHex(completed ? "#62D17A" : highlighted ? "#E6A11A" : "#00000000"),
            },
        });

        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            Margin = new Thickness(14, 12),
            SeparationOverride = 16,
            HorizontalExpand = true,
        };
        card.AddChild(row);

        row.AddChild(new TextureRect
        {
            TexturePath = icon,
            SetSize = new Vector2(44f, 44f),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            ModulateSelfOverride = Color.FromHex(completed ? "#62D17A" : highlighted ? "#E6A11A" : "#D5C9AE"),
        });

        var texts = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        row.AddChild(texts);

        var titleMessage = new FormattedMessage();
        titleMessage.PushColor(completed ? Color.FromHex("#62D17A") : highlighted ? Color.FromHex("#E6A11A") : Color.White);
        titleMessage.AddText(completed ? $"{title} - выполнено" : title);
        titleMessage.Pop();

        var titleLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            MaxWidth = 380f,
        };
        titleLabel.SetMessage(titleMessage);
        texts.AddChild(titleLabel);

        var descriptionLabel = new RichTextLabel
        {
            HorizontalExpand = true,
            MaxWidth = 380f,
        };
        descriptionLabel.SetMessage(FormattedMessage.FromUnformatted(description));
        texts.AddChild(descriptionLabel);

        texts.AddChild(new Label
        {
            Text = reward,
            StyleIdentifier = completed
                ? MainMenuControl.StyleIdentifierSubtle
                : MainMenuControl.StyleIdentifierGoldSmall,
            ClipText = false,
        });
    }

    private void OnRoleTypeChanged(MindRoleTypeChangedEvent ev, EntitySessionEventArgs _)
    {
        UpdateRoleType();
        _characterInfo.RequestCharacterInfo();
    }

    private void UpdateRoleType()
    {
        if (_window == null || !_window.IsOpen)
            return;

        if (!_ent.TryGetComponent<MindContainerComponent>(_player.LocalEntity, out var container)
            || container.Mind is null)
            return;

        if (!_ent.TryGetComponent<MindComponent>(container.Mind.Value, out var mind))
            return;

        if (!_prototypeManager.TryIndex(mind.RoleType, out var proto))
            Log.Error($"Player '{_player.LocalSession}' has invalid Role Type '{mind.RoleType}'. Displaying default instead");

        var roles = _ent.System<SharedRoleSystem>();
        var roleName = roles.GetRoleSubtypeLabel(proto?.Name ?? RoleTypePrototype.FallbackName, mind.Subtype);
        var roleColor = proto?.Color ?? RoleTypePrototype.FallbackColor;

        _window.RoleHeader.Text = roleName;
        _window.RoleHeader.FontColorOverride = roleColor;
        _window.RoleType.Text = roleName;
        _window.RoleType.FontColorOverride = roleColor;
    }

    private void CharacterDetached(EntityUid uid)
    {
        CloseWindow();
    }

    private void CharacterButtonPressed(ButtonEventArgs args)
    {
        ToggleWindow();
    }

    private void CloseWindow()
    {
        _window?.Close();
    }

    private void ToggleWindow()
    {
        if (_window == null)
            return;

        CharacterButton?.SetClickPressed(!_window.IsOpen);

        if (_window.IsOpen)
        {
            CloseWindow();
        }
        else
        {
            _characterInfo.RequestCharacterInfo();
            _window.Open();
        }
    }
}
