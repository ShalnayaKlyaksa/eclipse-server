using Content.Shared.Verbs;
using Robust.Client.UserInterface;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client.UserInterface.Systems.Emotes;

public sealed class DirectedEmotesSystem : EntitySystem
{
    private const float InteractionRange = 3f;

    [Dependency] private readonly IUserInterfaceManager _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GetVerbsEvent<Verb>>(OnGetVerbs);
    }

    private void OnGetVerbs(GetVerbsEvent<Verb> args)
    {
        if (args.Target == args.User
            || !HasComp<ActorComponent>(args.Target)
            || !TryGetDistance(args.User, args.Target, out var distance)
            || distance > InteractionRange)
        {
            return;
        }

        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("directed-emotes-menu-verb"),
            Category = VerbCategory.Interact,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/Actions/eyeopen.png")),
            ClientExclusive = true,
            Act = () => _ui.GetUIController<DirectedEmotesUIController>().OpenDirectedEmotesMenu(args.Target)
        });
    }

    private bool TryGetDistance(EntityUid user, EntityUid target, out float distance)
    {
        distance = default;

        return TryComp(user, out TransformComponent? userXform)
            && TryComp(target, out TransformComponent? targetXform)
            && targetXform.Coordinates.TryDistance(EntityManager, userXform.Coordinates, out distance);
    }
}
