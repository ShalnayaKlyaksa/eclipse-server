using System.Linq;
using Content.Shared.Research.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Erida.Research.UI;

public sealed partial class FancyResearchConsoleMenu
{
    public Action<string, string, bool>? OnAuthenticate;
    public Action<string>? OnCommand;
    private bool _hasTerminalSession;
    private bool _canResearch;
    private bool _terminalVisible;
    private string? _sessionUser;
    private string[] _terminalLines = [];
    private bool _scrollTerminal;
    private static readonly string[] Commands = ["help", "status", "whoami", "techs", "find", "info", "tree", "queue", "jobs", "cancel", "servers", "connect", "users", "role", "reset", "mail", "history", "logout", "plan", "research", "batch", "analyze", "trace", "tune", "verify", "terminals", "remote", "revoke", "clear", "exit"];

    private void InitializeSessionControls()
    {
        LoginHelp.SetMessage(Loc.GetString("research-terminal-login-help"), defaultColor: Color.FromHex("#A6A6A6"));
        LoginName.IsValid = text => text.Length <= 24;
        CommandInput.IsValid = text => text.Length <= 512;
        OnOpen += () => { if (!_hasTerminalSession) LoginName.GrabKeyboardFocus(); };
        LoginButton.OnPressed += _ => SubmitLogin(false);
        LoginName.OnTextEntered += _ => LoginPassword.GrabKeyboardFocus();
        LoginPassword.OnTextEntered += _ => SubmitLogin(false);
        GraphTab.OnPressed += _ => { ShowTerminal(false); if (_disciplineFilter == "Energy") SelectResearchSection(1); };
        EnergyTab.OnPressed += _ =>
        {
            ShowTerminal(false);
            var discipline = _disciplines.IndexOf("Energy");
            if (discipline >= 0) { SelectResearchSection(discipline + 2); FitGraph(); }
        };
        CommandTab.OnPressed += _ => ShowTerminal(true);
        CommandSend.OnPressed += _ => CommandInput.ForceSubmitText();
        CommandInput.OnTextEntered += _ => SubmitCommand();
        foreach (var (topic, caption) in new[]
                 { ("", "overview"), ("science", "science"), ("analysis", "analysis"), ("network", "network"), ("accounts", "accounts") })
        {
            var button = new Button { Text = Loc.GetString("research-terminal-reference-" + caption), HorizontalExpand = true };
            button.AddStyleClass("terminal-control");
            button.OnPressed += _ => { OnCommand?.Invoke("help " + topic); CommandInput.GrabKeyboardFocus(); };
            CommandHelpButtons.AddChild(button);
        }
        CommandReferenceHint.SetMessage(Loc.GetString("research-terminal-reference-hint"), defaultColor: Color.FromHex("#A6A6A6"));
        CommandInput.OnTextChanged += _ => UpdateCommandUsage();
        CommandInput.OnTabComplete += args => CompleteCommand(args.Text);
        UpdateCommandUsage();
    }

    private void SubmitLogin(bool register)
    {
        if (string.IsNullOrWhiteSpace(LoginName.Text) || string.IsNullOrWhiteSpace(LoginPassword.Text)) return;
        OnAuthenticate?.Invoke(LoginName.Text, LoginPassword.Text, register);
        LoginPassword.SetText("", true);
    }

    private void SubmitCommand()
    {
        var command = CommandInput.Text.Trim();
        if (!_hasTerminalSession || command.Length == 0) return;
        OnCommand?.Invoke(command);
        CommandInput.Text = "";
    }

    public void ShowTerminal(bool visible)
    {
        _terminalVisible = visible;
        GraphPage.Visible = !visible;
        CommandPage.Visible = visible;
        UpdateGraphTabs();
        if (visible) CommandInput.GrabKeyboardFocus();
    }

    private void UpdateGraphTabs()
    {
        var energy = !_terminalVisible && _disciplineFilter == "Energy";
        GraphTab.Pressed = !_terminalVisible && !energy;
        EnergyTab.Pressed = energy;
        CommandTab.Pressed = _terminalVisible;
    }

