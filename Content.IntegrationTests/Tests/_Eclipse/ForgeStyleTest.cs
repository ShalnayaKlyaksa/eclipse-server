using Content.Client.Construction.UI;
using Content.Client.Options.UI;
using Content.Client.PDA;
using System.Numerics;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.IntegrationTests.Fixtures;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Eclipse;

[TestFixture]
public sealed class ForgeStyleTest : GameTest
{
    [Test]
    public async Task PdaButtonStatesKeepTheSameContentBounds()
    {
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var styles = Client.ResolveDependency<IStylesheetManager>();
            using var root = new Control();
            ui.RootControl.AddChild(root);

            foreach (var station in new[] { styles.SheetNanotrasen, styles.SheetSystem })
            {
                root.Stylesheet = PdaTheme.Create(station);
                foreach (var variant in new[] { "", "pda-copy", "pda-primary" })
                {
                    using var button = new TestButton();
                    button.AddStyleClass(ContainerButton.StyleClassButton);
                    if (variant.Length > 0)
                        button.AddStyleClass(variant);
                    root.AddChild(button);
                    button.ForceRunStyleUpdate();

                    var bounds = UIBox2.FromDimensions(Vector2.Zero, new Vector2(120, 28));
                    Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var normal), Is.True);
                    var content = normal.GetContentBox(bounds, 1);
                    foreach (var state in new[] { ContainerButton.StylePseudoClassHover, ContainerButton.StylePseudoClassPressed, ContainerButton.StylePseudoClassDisabled })
                    {
                        button.SetState(state);
                        button.ForceRunStyleUpdate();
                        Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var style), Is.True);
                        Assert.That(style.GetContentBox(bounds, 1), Is.EqualTo(content), $"{variant} {state}");
                    }
                }
            }
        });
    }

    private sealed class TestButton : ContainerButton
    {
        public void SetState(string state) => SetOnlyStylePseudoClass(state);
    }

    [Test]
    public async Task DefaultThemeAndWindowStylesReachChatAndCustomControls()
    {
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var styles = Client.ResolveDependency<IStylesheetManager>();
            var config = Client.ResolveDependency<IConfigurationManager>();
            Assert.That(ui.DefaultTheme.ID, Is.EqualTo("SS14ForgeTheme"));
            Assert.That(config.GetCVar(CVars.InterfaceTheme), Is.EqualTo("SS14ForgeTheme"));

            using var root = new Control();
            ui.RootControl.AddChild(root);
            foreach (var stylesheet in new[] { styles.SheetNanotrasen, styles.SheetSystem })
            {
                root.Stylesheet = stylesheet;
                using var button = new Button { ToggleMode = true, Text = "Forge" };
                button.AddStyleClass(StyleClass.ButtonSmall);
                root.AddChild(button);
                root.ForceRunStyleUpdate();
                Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var normal), Is.True);
                Assert.That(normal, Is.TypeOf<StyleBoxFlat>(), "Wide window buttons must not stretch slot textures.");
                Assert.That(((StyleBoxFlat) normal).BorderColor, Is.EqualTo(ForgeStyle.Border));
                button.Pressed = true;
                button.ForceRunStyleUpdate();
                Assert.That(button.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var pressed), Is.True);
                Assert.That(pressed.MinimumSize, Is.EqualTo(normal.MinimumSize), "Toggling must not resize compact buttons.");
                Assert.That(button.ActualModulateSelf, Is.EqualTo(Color.White), "Selected controls must not acquire a green tint.");
            }

            root.Stylesheet = styles.SheetNanotrasen;
            using var escape = new EscapeMenu();
            root.AddChild(escape);
            root.ForceRunStyleUpdate();
            escape.Measure(new Vector2(800, 600));
            escape.Arrange(UIBox2.FromDimensions(Vector2.Zero, escape.DesiredSize));
            var title = escape.FindControl<Label>("TitleLabel");
            using var fullTitle = new Label { Text = title.Text };
            fullTitle.AddStyleClass("windowTitle");
            root.AddChild(fullTitle);
            fullTitle.ForceRunStyleUpdate();
            fullTitle.Measure(new Vector2(800, 600));
            Assert.That(title.Width, Is.GreaterThanOrEqualTo(fullTitle.DesiredSize.X), "The escape menu title must not overlap the close button.");

            using var window = new ConstructionMenu();
            root.AddChild(window);
            root.ForceRunStyleUpdate();
            var header = window.FindControl<PanelContainer>("WindowHeader");
            Assert.That(header.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var headerBox), Is.True);
            Assert.That(headerBox, Is.TypeOf<StyleBoxFlat>());
            Assert.That(((StyleBoxFlat) headerBox).BackgroundColor, Is.EqualTo(ForgeStyle.Surface));
            Assert.That(window.FindControl<ContainerButton>("ConstructionTabButton").ActualModulateSelf, Is.EqualTo(Color.White));

            using var chat = new ChatBox();
            root.AddChild(chat);
            root.ForceRunStyleUpdate();
            Assert.That(chat.ChatWindowPanel.PanelOverride, Is.Null, "Chat must use the station stylesheet.");
            Assert.That(chat.ChatWindowPanel.TryGetStyleProperty<StyleBox>(PanelContainer.StylePropertyPanel, out var chatBox), Is.True);
            Assert.That(chatBox, Is.TypeOf<StyleBoxFlat>());
            Assert.That(((StyleBoxFlat) chatBox).BackgroundColor, Is.EqualTo(ForgeStyle.Background));
            Assert.That(((StyleBoxFlat) chatBox).BorderColor, Is.EqualTo(ForgeStyle.Border));
            Assert.That(chat.ChatInput.Input.StyleBoxOverride, Is.Null);
            Assert.That(chat.ChatInput.ChannelSelector.StyleBoxOverride, Is.Null);
            Assert.That(chat.ChatInput.FilterButton.StyleBoxOverride, Is.Null);
            Assert.That(chat.ChatInput.ChannelSelector.TryGetStyleProperty<StyleBox>(ContainerButton.StylePropertyStyleBox, out var channelBox), Is.True);
            Assert.That(channelBox, Is.TypeOf<StyleBoxFlat>(), "Chat controls must not stretch HUD slot artwork.");
        });
    }
}
