using Content.Server.CartridgeLoader;
using Content.Server.Research.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Research.Components;
namespace Content.Server.Research.Systems;
public sealed class ResearchMailCartridgeSystem : EntitySystem
{
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly CartridgeLoaderSystem _cartridges = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<ResearchMailCartridgeComponent, CartridgeUiReadyEvent>((uid, component, args) =>
            _cartridges.UpdateCartridgeUiState(args.Loader, _research.GetResearchInbox(args.Loader)));
        SubscribeLocalEvent<ResearchMailCartridgeComponent, CartridgeMessageEvent>((uid, component, args) =>
        {
            var loader = GetEntity(args.LoaderUid);
            var feedback = args is ResearchMailActionMessage action ? _research.HandlePersonalMail(loader, action) : null;
            var state = _research.GetResearchInbox(loader);
            state.Feedback = feedback;
            _cartridges.UpdateCartridgeUiState(loader, state);
        });
    }
}
