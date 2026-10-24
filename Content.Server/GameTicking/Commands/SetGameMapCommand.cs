using Content.Server.Administration;
using Content.Server.Maps;
using Content.Shared.Administration;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Console;

namespace Content.Server.GameTicking.Commands
{
    [AdminCommand(AdminFlags.Round)]
    public sealed class SetGameMapCommand : IConsoleCommand
    {
        [Dependency] private readonly IConfigurationManager _configurationManager = default!;
        [Dependency] private readonly IGameMapManager _gameMapManager = default!;

        public string Command => "setgamemap";
        public string Description => Loc.GetString("set-game-map-command-description", ("command", Command));
        public string Help => Loc.GetString("set-game-map-command-help-text", ("command", Command));

        public void Execute(IConsoleShell shell, string argStr, string[] args)
        {
            if (args.Length != 1)
            {
                shell.WriteError(Loc.GetString("shell-need-exactly-one-argument"));
                return;
            }

            var name = ForcedGameMapCommandHelper.NormalizeMapArgument(args[0]);

            switch (ForcedGameMapCommandHelper.TrySetForcedMap(_configurationManager, _gameMapManager, name))
            {
                case ForcedGameMapCommandHelper.Result.MapNotFound:
                    shell.WriteError(Loc.GetString("set-game-map-map-error", ("map", args[0])));
                    break;
                case ForcedGameMapCommandHelper.Result.Cleared:
                    shell.WriteLine(Loc.GetString("set-game-map-map-cleared"));
                    break;
                case ForcedGameMapCommandHelper.Result.Success:
                    shell.WriteLine(Loc.GetString("set-game-map-map-set", ("map", name)));
                    break;
            }
        }

        public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        {
            return args.Length switch
            {
                1 => CompletionResult.FromHintOptions(
                    CompletionHelper.PrototypeIDs<GameMapPrototype>(),
                    Loc.GetString("set-game-map-command-hint-1")),
                _ => CompletionResult.Empty
            };
        }
    }
}
