using Robust.Client.UserInterface;
namespace Content.Client._Erida.Research.UI;
public sealed partial class FancyResearchConsoleMenu
{
    private void ApplyTerminalStyle()
    {
        var theme = ResearchTerminalTheme.Create(IoCManager.Resolve<IUserInterfaceManager>().Stylesheet);
        ((Control)this).Stylesheet = theme;
        ResearchTerminalTheme.StyleButtons(ContentsContainer);
        SectionFilter.OptionStyleClasses.Add("terminal-control");
        AvailabilityFilter.OptionStyleClasses.Add("terminal-control");
        if (AvailabilityFilter.OptionsScroll.Parent is { } statePopup) statePopup.Stylesheet = theme;
        if (SectionFilter.OptionsScroll.Parent is { } popup) popup.Stylesheet = theme;
    }
    private static void ApplyTerminalButtons(Control control) => ResearchTerminalTheme.StyleButtons(control);
}
