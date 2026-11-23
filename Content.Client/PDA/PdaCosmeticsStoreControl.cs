using System.Linq;
using System.Numerics;
using Content.Shared.PDA;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.PDA;

/// <summary>The account cosmetics shop is hosted exclusively by the lobby.</summary>
public sealed class PdaCosmeticsStoreControl : BoxContainer
{
    private PdaCosmeticsClientSystem _store = IoCManager.Resolve<IEntityManager>().System<PdaCosmeticsClientSystem>();

    public PdaCosmeticsStoreControl()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;
        SeparationOverride = 14;
        Stylesheet = PdaTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        Build();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _store = IoCManager.Resolve<IEntityManager>().System<PdaCosmeticsClientSystem>();
        _store.Changed += Build;
        _store.Request();
    }
    protected override void ExitedTree() { _store.Changed -= Build; base.ExitedTree(); }

    private void Build()
    {
        RemoveAllChildren();
        AddChild(new Label { Text = Loc.GetString("pda-os-lobby-store-title"), StyleClasses = { "pda-title" } });
        var description = new RichTextLabel { HorizontalExpand = true };
        description.SetMessage(Loc.GetString("pda-os-lobby-store-description"), PdaTheme.TextColor);
        AddChild(description);
        AddChild(new Label { Text = _store.Ready ? Loc.GetString("pda-os-wallet", ("merits", _store.Merits), ("shards", _store.Shards)) : Loc.GetString("pda-os-loading"), StyleClasses = { "pda-caption" } });
        if (_store.Feedback != null) AddChild(new Label { Text = Loc.GetString(_store.Feedback) });
        var grid = new GridContainer { Columns = 2, HSeparationOverride = 14, VSeparationOverride = 14, HorizontalExpand = true };
        foreach (var item in PdaCosmetics.Catalog)
        {
            var owned = _store.Profile.Owned.Contains(item.Id);
            var column = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 9, HorizontalExpand = true };
            column.AddChild(new PdaWallpaperPreview(item.Id) { MinHeight = 110, HorizontalExpand = true });
            column.AddChild(new Label { Text = Loc.GetString("pda-os-wallpaper-" + item.Id), StyleClasses = { "pda-caption" } });
            if (item.Id == "studio")
            {
                var info = new RichTextLabel { HorizontalExpand = true };
                info.SetMessage(Loc.GetString("pda-os-studio-description"), PdaTheme.TextColor);
                column.AddChild(info);
            }
            if (owned || item.Merits == 0)
                column.AddChild(new Label { Text = Loc.GetString("pda-os-owned"), StyleClasses = { "terminal-muted" } });
            else
            {
                var buttons = new BoxContainer { SeparationOverride = 6 };
                var merits = new Button { Text = Loc.GetString("pda-os-buy-merits", ("amount", item.Merits)), HorizontalExpand = true, Disabled = !_store.Ready || _store.Pending || _store.Merits < item.Merits };
                var shards = new Button { Text = Loc.GetString("pda-os-buy-shards", ("amount", item.Shards)), HorizontalExpand = true, Disabled = !_store.Ready || _store.Pending || _store.Shards < item.Shards };
                merits.OnPressed += _ => _store.Buy(item.Id, false);
                shards.OnPressed += _ => _store.Buy(item.Id, true);
                buttons.AddChild(merits); buttons.AddChild(shards); column.AddChild(buttons);
            }
            grid.AddChild(new PanelContainer { MinWidth = 260, HorizontalExpand = true, PanelOverride = PdaTheme.Box("#142331", "#334657", 14), Children = { column } });
        }
        AddChild(new ScrollContainer { HScrollEnabled = false, HorizontalExpand = true, VerticalExpand = true, Children = { grid } });
    }
}
