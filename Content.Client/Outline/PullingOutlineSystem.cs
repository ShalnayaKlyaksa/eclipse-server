using Content.Client.Interactable.Components;
using Content.Shared.CCVar;
using Content.Shared.Movement.Pulling.Systems;
using Content.Shared.Stealth.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;

namespace Content.Client.Outline;

public sealed class PullingOutlineSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> Outline = "SelectionOutlineInrange";

    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly PullingSystem _pulling = default!;

    private ShaderInstance _shader = default!;
    private SpriteComponent? _highlighted;
    private ShaderInstance? _hoverShader;

    public override void Initialize()
    {
        _shader = _prototypes.Index(Outline).InstanceUnique();
        _shader.SetParameter("outline_color", new Color(0.35f, 0.78f, 1f, 0.6f));
        _shader.SetParameter("outline_width", 1f);
    }

    public override void Shutdown()
    {
        Clear();
        _shader.Dispose();
    }

    public override void FrameUpdate(float frameTime)
    {
        if (!_config.GetCVar(CCVars.OutlineEnabled) || _players.LocalEntity is not { } player
            || _pulling.GetPulling(player) is not { } target
            || !TryComp<SpriteComponent>(target, out var sprite) || !sprite.Visible || sprite.Color.A <= 0f
            || TryComp<StealthComponent>(target, out var stealth) && stealth.Enabled)
        {
            Clear();
            return;
        }

        if (_highlighted != sprite)
            Clear();
        if (sprite.PostShader == _shader)
            return;

        if (sprite.PostShader is { } current
            && (!TryComp<InteractionOutlineComponent>(target, out var hover) || !hover.OwnsShader(current)))
            return;

        _highlighted = sprite;
        _hoverShader = sprite.PostShader;
        sprite.PostShader = _shader;
    }

    private void Clear()
    {
        if (_highlighted is { Deleted: false } sprite && sprite.PostShader == _shader)
        {
            var hoverStillActive = _hoverShader is { } previous
                && TryComp<InteractionOutlineComponent>(sprite.Owner, out var hover) && hover.OwnsShader(previous);
            sprite.PostShader = hoverStillActive ? _hoverShader : null;
        }

        _highlighted = null;
        _hoverShader = null;
    }
}
