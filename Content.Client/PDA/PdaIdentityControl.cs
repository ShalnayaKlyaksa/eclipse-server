using Content.Client.MainMenu.UI;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA;

public sealed class PdaIdentityControl : BoxContainer
{
    private readonly PdaCosmeticsClientSystem _identity = IoCManager.Resolve<IEntityManager>().System<PdaCosmeticsClientSystem>();
    private readonly LineEdit _id = new() { HorizontalExpand = true };
    private readonly RichTextLabel _feedback = new() { Visible = false, MaxWidth = 390, HorizontalAlignment = HAlignment.Left };
    private readonly Button _save = new() { StyleIdentifier = MainMenuControl.StyleIdentifierCompactButton };
    private int? _slot;
    private string _savedId = string.Empty;
    private bool _canEdit;
    private bool _pending;
    private string DraftId => _id.Text.Trim().ToLowerInvariant();

    public PdaIdentityControl()
    {
        Orientation = LayoutOrientation.Vertical;
        SeparationOverride = 6;
        Visible = false;
        _id.PlaceHolder = Loc.GetString("pda-services-id-placeholder");
        _save.Text = Loc.GetString("pda-services-id-save");
        _save.OnPressed += _ => Save();
        _id.OnTextChanged += _ => UpdateInputs();
        _id.OnTextEntered += _ => Save();

        var hint = new RichTextLabel { MaxWidth = 390, HorizontalAlignment = HAlignment.Left };
        hint.SetMessage(Loc.GetString("pda-services-id-hint"));
        AddChild(new Label
        {
            Text = Loc.GetString("pda-services-character-id"),
            StyleIdentifier = MainMenuControl.StyleIdentifierCompactText,
        });
        AddChild(new BoxContainer { SeparationOverride = 8, Children = { _id, _save } });
        AddChild(hint);
        AddChild(_feedback);
        UpdateInputs();
    }

    public void SetCharacter(int? slot)
    {
        if (_slot == slot)
            return;

        _slot = slot;
        Visible = slot.HasValue;
        if (IsInsideTree)
            RequestId();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _identity.PublicIdChanged += OnState;
        RequestId();
    }

    protected override void ExitedTree()
    {
        _identity.PublicIdChanged -= OnState;
        base.ExitedTree();
    }

    private void RequestId()
    {
        _canEdit = false;
        _pending = false;
        _savedId = string.Empty;
        _id.Text = string.Empty;
        _feedback.Visible = false;
        UpdateInputs();
        if (_slot is { } slot)
            _identity.RequestPublicId(slot);
    }

    private void OnState(PdaPublicIdStateEvent state)
    {
        if (state.Slot != _slot || (_pending && state.CanEdit && state.Feedback == null))
            return;

        _canEdit = state.CanEdit;
        _savedId = state.Id;
        if (!_pending || state.Feedback == "pda-services-id-saved")
            _id.Text = state.Id;
        _pending = false;

        var feedback = state.CanEdit ? state.Feedback : "pda-services-id-unavailable";
        _feedback.Visible = feedback != null;
        _feedback.SetMessage(feedback != null ? Loc.GetString(feedback) : string.Empty);
        UpdateInputs();
    }

    private void Save()
    {
        if (_save.Disabled || _slot is not { } slot)
            return;

        _pending = true;
        _feedback.Visible = false;
        UpdateInputs();
        _identity.SetPublicId(slot, DraftId);
    }

    private void UpdateInputs()
    {
        _id.Editable = _canEdit && !_pending;
        var id = DraftId;
        _save.Disabled = !_id.Editable || id == _savedId || !PdaPublicId.Valid(id);
    }
}
