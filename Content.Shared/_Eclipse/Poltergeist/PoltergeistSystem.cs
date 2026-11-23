using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Hands;
using Content.Shared.Item;
using Content.Shared.Eye;
using Content.Shared.Movement.Events;
using Content.Shared.Speech;

namespace Content.Shared._Eclipse.Poltergeist;

/// <summary>Possession grants supernatural actions, not the object's normal inventory or machine UI.</summary>
public sealed class SharedPoltergeistSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<PoltergeistPossessedComponent, InteractionAttemptEvent>(OnInteract);
        SubscribeLocalEvent<PoltergeistPossessedComponent, UseAttemptEvent>(OnUse);
        SubscribeLocalEvent<PoltergeistPossessedComponent, PickupAttemptEvent>(OnPickup);
        SubscribeLocalEvent<PoltergeistPossessedComponent, UpdateCanMoveEvent>(OnMove);
        SubscribeLocalEvent<PoltergeistPossessedComponent, ChangeDirectionAttemptEvent>(OnChangeDirection);
        SubscribeLocalEvent<PoltergeistComponent, UpdateCanMoveEvent>(OnSpiritMove);
        SubscribeLocalEvent<PoltergeistComponent, SpeakAttemptEvent>(OnSpeak);
        SubscribeLocalEvent<PoltergeistPossessedComponent, GetVisMaskEvent>(OnGetVisMask);
    }

    private void OnInteract(Entity<PoltergeistPossessedComponent> ent, ref InteractionAttemptEvent args) => args.Cancelled = true;
    private void OnUse(Entity<PoltergeistPossessedComponent> ent, ref UseAttemptEvent args) => args.Cancel();
    private void OnPickup(Entity<PoltergeistPossessedComponent> ent, ref PickupAttemptEvent args) => args.Cancel();
    private void OnMove(Entity<PoltergeistPossessedComponent> ent, ref UpdateCanMoveEvent args) => args.Cancel();
    private void OnChangeDirection(Entity<PoltergeistPossessedComponent> ent, ref ChangeDirectionAttemptEvent args) => args.Cancel();
    private void OnSpiritMove(Entity<PoltergeistComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.Host != null)
            args.Cancel();
    }
    private void OnSpeak(Entity<PoltergeistComponent> ent, ref SpeakAttemptEvent args) => args.Cancel();

    private void OnGetVisMask(Entity<PoltergeistPossessedComponent> ent, ref GetVisMaskEvent args)
    {
        // The action provider is the spirit, which stays on the ghost visibility layer.
        args.VisibilityMask |= (int)VisibilityFlags.Ghost;
    }
}
