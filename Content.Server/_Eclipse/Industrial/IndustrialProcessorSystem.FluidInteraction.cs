using System.Linq;
using Content.Shared._Eclipse.Industrial;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;

namespace Content.Server._Eclipse.Industrial;

public sealed partial class IndustrialProcessorSystem
{
    [Dependency] private SharedInteractionSystem _fluidInteraction = default!;
    [Dependency] private ActionBlockerSystem _fluidActionBlocker = default!;
    [Dependency] private SharedDoAfterSystem _fluidDoAfter = default!;
    [Dependency] private ISharedAdminLogManager _fluidAdminLog = default!;

    protected override void OpenFluidSelection(Entity<IndustrialProcessorComponent> ent, EntityUid user, EntityUid container)
    {
        if (ent.Comp.InTransaction || !TryComp<IndustrialFluidProcessorComponent>(ent, out var fluid))
            return;
        var tanks = new List<IndustrialFluidTankState>();
        var input = TankState(ent, true, out var inputCapacity);
        if (inputCapacity > 0)
            tanks.Add(new IndustrialFluidTankState(true, inputCapacity, input));
        var output = TankState(ent, false, out var outputCapacity);
        if (outputCapacity > 0 && fluid.InputSolution != fluid.OutputSolution)
            tanks.Add(new IndustrialFluidTankState(false, outputCapacity, output));
        if (tanks.All(t => t.Contents.Length == 0))
        {
            _popup.PopupCursor(Loc.GetString("industrial-fluid-tank-empty"), user);
            return;
        }
        if (tanks.Count == 1 && tanks[0].Contents.Length == 1)
        {
            DrawFluid(ent, user, container, tanks[0].Input, tanks[0].Contents[0].Id);
            return;
        }
        _ui.SetUiState(ent.Owner, IndustrialProcessorUiKey.Fluid, new IndustrialFluidSelectionState(tanks.ToArray()));
        _ui.OpenUi(ent.Owner, IndustrialProcessorUiKey.Fluid, user);
    }

    private void OnFluidDrawFinished(Entity<IndustrialProcessorComponent> ent, ref IndustrialFluidDrawDoAfterEvent args)
    {
        if (!args.Cancelled && args.Args.Used is { } container)
            DrawFluid(ent, args.User, container, args.Input, args.Reagent, true);
    }

    private void DrawFluid(Entity<IndustrialProcessorComponent> ent, EntityUid user, EntityUid container,
        bool input, string? reagent, bool afterDelay = false)
    {
        // Recheck the real tank and the original held container; radial choices can become stale.
        if (ent.Comp.InTransaction || TerminatingOrDeleted(container) ||
            !TryComp<IndustrialFluidProcessorComponent>(ent, out var fluid) ||
            !_hands.TryGetActiveItem(user, out var held) || held != container ||
            !_fluidActionBlocker.CanInteract(user, ent.Owner) || !_fluidInteraction.InRangeUnobstructed(user, ent.Owner) ||
            !TryComp<SolutionTransferComponent>(container, out var transfer) || !transfer.CanReceive ||
            !TryComp<RefillableSolutionComponent>(container, out var refillable) ||
            !Solutions.TryGetRefillableSolution(container, out var recipient, out var recipientSolution) ||
            recipientSolution.Volume != 0 ||
            !Solutions.TryGetSolution(ent.Owner, input ? fluid.InputSolution : fluid.OutputSolution, out var donor, out var donorSolution))
            return;
        if (reagent == null || donorSolution.GetTotalPrototypeQuantity(reagent) <= 0)
        {
            _popup.PopupCursor(Loc.GetString("industrial-fluid-tank-empty"), user);
            return;
        }
        var amount = FixedPoint2.Min(transfer.TransferAmount, recipientSolution.AvailableVolume,
            donorSolution.GetTotalPrototypeQuantity(reagent));
        if (refillable.MaxRefill is { } maximum)
            amount = FixedPoint2.Min(amount, maximum);
        if (amount <= 0)
            return;
        var attempt = new SolutionTransferAttemptEvent(ent.Owner, container);
        RaiseLocalEvent(ent.Owner, ref attempt);
        RaiseLocalEvent(container, ref attempt);
        if (attempt.CancelReason is { } reason)
        {
            _popup.PopupCursor(reason, user);
            return;
        }
        var delay = refillable.RefillTime;
        if (TryComp<DrainableSolutionComponent>(ent, out var drainable))
            delay += drainable.DrainTime;
        if (!afterDelay && delay > TimeSpan.Zero)
        {
            _fluidDoAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, delay,
                new IndustrialFluidDrawDoAfterEvent(input, reagent), ent.Owner, ent.Owner, container)
            {
                NeedHand = true, BreakOnMove = true, BreakOnDamage = true, Hidden = true,
            });
            return;
        }

        // Native splitting preserves temperature and per-reagent data, including variants of the selected fluid.
        var drawn = donorSolution.SplitSolutionWithOnly(amount, reagent);
        if (!Solutions.TryAddSolution(recipient.Value, drawn))
            Solutions.TryAddSolution(donor.Value, drawn);
        else
        {
            var transferred = new SolutionTransferredEvent(ent.Owner, container, user, amount);
            RaiseLocalEvent(container, ref transferred);
            _fluidAdminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(user):player} drew {SharedSolutionContainerSystem.ToPrettyString(drawn)} from {ToPrettyString(ent.Owner):target} into {ToPrettyString(container)}");
            _popup.PopupCursor(Loc.GetString("industrial-fluid-drawn", ("amount", amount),
                ("reagent", PrototypeManager.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>(reagent).LocalizedName)), user);
        }
        Solutions.UpdateChemicals(donor.Value);
        UpdateUserInterface(ent);
    }
}
