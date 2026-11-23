using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids;
using Content.Shared.Forensics.Systems;
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared.Medical;

public sealed class VomitSystem : EntitySystem
{
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly HungerSystem _hunger = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly MovementModStatusSystem _movementMod = default!;
    [Dependency] private readonly ThirstSystem _thirst = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedForensicsSystem _forensics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedPuddleSystem _puddle = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainer = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StomachComponent, BodyRelayedEvent<TryVomitEvent>>(TryVomitSolution);
    }

    private static readonly ProtoId<SoundCollectionPrototype> VomitCollection = "Vomit";

    private readonly SoundSpecifier _vomitSound = new SoundCollectionSpecifier(VomitCollection,
        AudioParams.Default.WithVariation(0.2f).WithVolume(-4f));

    private void TryVomitSolution(Entity<StomachComponent> ent, ref BodyRelayedEvent<TryVomitEvent> args)
    {
        if (!_solutionContainer.ResolveSolution(ent.Owner,
                StomachSystem.DefaultSolutionName,
                ref ent.Comp.Solution,
                out var sol))
            return;

        // Empty stomach solution into the new vomit solution
        args.Args.Sol.AddSolution(sol, _proto);
        sol.RemoveAllSolution();

        // Remind the stomach that it's empty.
        _solutionContainer.UpdateChemicals(ent.Comp.Solution.Value);
        args.Args = args.Args with { Handled = true };
    }

    /// <summary>
    /// Make an entity vomit, if they have a stomach.
    /// </summary>
    /// <returns>Whether any fluid was expelled. An empty stomach only produces dry heaves.</returns>
    public bool Vomit(EntityUid uid, float thirstAdded = -40f, float hungerAdded = -40f, bool force = false)
    {
        // Vomit only if entity is alive
        // Ignore condition if force was set to true
        if (!force && _mobState.IsDead(uid))
            return false;

        // TODO: Need decals
        var solution = new Solution();

        var ev = new TryVomitEvent(solution, force);
        RaiseLocalEvent(uid, ref ev);

        if (!ev.Handled)
            return false;

        if (solution.Volume <= 0)
        {
            _movementMod.TryUpdateMovementSpeedModDuration(uid, MovementModStatusSystem.VomitingSlowdown, TimeSpan.FromSeconds(1), 0.5f);
            PlayVomitEffects(uid, dry: true);
            return false;
        }

        // Resource loss scales with the actual expelled contents, including small meals.
        var expelledVolume = (float) solution.Volume;
        var lossScale = Math.Clamp(expelledVolume / Math.Max(1, MathF.Abs(thirstAdded) + MathF.Abs(hungerAdded)), 0, 1);
        if (TryComp<HungerComponent>(uid, out var hunger))
            _hunger.ModifyHunger(uid, hungerAdded * lossScale, hunger);

        if (TryComp<ThirstComponent>(uid, out var thirst))
            _thirst.ModifyThirst(uid, thirst, thirstAdded * lossScale);

        var solutionSize = Math.Clamp(expelledVolume / 6, 1, 10);

        // Apply a bit of slowdown
        _movementMod.TryUpdateMovementSpeedModDuration(uid, MovementModStatusSystem.VomitingSlowdown, TimeSpan.FromSeconds(solutionSize), 0.5f);

        if (_puddle.TrySpillAt(uid, solution, out var puddle, false))
        {
            _forensics.TransferDna(puddle, uid, false);
        }
        PlayVomitEffects(uid, dry: false);
        return true;
    }

    private void PlayVomitEffects(EntityUid uid, bool dry)
    {
        if (!_netManager.IsServer)
            return;

        _audio.PlayPvs(_vomitSound, uid);
        _popup.PopupEntity(Loc.GetString(dry ? "disease-dry-heave" : "disease-vomit", ("person", Identity.Entity(uid, EntityManager))), uid);
    }
}

[ByRefEvent]
public record struct TryVomitEvent(Solution Sol, bool Forced = false, bool Handled = false);
