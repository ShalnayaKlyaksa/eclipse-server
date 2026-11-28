using System.Globalization;
using System.Security.Cryptography;
using Content.Server.Research.Components;
using Content.Shared.Research.Components;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void StartTerminalAnalysis(EntityUid uid, ResearchConsoleComponent console, EntityUid actor, string argument)
    {
        if (!CanResearchAtTerminal(uid, actor)) { TerminalPrint(console, Loc.GetString("research-terminal-operation-denied")); return; }
        if (_timing.CurTime < console.NextAnalysis)
        { TerminalPrint(console, Loc.GetString("research-terminal-analysis-cooldown")); return; }
        var technology = FindTerminalTechnology(argument);
        if (technology == null) { TerminalPrint(console, Loc.GetString("research-terminal-tech-not-found")); return; }
        SyncClientWithServer(uid);
        var accounts = TerminalAccounts(console.AccountServer!.Value);
        if (accounts.AnalyzedTechnologies.Contains(technology.ID) ||
            !IsTechnologyAvailable(Comp<TechnologyDatabaseComponent>(uid), technology))
        { TerminalPrint(console, Loc.GetString("research-terminal-analysis-ineligible")); return; }
        var analysis = new ResearchAnalysis
        {
            Technology = technology.ID,
            Node = RandomNumberGenerator.GetInt32(3),
            Offset = RandomNumberGenerator.GetInt32(-12, 13),
        };
        console.Analysis = analysis;
        console.NextAnalysis = _timing.CurTime + TimeSpan.FromMinutes(3);
        var values = new[] { RandomNumberGenerator.GetInt32(20, 60), RandomNumberGenerator.GetInt32(20, 60), RandomNumberGenerator.GetInt32(20, 60) };
        values[analysis.Node] = RandomNumberGenerator.GetInt32(80, 100);
        TerminalPrint(console, Loc.GetString("research-terminal-analysis-trace", ("a", values[0]), ("b", values[1]), ("c", values[2])));
    }

    private void AdvanceTerminalAnalysis(EntityUid uid, ResearchConsoleComponent console, EntityUid actor, string verb, string argument)
    {
        if (!CanResearchAtTerminal(uid, actor))
        {
            console.Analysis = null;
            TerminalPrint(console, Loc.GetString("research-terminal-operation-denied"));
            return;
        }
        if (console.Analysis is not { } analysis)
        { TerminalPrint(console, Loc.GetString("research-terminal-analysis-none")); return; }
        if (_timing.CurTime >= console.NextAnalysis)
        {
            console.Analysis = null;
            TerminalPrint(console, Loc.GetString("research-terminal-analysis-expired"));
            return;
        }
        var correct = analysis.Stage switch
        {
            0 => verb == "trace" && argument.Equals(((char) ('A' + analysis.Node)).ToString(), StringComparison.OrdinalIgnoreCase),
            1 => verb == "tune" && int.TryParse(argument, out var offset) && offset == analysis.Offset,
            2 => verb == "verify" && int.TryParse(argument.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? argument[2..] : argument,
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var checksum) && checksum == analysis.Checksum,
            _ => false,
        };
        if (!correct)
        {
            console.Analysis = null;
            console.NextAnalysis = _timing.CurTime + TimeSpan.FromSeconds(45);
            TerminalPrint(console, Loc.GetString("research-terminal-analysis-failed"));
            return;
        }
        analysis.Stage++;
        if (analysis.Stage == 1)
            TerminalPrint(console, Loc.GetString("research-terminal-analysis-tune", ("reference", 300), ("measured", 300 - analysis.Offset)));
        else if (analysis.Stage == 2)
        {
            var a = RandomNumberGenerator.GetInt32(16, 220);
            var b = RandomNumberGenerator.GetInt32(16, 220);
            var c = RandomNumberGenerator.GetInt32(16, 220);
            analysis.Checksum = (a + b + c) & 255;
            TerminalPrint(console, Loc.GetString("research-terminal-analysis-verify", ("a", a.ToString("X2")), ("b", b.ToString("X2")), ("c", c.ToString("X2"))));
        }
        else
        {
            var technology = PrototypeManager.Index<Content.Shared.Research.Prototypes.TechnologyPrototype>(analysis.Technology);
            var accounts = TerminalAccounts(console.AccountServer!.Value);
            if (accounts.AnalyzedTechnologies.Add(analysis.Technology))
            {
                var reward = Math.Clamp(technology.Cost / 10, 100, 600);
                ModifyServerPoints(console.AccountServer.Value, reward);
                TerminalPrint(console, Loc.GetString("research-terminal-analysis-complete", ("points", reward)));
            }
            console.Analysis = null;
            console.NextAnalysis = _timing.CurTime;
        }
    }
}
