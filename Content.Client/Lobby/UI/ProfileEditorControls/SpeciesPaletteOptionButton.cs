using System.Linq;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Lobby.UI.ProfileEditorControls;

public sealed class SpeciesPaletteOptionButton : OptionButton
{
    public SpeciesPaletteOptionButton()
    {
        var caption = Children.SelectMany(child => child.Children).OfType<Label>().FirstOrDefault();
        if (caption != null)
            caption.ClipText = true;

        OptionsScroll.HScrollEnabled = false;
    }

    public override void ButtonOverride(Button button)
    {
        button.ClipText = true;
        button.ToolTip = button.Text;
    }
}
