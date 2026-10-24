using Content.Client.Audio;
using Content.Client.UserInterface.Systems.DamageOverlays;
using Content.Shared._Eclipse.AdvancedHealth;
using Content.Shared.Mobs.Systems;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Eclipse.AdvancedHealth;

/// <summary>
/// Client-side distress feedback: muffled audio, ear ringing and damage overlays.
/// Heartbeat lives in the status window (audible only there); all effects stop once the mob is dead.
/// </summary>
public sealed class AdvancedHealthEffectsSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private EntityUid? _ringingStream;
    private float _currentMuffle = 1f;
    private bool _wasDead;

    private static readonly SoundPathSpecifier RingingSound = new("/Audio/Ambience/Objects/emf_buzz.ogg");

    private const float AudioLerpSpeed = 4f;
    private const float RingingMinIntensity = 0.12f;

    public override void Initialize()
    {
        UpdatesOutsidePrediction = true;
        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnAttach);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnDetach);
    }

    public override void Update(float frameTime)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var player = _player.LocalEntity;
        if (player is not { } uid || !TryComp<AdvancedHealthComponent>(uid, out var health))
        {
            ResetEffects(frameTime);
            _wasDead = false;
            return;
        }

        // Once actually dead, drop all living-body feedback and show a steady dead overlay (no pulsing).
        if (_mobState.IsDead(uid))
        {
            ResetEffects(frameTime);
            if (!_wasDead)
            {
                _ui.GetUIController<DamageOverlayUiController>().SetSteadyDeadOverlay();
                _wasDead = true;
            }
            return;
        }
        _wasDead = false;

        _ui.GetUIController<DamageOverlayUiController>().UpdateFromAdvancedHealth(health);
        UpdateRinging(uid, health, frameTime);
        UpdateMuffle(health, frameTime);
    }

    private void OnAttach(LocalPlayerAttachedEvent ev)
    {
        _currentMuffle = 1f;
        _wasDead = false;
    }

    private void OnDetach(LocalPlayerDetachedEvent ev)
    {
        StopStream(ref _ringingStream);
        _currentMuffle = 1f;
        ApplyMasterGain();
    }

    private void UpdateRinging(EntityUid player, AdvancedHealthComponent health, float frameTime)
    {
        var intensity = AdvancedHealthDistress.ComputeRingingIntensity(health);
        if (intensity < RingingMinIntensity)
        {
            StopStream(ref _ringingStream);
            return;
        }

        EnsureLoopingStream(ref _ringingStream, player, RingingSound, intensity, 1.8f, 0.5f, -24f, 16f);
        ApplyStreamVolume(_ringingStream, intensity, -24f, 16f);
    }

    private void EnsureLoopingStream(
        ref EntityUid? stream,
        EntityUid player,
        SoundPathSpecifier sound,
        float intensity,
        float basePitch,
        float pitchRange,
        float baseVolume,
        float volumeRange)
    {
        if (stream is { } existing && Exists(existing))
            return;

        var pitch = basePitch + intensity * pitchRange;
        var volume = baseVolume + intensity * volumeRange;
        var played = _audio.PlayGlobal(
            sound,
            player,
            AudioParams.Default.WithLoop(true).WithVolume(volume).WithPitchScale(pitch));

        stream = played?.Entity;
    }

    private void ApplyStreamVolume(EntityUid? stream, float intensity, float baseVolume, float volumeRange)
    {
        if (stream is not { } entity || !TryComp(entity, out AudioComponent? audio))
            return;

        var targetVolume = baseVolume + intensity * volumeRange;
        _audio.SetVolume(entity, targetVolume, audio);
    }

    private void UpdateMuffle(AdvancedHealthComponent health, float frameTime)
    {
        var target = AdvancedHealthDistress.ComputeMuffleFactor(health);
        _currentMuffle = MathHelper.Lerp(_currentMuffle, target, frameTime * AudioLerpSpeed);
        ApplyMasterGain();
    }

    private void ResetEffects(float frameTime)
    {
        StopStream(ref _ringingStream);
        _currentMuffle = MathHelper.Lerp(_currentMuffle, 1f, frameTime * AudioLerpSpeed);
        ApplyMasterGain();
    }

    private void ApplyMasterGain()
    {
        var baseGain = _cfg.GetCVar(CVars.AudioMasterVolume) * ContentAudioSystem.MasterVolumeMultiplier;
        _audioManager.SetMasterGain(baseGain * _currentMuffle);
    }

    private void StopStream(ref EntityUid? stream)
    {
        if (stream is not { } entity)
            return;

        _audio.Stop(entity);
        stream = null;
    }
}
