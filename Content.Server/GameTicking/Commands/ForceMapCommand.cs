using System.Linq;
using Content.Server.Administration;
using Content.Server.Maps;
using Content.Shared.Administration;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server.GameTicking.Commands
{
    [AdminCommand(AdminFlags.Round)]
    public sealed class ForceMapCommand : LocalizedCommands
    {
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;
        [Dependency] private readonly IGameMapManager _gameMapManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

        public override string Command => "forcemap";

        public override void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 1)
            {
                shell.WriteLine(Loc.GetString("shell-need-exactly-one-argument"));
                return;
            }

            var name = args[0];

            switch (ForcedGameMapCommandHelper.TrySetForcedMap(_configurationManager, _gameMapManager, name))
            {
                case ForcedGameMapCommandHelper.Result.MapNotFound:
                    shell.WriteLine(Loc.GetString("cmd-forcemap-map-not-found", ("map", name)));
                    break;
                case ForcedGameMapCommandHelper.Result.Cleared:
                    shell.WriteLine(Loc.GetString("cmd-forcemap-cleared"));
                    break;
                case ForcedGameMapCommandHelper.Result.Success:
                    shell.WriteLine(Loc.GetString("cmd-forcemap-success", ("map", name)));
                    break;
            }
        }

        public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            if (args.Length == 1)
            {
                var options = _prototypeManager
                    .EnumeratePrototypes<GameMapPrototype>()
                    .Select(p => new CompletionOption(p.ID, p.MapName))
                    .OrderBy(p => p.Value);

                return CompletionResult.FromHintOptions(options, Loc.GetString($"cmd-forcemap-hint"));
            }

            return CompletionResult.Empty;
        }
    }
}
