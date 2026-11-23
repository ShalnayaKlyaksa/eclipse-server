using Content.Shared.PDA;

namespace Content.Client.PDA;

public sealed class PdaCosmeticsClientSystem : EntitySystem
{
    public PdaDesktopProfile Profile { get; private set; } = new();
    public int Merits { get; private set; }
    public int Shards { get; private set; }
    public bool Ready { get; private set; }
    public bool Pending { get; private set; }
    public string? Feedback { get; private set; }
    public event Action? Changed;
    public event Action<PdaPublicIdStateEvent>? PublicIdChanged;

    public override void Initialize()
    {
        SubscribeNetworkEvent<PdaCosmeticsStateEvent>(OnState);
        SubscribeNetworkEvent<PdaPublicIdStateEvent>(state => PublicIdChanged?.Invoke(state));
    }
    public void Request()
    {
        Ready = false;
        Pending = false;
        RaiseNetworkEvent(new PdaCosmeticsRequestEvent());
    }
    public void Buy(string id, bool shards)
    {
        if (!Ready || Pending) return;
        Pending = true;
        Changed?.Invoke();
        RaiseNetworkEvent(new PdaCosmeticBuyEvent(id, shards));
    }
    private void OnState(PdaCosmeticsStateEvent ev)
    {
        Profile = ev.Profile; Merits = ev.Merits; Shards = ev.Shards;
        Ready = true; Pending = false; Feedback = ev.Feedback;
        Changed?.Invoke();
    }

    public void RequestPublicId(int slot) => RaiseNetworkEvent(new PdaRequestPublicIdEvent(slot));

    public void SetPublicId(int slot, string id) => RaiseNetworkEvent(new PdaSetPublicIdEvent(slot, id));
}
