using System.Linq;
using System.Globalization;
using Content.Client.UserInterface.Controls;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.UserInterface;
using Robust.Shared.Utility;

namespace Content.Client._Eclipse.Industrial;

public sealed class IndustrialFluidSelectionBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private SimpleRadialMenu? _menu;
    private NetEntity? _container;

    protected override void Open()
    {
        base.Open();
        if (PlayerManager.LocalEntity is not { } user ||
            !EntMan.System<SharedHandsSystem>().TryGetActiveItem(user, out var held))
        {
            Close();
            return;
        }
        _container = EntMan.GetNetEntity(held.Value);
        _menu = this.CreateWindow<SimpleRadialMenu>();
        if (State is IndustrialFluidSelectionState state)
            _menu.SetButtons(BuildOptions(state, Select));
        _menu.Track(Owner);
        _menu.OpenOverMouseScreenPosition();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is IndustrialFluidSelectionState fluids)
            _menu?.SetButtons(BuildOptions(fluids, Select));
    }

    private void Select(bool input, string? reagent)
    {
        if (_container is { } container)
            SendMessage(new IndustrialProcessorDrainMessage(input, reagent, container));
    }

    public static RadialMenuOptionBase[] BuildOptions(IndustrialFluidSelectionState state, Action<bool, string?> select)
    {
        if (state.Tanks.Length == 1)
            return Fluids(state.Tanks[0], select);
        return state.Tanks.Select(tank =>
        {
            var fluids = Fluids(tank, select);
            var name = Loc.GetString(tank.Input ? "industrial-ui-fluid-input" : "industrial-ui-fluid-output");
            var tooltip = $"{name}\n{tank.Contents.Sum(f => f.Amount):0.#} / {tank.Capacity:0.#} u";
            if (tank.Contents.Length == 0)
                tooltip += "\n" + Loc.GetString("industrial-fluid-tank-empty");
            RadialMenuOptionBase option = fluids.Length > 1
                ? new RadialMenuNestedLayerOption(fluids) { ToolTip = tooltip }
                : new RadialMenuActionOption<bool>(input => select(input, tank.Contents.FirstOrDefault()?.Id), tank.Input) { ToolTip = tooltip };
            option.IconSpecifier = RadialMenuIconSpecifier.With(new SpriteSpecifier.Texture(new ResPath(
                tank.Input ? "/Textures/Interface/VerbIcons/insert.svg.192dpi.png" : "/Textures/Interface/VerbIcons/eject.svg.192dpi.png")));
            option.IconColor = tank.Contents.Length > 0 ? Color.White : Color.Gray;
            return option;
        }).ToArray();
    }

    private static RadialMenuOptionBase[] Fluids(IndustrialFluidTankState tank, Action<bool, string?> select)
        => tank.Contents.Select(fluid => (RadialMenuOptionBase) new RadialMenuActionOption<string>(
            id => select(tank.Input, id), fluid.Id)
        {
            IconSpecifier = RadialMenuIconSpecifier.With(new SpriteSpecifier.Rsi(
                new ResPath("_Eclipse/Structures/Piping/liquid_pipe.rsi"), "content")),
            IconColor = fluid.Color.WithAlpha(1f),
            ToolTip = fluid.Name + "\n" + fluid.Amount.ToString("0.#", CultureInfo.CurrentCulture) + " u",
        }).ToArray();
}
