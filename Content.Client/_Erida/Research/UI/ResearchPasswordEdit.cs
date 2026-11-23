using System.Numerics;
using System.Linq;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Erida.Research.UI;

/// <summary>Keep the editable value local; render a mask instead of the underlying text.</summary>
public sealed class ResearchPasswordEdit : LineEdit
{
    private readonly Label _mask;
    public ResearchPasswordEdit()
    {
        AddStyleClass("terminal-password");
        _mask = new Label { MouseFilter = MouseFilterMode.Ignore, VerticalAlignment = VAlignment.Center };
        AddChild(_mask);
        IsValid = value => value.Length <= 32;
        OnTextChanged += args => _mask.Text = new string('•', args.Text.Length);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var desired = base.MeasureOverride(availableSize);
        _mask.Measure(availableSize);
        return desired;
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        base.ArrangeOverride(finalSize);
        _mask.Arrange(new UIBox2(8, 0, MathF.Max(8, finalSize.X - 8), finalSize.Y));
        return finalSize;
    }
}
