using Content.Server.Access.Systems;
using Content.Server.Power.Components;
using Content.Server.Research.Components;
using Content.Server.Stack;
using Content.Shared.Cargo.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.PDA;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server.PDA;

public sealed class PdaAtmSystem : EntitySystem
{
    [Dependency] private readonly PdaPersonalServicesSystem _bank = default!;
    [Dependency] private readonly IdCardSystem _idCards = default!;
    [Dependency] private readonly StackSystem _stacks = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private readonly Dictionary<(EntityUid, EntityUid), Queue<Guid>> _operations = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PdaAtmComponent, PdaAtmRequestMessage>((uid, _, message) => { if (PdaAtmUiKey.Key.Equals(message.UiKey)) SendState(uid, message.Actor); });
        SubscribeLocalEvent<PdaAtmComponent, PdaAtmOperationMessage>(OnOperation);
        SubscribeLocalEvent<PdaAtmComponent, InteractUsingEvent>(OnCash);
        SubscribeLocalEvent<Content.Shared.GameTicking.RoundRestartCleanupEvent>(_ => _operations.Clear());
    }

    private bool Identity(EntityUid actor, out PdaEmployeeCardComponent account)
    {
        account = default!;
        if (!_idCards.TryFindIdCard(actor, out var card) || !TryComp<PdaEmployeeCardComponent>(card.Owner, out var found) ||
            !TryComp<ResearchAccountsComponent>(found.Domain, out var registry) || !registry.Credentials.ContainsKey(found.Username)) return false;
        account = found;
        return true;
    }

    private bool Powered(EntityUid uid) => TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered;

    private void OnOperation(EntityUid uid, PdaAtmComponent component, PdaAtmOperationMessage message)
    {
        if (!PdaAtmUiKey.Key.Equals(message.UiKey) || !Powered(uid) || !Identity(message.Actor, out var account))
        { SendState(uid, message.Actor, "pda-atm-no-card"); return; }
        var key = (uid, message.Actor);
        if (!_operations.TryGetValue(key, out var requests)) _operations[key] = requests = new();
        if (requests.Contains(message.RequestId)) { SendState(uid, message.Actor, "pda-atm-done"); return; }
        if (message.RequestId == Guid.Empty || message.Amount <= 0 || message.Amount > 1000000 || _timing.CurTime < component.NextOperation)
        { SendState(uid, message.Actor, "pda-services-invalid"); return; }
        var success = message.Withdraw
            ? Withdraw(account.Domain, account.Username, uid, message.Amount, out var cash)
            : _hands.TryGetActiveItem(message.Actor, out var held) && Deposit(account.Domain, account.Username, held.Value, message.Amount);
        if (success)
        {
            component.NextOperation = _timing.CurTime + TimeSpan.FromSeconds(.5);
            requests.Enqueue(message.RequestId);
            if (requests.Count > 64) requests.Dequeue();
        }
        SendState(uid, message.Actor, success ? "pda-atm-done" : "pda-atm-failed");
    }

    private void OnCash(EntityUid uid, PdaAtmComponent component, ref InteractUsingEvent args)
    {
        if (args.Handled || !Powered(uid) || !HasComp<CashComponent>(args.Used) || !Identity(args.User, out var account) ||
            !TryComp<StackComponent>(args.Used, out var stack)) return;
        var success = Deposit(account.Domain, account.Username, args.Used, stack.Count);
        args.Handled = success;
        SendState(uid, args.User, success ? "pda-atm-done" : "pda-atm-failed");
    }

    public bool Deposit(EntityUid domain, string username, EntityUid cash, int amount)
    {
        if (amount <= 0 || !_bank.HasAccount(domain, username) || !HasComp<CashComponent>(cash) || !TryComp<StackComponent>(cash, out var stack) || stack.StackTypeId.Id != "Credit" ||
            stack.Unlimited || amount > stack.Count || _bank.CreditCapacity(domain, username) < amount) return false;
        if (!_stacks.TryUse((cash, stack), amount)) return false;
        return _bank.AdjustCredits(domain, username, amount, Loc.GetString("pda-atm-deposit-entry"));
    }

    public bool Withdraw(EntityUid domain, string username, EntityUid machine, int amount, out EntityUid? cash)
    {
        cash = null;
        if (amount <= 0 || amount > 1000000 || !_bank.HasAccount(domain, username) || _bank.Balance(domain, username) < amount) return false;
        var spawned = _stacks.SpawnAtPosition(amount, "Credit", Transform(machine).Coordinates);
        if (_stacks.GetCount(spawned) != amount || !_bank.AdjustCredits(domain, username, -amount, Loc.GetString("pda-atm-withdraw-entry")))
        { QueueDel(spawned); return false; }
        cash = spawned;
        return true;
    }

    private void SendState(EntityUid machine, EntityUid actor, string? feedback = null)
    {
        var state = new PdaAtmStateMessage("", "", 0, feedback);
        if (Identity(actor, out var account))
        {
            var credential = Comp<ResearchAccountsComponent>(account.Domain).Credentials[account.Username];
            state.Id = credential.PublicId; state.Name = credential.Name; state.Balance = _bank.Balance(account.Domain, account.Username);
        }
        _ui.ServerSendUiMessage(machine, PdaAtmUiKey.Key, state, actor);
    }
}
