using System.Diagnostics.CodeAnalysis;
using Content.Client.Guidebook.RichText;
using Content.Client.Guidebook.Richtext;
using Content.Client.Lobby.UI.ProfileEditorControls;
using Content.Client.UserInterface.ControlExtensions;
using Content.Shared.Guidebook;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client.Guidebook.Controls;

[UsedImplicitly]
public sealed class GuideSpeciesCard : ContainerButton, IDocumentTag
{
    private ProfilePreviewSpriteView _portrait = default!;
    private HumanoidCharacterProfile _profile = default!;

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        if (!args.TryGetValue("Species", out var id) || !args.TryGetValue("Caption", out var caption)
            || !prototypes.TryIndex<SpeciesPrototype>(id, out var species)
            || !prototypes.HasIndex<GuideEntryPrototype>(id))
        {
            control = null;
            return false;
        }

        _profile = HumanoidCharacterProfile.DefaultWithSpecies(species.ID);
        if (species.AppearancePresets.Count > 0)
            _profile = _profile.WithCharacterAppearance(_profile.Appearance.WithColorPreset(species.AppearancePresets[0]));
        _portrait = new ProfilePreviewSpriteView
        {
            Scale = new(1.8f),
            MinSize = new(56, 56),
            MouseFilter = MouseFilterMode.Ignore,
        };

        var title = Loc.GetString(species.Name);
        var accent = args.TryGetValue("Accent", out var color) ? Color.FromHex(color) : GuidebookWindow.Accent;
        var portraitFrame = new PanelContainer
        {
            PanelOverride = GuidebookWindow.Panel("#171717", accent, 3),
            VerticalAlignment = VAlignment.Center,
            MouseFilter = MouseFilterMode.Ignore,
        };
        portraitFrame.AddChild(_portrait);

        var details = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalAlignment = VAlignment.Center,
            SeparationOverride = 4,
        };
        details.AddChild(new Label { Text = title, FontColorOverride = accent, StyleClasses = { "LabelHeading" } });
        details.AddChild(GuidebookWindow.Paragraph(caption));

        var row = new BoxContainer { SeparationOverride = 10 };
        row.AddChild(portraitFrame);
        row.AddChild(details);
        row.AddChild(new Label { Text = "›", FontColorOverride = accent, VerticalAlignment = VAlignment.Center });
        AddChild(row);

        StyleClasses.Add("GuidebookResult");
        Margin = new Thickness(0, 0, 8, 8);
        MinHeight = 88;
        HorizontalExpand = true;
        DefaultCursorShape = CursorShape.Hand;
        ToolTip = title;
        OnPressed += _ =>
        {
            if (this.TryGetParentHandler<ILinkClickHandler>(out var handler))
                handler.HandleClick(id);
        };

        control = this;
        return true;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _portrait.LoadPreview(_profile, showClothes: false);
    }
}
