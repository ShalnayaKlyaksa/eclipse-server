using System.Linq;
using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.CartridgeLoader.Cartridges;

/// <summary>
///     UI fragment responsible for displaying NanoTask controls in a PDA and coordinating with the NanoTaskCartridgeSystem for state
/// </summary>
public sealed partial class NanoTaskUi : UIFragment
{
    private NanoTaskUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new NanoTaskUiFragment();
        _fragment.ToggleTaskCompletion += id =>
        {
            if (_fragment.Tasks.Find(task => task.Id == id) is not NanoTaskItemAndId task)
                return;

            userInterface.SendMessage(new CartridgeUiMessage(new NanoTaskUiMessageEvent(new NanoTaskUpdateTask(new(id, new(
                description: task.Data.Description,
                taskIsFor: task.Data.TaskIsFor,
                isTaskDone: !task.Data.IsTaskDone,
                priority: task.Data.Priority
            ))))));
        };
        _fragment.Editor.Saved += (id, data) =>
        {
            if (id is { } taskId)
                userInterface.SendMessage(new CartridgeUiMessage(new NanoTaskUiMessageEvent(new NanoTaskUpdateTask(new(taskId, data)))));
            else
                userInterface.SendMessage(new CartridgeUiMessage(new NanoTaskUiMessageEvent(new NanoTaskAddTask(data))));
        };
        _fragment.Editor.Deleted += id =>
        {
            userInterface.SendMessage(new CartridgeUiMessage(new NanoTaskUiMessageEvent(new NanoTaskDeleteTask(id))));
        };
        _fragment.Editor.Printed += data =>
        {
            userInterface.SendMessage(new CartridgeUiMessage(new NanoTaskUiMessageEvent(new NanoTaskPrintTask(data))));
        };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not NanoTaskUiState nanoTaskState)
            return;

        _fragment?.UpdateState(nanoTaskState.Tasks);
    }
}
