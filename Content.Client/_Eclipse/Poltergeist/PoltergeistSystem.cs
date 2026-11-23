using Content.Shared._Eclipse.Poltergeist;
using Robust.Client.GameObjects;
using Robust.Client.Input;
using Robust.Client.Player;

namespace Content.Client._Eclipse.Poltergeist;

/// <summary>Ordinary items have no input context; possession temporarily gives them the spirit's controls.</summary>
public sealed class PoltergeistSystem : EntitySystem
{
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    private readonly Dictionary<EntityUid, string?> _contexts = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PoltergeistPossessedComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PoltergeistPossessedComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<PoltergeistPossessedComponent> ent, ref ComponentStartup args)
    {
        _contexts[ent] = TryComp<InputComponent>(ent, out var input) ? input.ContextName : null;
        EnsureComp<InputComponent>(ent).ContextName = "ghost";
        if (_player.LocalEntity == ent.Owner)
            _input.Contexts.SetActiveContext("ghost");
    }

    private void OnShutdown(Entity<PoltergeistPossessedComponent> ent, ref ComponentShutdown args)
    {
        if (!_contexts.Remove(ent, out var previous))
            return;
        if (previous == null)
            RemComp<InputComponent>(ent);
        else if (TryComp<InputComponent>(ent, out var input))
            input.ContextName = previous;
        if (_player.LocalEntity == ent.Owner)
            _input.Contexts.SetActiveContext(previous ?? "common");
    }
}
