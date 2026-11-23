using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Client.Guidebook.Richtext;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;
using static Content.Client.Guidebook.Controls.GuidebookWindow;

namespace Content.Client.Guidebook.Controls;

/// <summary>Small interactive explanations embedded directly in a guide article.</summary>
public sealed class GuideMechanismExample : BoxContainer, IDocumentTag
{
    private readonly GuidebookExampleState _state = new();

    public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
    {
        control = null;
        if (!args.TryGetValue("Kind", out var kind) || kind is not ("Crushing" or "Transport" or "Heat")) return false;
        HorizontalExpand = true;
        Orientation = LayoutOrientation.Vertical;
        var content = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 12,
            Margin = new Thickness(10) };
        var panel = new PanelContainer { PanelOverride = Panel("#17130F", Accent.WithAlpha(0.5f), 12), HorizontalExpand = true };
        panel.AddChild(content);
        var body = new CollapsibleBody { HorizontalExpand = true };
        body.AddChild(panel);
        var title = Loc.GetString("guidebook-example-" + kind.ToLowerInvariant());
        var heading = new CollapsibleHeading("[+] " + title) { ChevronVisible = false, StyleClasses = { "GuidebookFoldout" } };
        body.OnVisibilityChanged += _ => heading.Title = (body.Visible ? "[-] " : "[+] ") + title;
        AddChild(new Collapsible(heading, body) { HorizontalExpand = true });
        switch (kind)
        {
            case "Crushing": BuildCrusher(content); break;
            case "Transport": BuildTransport(content); break;
            case "Heat": BuildHeat(content); break;
        }
        control = this;
        return true;
    }

    private static TextureRect Image(string machine) => new()
    {
        Texture = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>(
            $"/Textures/_Eclipse/Structures/Machines/industrial_{machine}.rsi").RSI["icon"].Frame0,
        SetSize = new Vector2(64), Stretch = TextureRect.StretchMode.KeepAspectCentered,
        VerticalAlignment = VAlignment.Center,
    };
    private static BoxContainer Column() => new() { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true, SeparationOverride = 8 };
    private static void Message(RichTextLabel label, string key, params (string, object)[] args) =>
        label.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString(key, args)), Ink);

    private void BuildCrusher(BoxContainer body)
    {
        body.AddChild(Paragraph(Loc.GetString("guidebook-crusher-intro"), Accent));
        var row = new BoxContainer { SeparationOverride = 16 };
        var input = Column();
        var output = Column();
        var inputCount = new Label();
        var outputCount = new Label();
        input.AddChild(Paragraph(Loc.GetString("guidebook-example-input"), Muted));
        input.AddChild(inputCount);
        output.AddChild(Paragraph(Loc.GetString("guidebook-example-output"), Muted));
        output.AddChild(outputCount);
        row.AddChild(input); row.AddChild(Image("crusher")); row.AddChild(output); body.AddChild(row);
        var conditions = new OptionButton { HorizontalExpand = true };
        conditions.AddItem(Loc.GetString("guidebook-example-ready"), 0);
        conditions.AddItem(Loc.GetString("guidebook-example-output-full"), 1);
        conditions.AddItem(Loc.GetString("guidebook-example-no-power"), 2);
        conditions.SelectId(0);
        var status = Paragraph(Loc.GetString("guidebook-crusher-idle"));
        void Refresh()
        {
            inputCount.Text = Loc.GetString("guidebook-crusher-input", ("count", _state.Input));
            outputCount.Text = Loc.GetString("guidebook-crusher-output", ("count", _state.Output));
        }
        var buttons = new BoxContainer { SeparationOverride = 8 };
        buttons.AddChild(MakeButton(Loc.GetString("guidebook-example-run-cycle"), () =>
        {
            var result = _state.Crush(conditions.SelectedId != 2, conditions.SelectedId == 1);
            var key = result switch
            {
                GuidebookExampleState.CrushResult.Success => "guidebook-crusher-success",
                GuidebookExampleState.CrushResult.NoPower => "guidebook-crusher-no-power",
                GuidebookExampleState.CrushResult.OutputFull => "guidebook-crusher-output-full",
                _ => "guidebook-crusher-no-input",
            };
            Refresh(); Message(status, key);
        }));
        buttons.AddChild(MakeButton(Loc.GetString("guidebook-example-reset"), () =>
        {
            _state.Reset(); conditions.SelectId(0); Refresh(); Message(status, "guidebook-crusher-idle");
        }));
        body.AddChild(conditions); body.AddChild(buttons); body.AddChild(status);
        body.AddChild(Paragraph(Loc.GetString("guidebook-crusher-simplified"), Muted));
        Refresh();
    }

    private static void BuildTransport(BoxContainer body)
    {
        body.AddChild(Paragraph(Loc.GetString("guidebook-transport-intro"), Accent));
        var row = new BoxContainer { SeparationOverride = 14 };
        var source = Column(); var target = Column();
        source.AddChild(Paragraph(Loc.GetString("guidebook-example-source"), Muted));
        source.AddChild(Image("crusher"));
        target.AddChild(Paragraph(Loc.GetString("guidebook-example-target"), Muted));
        target.AddChild(Image("centrifuge"));
        var sourcePort = new OptionButton { HorizontalExpand = true };
        var targetPort = new OptionButton { HorizontalExpand = true };
        foreach (var option in new[] { sourcePort, targetPort })
        {
            option.AddItem(Loc.GetString("guidebook-port-output"), 0);
            option.AddItem(Loc.GetString("guidebook-port-input"), 1);
            option.AddItem(Loc.GetString("guidebook-port-closed"), 2);
        }
        sourcePort.SelectId(0); targetPort.SelectId(2);
        source.AddChild(sourcePort); target.AddChild(targetPort);
        var sourceCount = new Label(); var targetCount = new Label();
        source.AddChild(sourceCount); target.AddChild(targetCount);
        row.AddChild(source); row.AddChild(new Label { Text = "→", VerticalAlignment = VAlignment.Center }); row.AddChild(target); body.AddChild(row);
        var status = Paragraph(Loc.GetString("guidebook-transport-idle"));
        var moved = false;
        void Refresh()
        {
            sourceCount.Text = Loc.GetString(moved ? "guidebook-example-empty" : "guidebook-example-one-item");
            targetCount.Text = Loc.GetString(moved ? "guidebook-example-one-item" : "guidebook-example-empty");
        }
        void Reset()
        {
            moved = false; Refresh(); Message(status, "guidebook-transport-idle");
        }
        sourcePort.OnItemSelected += args => { sourcePort.SelectId(args.Id); Reset(); };
        targetPort.OnItemSelected += args => { targetPort.SelectId(args.Id); Reset(); };
        var buttons = new BoxContainer { SeparationOverride = 8 };
        buttons.AddChild(MakeButton(Loc.GetString("guidebook-example-transfer"), () =>
        {
            if (moved) { Message(status, "guidebook-transport-already"); return; }
            if (!GuidebookExampleState.CanTransfer(sourcePort.SelectedId == 0, targetPort.SelectedId == 1))
            {
                Message(status, sourcePort.SelectedId != 0 ? "guidebook-transport-source-error" : "guidebook-transport-target-error");
                return;
            }
            moved = true; Refresh(); Message(status, "guidebook-transport-success");
        }));
        buttons.AddChild(MakeButton(Loc.GetString("guidebook-example-reset"), Reset));
        body.AddChild(buttons); body.AddChild(status);
        body.AddChild(Paragraph(Loc.GetString("guidebook-transport-simplified"), Muted));
        Refresh();
    }

    private static void BuildHeat(BoxContainer body)
    {
        body.AddChild(Paragraph(Loc.GetString("guidebook-heat-intro"), Accent));
        body.AddChild(Image("centrifuge"));
        var temperature = new Label();
        var status = Paragraph("");
        var slider = new Slider
        {
            MinValue = 300, MaxValue = 500, Value = 350, HorizontalExpand = true, MinHeight = 28,
            BackgroundStyleBoxOverride = new StyleBoxFlat(Color.FromHex("#48331E")),
            FillStyleBoxOverride = new StyleBoxFlat(Accent),
            GrabberStyleBoxOverride = new StyleBoxFlat(Ink) { ContentMarginLeftOverride = 8, ContentMarginTopOverride = 18 },
        };
        void Refresh()
        {
            temperature.Text = Loc.GetString("guidebook-heat-temperature", ("temperature", (int) slider.Value));
            Message(status, GuidebookExampleState.CanStartHeatCycle(slider.Value) ? "guidebook-heat-ready" : "guidebook-heat-too-cold");
        }
        slider.OnValueChanged += _ => Refresh();
        body.AddChild(temperature); body.AddChild(slider); body.AddChild(status);
        body.AddChild(Paragraph(Loc.GetString("guidebook-heat-simplified"), Muted));
        Refresh();
    }
}