    public void UpdateTerminalState(ResearchTerminalState state)
    {
        var changed = _hasTerminalSession != (state.Username != null) || _sessionUser != state.Username || _canResearch != state.CanResearch;
        _hasTerminalSession = state.Username != null;
        ResearchAmountLabel.Visible = _hasTerminalSession;
        DiskHint.Visible = _hasTerminalSession;
        _sessionUser = state.Username;
        _canResearch = state.CanResearch;
        WriteDisk.Disabled = !_canResearch;
        AuthenticationPage.Visible = !_hasTerminalSession;
        AuthenticatedPage.Visible = _hasTerminalSession;
        SessionCaption.Text = $"{state.ServerName} / lab-{state.Address}" + (_hasTerminalSession ? $" / {state.Username}" : "");
        if (!_terminalLines.SequenceEqual(state.Lines))
        {
            _terminalLines = state.Lines;
            var message = new FormattedMessage();
            foreach (var line in state.Lines.SelectMany(l => l.Split('\n')))
            {
                message.PushColor(line.Contains("@lab-", StringComparison.Ordinal) ? Color.FromHex("#DBEACB") : line.StartsWith("==", StringComparison.Ordinal) ? Color.FromHex("#95B9A0") : Color.FromHex("#91B39B"));
                message.AddText(line);
                message.Pop();
                message.PushNewline();
            }
            CommandOutput.SetMessage(message, defaultColor: Color.FromHex("#EEEEEE"));
            LoginFeedback.SetMessage(FormattedMessage.FromUnformatted(state.Lines.LastOrDefault() ?? ""), defaultColor: Color.FromHex("#C6B58A"));
            _scrollTerminal = true;
        }
        QueueSummary.Text = Loc.GetString(state.QueuedTechnologies.Length == 0 ? "research-terminal-queue-idle" : "research-terminal-queue-count", ("count", state.QueuedTechnologies.Length));
        BrowserQueueStatus.Text = QueueSummary.Text;
        CommandPrompt.Text = $"{state.Username}@lab-{state.Address}>";
        if (changed)
        {
            LoginPassword.SetText("", true);
            CommandInput.ClearHistory();
            ShowTerminal(_terminalVisible);
            if (CurrentTech is { } technology && List.TryGetValue(technology.Id, out var availability))
                SelectTech(_prototype.Index(technology), availability);
            _pendingFocus = true;
        }
    }

    private void UpdateCommandUsage()
    {
        var verb = CommandInput.Text.Split(' ', 2)[0].ToLowerInvariant();
        var key = verb switch
        {
            "research" or "plan" or "info" or "find" or "techs" or "tree" or "batch" => "science",
            "queue" or "jobs" or "cancel" => "queue",
            "analyze" or "trace" or "tune" or "verify" => "analysis",
            "servers" or "connect" or "terminals" or "remote" => "network",
            "mail" or "users" or "role" or "reset" or "revoke" => "accounts",
            _ => "overview",
        };
        CommandUsage.SetMessage(Loc.GetString("research-terminal-input-hint-" + key), defaultColor: Color.FromHex("#A6A6A6"));
    }

    private void CompleteCommand(string text)
    {
        var space = text.LastIndexOf(' ');
        var prefix = space < 0 ? text : text[(space + 1)..];
        var candidates = space < 0 ? Commands : List.Keys.Select(id => id.StartsWith("Industrial", StringComparison.Ordinal) ? id[10..].ToLowerInvariant() : id).ToArray();
        var matches = candidates.Where(c => c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct().OrderBy(c => c).Take(12).ToArray();
        if (matches.Length == 1)
        {
            CommandInput.Text = (space < 0 ? "" : text[..(space + 1)]) + matches[0] + " ";
            CommandInput.CursorPosition = CommandInput.Text.Length;
        }
        else if (matches.Length > 1)
            CommandUsage.SetMessage(FormattedMessage.FromUnformatted(string.Join(" · ", matches)), defaultColor: Color.FromHex("#EEEEEE"));
    }

    private void UpdateSessionLayout()
    {
        CommandReference.Visible = Width >= 1000;
        if (_scrollTerminal && CommandPage.Visible && CommandOutput.IsArrangeValid)
        {
            TerminalScroll.VScrollTarget = CommandOutput.Height;
            _scrollTerminal = false;
        }
    }
}
