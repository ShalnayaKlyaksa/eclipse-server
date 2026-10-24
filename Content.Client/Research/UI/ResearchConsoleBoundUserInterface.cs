using System.Linq;
using Content.Client._Erida.Research.UI;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.Research.UI;

[UsedImplicitly]
public sealed class ResearchConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private FancyResearchConsoleMenu? _consoleMenu;  // Goobstation R&D Console rework
    public ResearchConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        var owner = Owner;

        _consoleMenu = this.CreateWindow<FancyResearchConsoleMenu>();   // Goobstation R&D Console rework
        _consoleMenu.SetEntity(owner);
        _consoleMenu.OnClose += () => _consoleMenu = null; // Goobstation R&D Console rework

        _consoleMenu.OnTechnologyCardPressed += id =>
        {
            SendMessage(new ConsoleUnlockTechnologyMessage(id));
        };

        _consoleMenu.OnServerButtonPressed += () =>
        {
            SendMessage(new ConsoleServerSelectionMessage());
        };
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);

        if (!args.WasModified<TechnologyPrototype>())
            return;

        if (State is not ResearchConsoleBoundInterfaceState rState)
            return;

        _consoleMenu?.UpdatePanels(rState.Researches); // Goobstation R&D Console rework
        _consoleMenu?.UpdateInformationPanel(rState.Points); // Goobstation R&D Console rework
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ResearchConsoleBoundInterfaceState castState)
            return;

        // Goobstation R&D Console rework start
        if (_consoleMenu == null)
            return;
        if (!_consoleMenu.List.SequenceEqual(castState.Researches))
            _consoleMenu.UpdatePanels(castState.Researches);
        if (_consoleMenu.Points != castState.Points)
            _consoleMenu.UpdateInformationPanel(castState.Points);
        // Goobstation R&D Console rework end
    }
}
