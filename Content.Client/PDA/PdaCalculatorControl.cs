using System.Globalization;
using System.Numerics;
using Content.Shared.PDA;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client.PDA;

public sealed class PdaCalculatorControl : BoxContainer
{
    private readonly LineEdit _expression = new() { Text = "0", MinHeight = 34, HorizontalExpand = true };
    private readonly Label _result = new() { Text = "0", ClipText = true, StyleClasses = { "pda-balance" } };
    private readonly Label _memoryState = new() { StyleClasses = { "terminal-muted" } };
    private readonly BoxContainer _history = new() { Orientation = LayoutOrientation.Vertical, SeparationOverride = 3 };
    private decimal _memory;
    private bool _replace = true;

    public PdaCalculatorControl()
    {
        SeparationOverride = 6; HorizontalExpand = VerticalExpand = true;
        var keypad = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 4, SetWidth = 286 };
        keypad.AddChild(_expression); keypad.AddChild(PdaAppUi.Card(_result, "#14232A", "#425962", 5));
        keypad.AddChild(_memoryState);
        var keys = new[] { "MC", "MR", "M+", "M−", "C", "⌫", "%", "÷", "7", "8", "9", "×", "4", "5", "6", "−", "1", "2", "3", "+", "±", "0", ".", "=" };
        for (var row = 0; row < 6; row++)
        {
            var line = new BoxContainer { SeparationOverride = 4, HorizontalExpand = true };
            for (var column = 0; column < 4; column++)
            {
                var key = keys[row * 4 + column];
                var button = PdaAppUi.Action(key == "⌫" ? "DEL" : key, key == "="); button.MinHeight = 28; button.HorizontalExpand = true;
                button.OnPressed += _ => Press(key); line.AddChild(button);
            }
            keypad.AddChild(line);
        }
        _expression.OnTextEntered += _ => Evaluate();
        _expression.OnTextChanged += _ => _replace = false;
        AddChild(keypad); AddChild(new PanelContainer { StyleClasses = { "pda-divider" }, MinWidth = 1 });
        var history = new BoxContainer { Orientation = LayoutOrientation.Vertical, SeparationOverride = 5, HorizontalExpand = true };
        history.AddChild(new Label { Text = Loc.GetString("pda-calc-history"), StyleClasses = { "pda-caption" } });
        history.AddChild(new ScrollContainer { HScrollEnabled = false, VerticalExpand = true, Children = { _history } });
        AddChild(history);
    }
    public void Press(string key)
    {
        switch (key)
        {
            case "=": Evaluate(); return;
            case "C": _expression.Text = "0"; _result.Text = "0"; _replace = true; break;
            case "⌫": _expression.Text = _expression.Text.Length > 1 ? _expression.Text[..^1] : "0"; _replace = false; break;
            case "±": _expression.Text = "-(" + _expression.Text + ")"; _replace = false; break;
            case "MC": _memory = 0; break;
            case "MR": _expression.Text = _memory.ToString(CultureInfo.InvariantCulture); _replace = true; break;
            case "M+": ChangeMemory(false); break;
            case "M−": ChangeMemory(true); break;
            default:
                if (_expression.Text.Length >= 256) return;
                if (_replace && (char.IsAsciiDigit(key[0]) || key == ".")) _expression.Text = "";
                _expression.Text += key; _replace = false; break;
        }
        _memoryState.Text = _memory == 0 ? "" : "M = " + _memory.ToString(CultureInfo.CurrentCulture);
        _expression.CursorPosition = _expression.Text.Length; _expression.GrabKeyboardFocus();
    }
    private void ChangeMemory(bool subtract)
    {
        if (PdaCalculator.TryEvaluate(_expression.Text, out var value) &&
            PdaCalculator.TryEvaluate(_memory.ToString(CultureInfo.InvariantCulture) +
                                      (subtract ? "-(" : "+(") + value.ToString(CultureInfo.InvariantCulture) + ")", out var result))
            _memory = result;
        else
            _result.Text = Loc.GetString("pda-os-calculator-invalid");
    }
    public void Evaluate()
    {
        var expression = _expression.Text;
        if (!PdaCalculator.TryEvaluate(expression, out var value)) { _result.Text = Loc.GetString("pda-os-calculator-invalid"); return; }
        var result = value.ToString("0.############################", CultureInfo.InvariantCulture);
        _result.Text = result; _result.ToolTip = result; _expression.Text = result; _replace = true;
        var item = PdaAppUi.Action(expression + " = " + result); item.ClipText = true; item.ToolTip = item.Text;
        item.OnPressed += _ => { _expression.Text = result; _result.Text = result; _replace = true; };
        _history.AddChild(item); if (_history.ChildCount > 20) _history.GetChild(0).Dispose();
    }
}


