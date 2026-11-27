using System.Linq;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client.UserInterface.Controls;

public sealed class ClippedOptionButton : OptionButton
{
    public ClippedOptionButton()
    {
        // OptionButton keeps its caption label inside its own layout container.
        var caption = Children.SelectMany(child => child.Children).OfType<Label>().FirstOrDefault();
        if (caption != null)
            caption.ClipText = true;

        OptionsScroll.HScrollEnabled = false;
    }

    public override void ButtonOverride(Button button)
    {
        button.ClipText = true;
        button.TooltipSupplier = static sender => new Tooltip { Text = ((Button) sender).Text };
    }
}
