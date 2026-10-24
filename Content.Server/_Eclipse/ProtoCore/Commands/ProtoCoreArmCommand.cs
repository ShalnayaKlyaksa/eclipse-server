using Content.Server._Eclipse.ProtoCore.Components;
using Content.Server.Administration;
using Content.Shared.Administration;
using JetBrains.Annotations;
using Robust.Shared.Console;

namespace Content.Server._Eclipse.ProtoCore.Commands;

[UsedImplicitly]
[AdminCommand(AdminFlags.Fun)]
public sealed class ProtoCoreArmCommand : LocalizedCommands
{
    [Dependency] private readonly IEntityManager _entManager = default!;

    public override string Command => "protocorearm";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        EntityUid? coreUid = null;
        ProtoCoreComponent? core = null;

        if (args.Length >= 2)
        {
            if (!_entManager.TryParseNetEntity(args[1], out coreUid))
            {
                shell.WriteError(Loc.GetString("shell-entity-uid-must-be-number"));
                return;
            }

            if (!_entManager.TryGetComponent(coreUid.Value, out core))
            {
                shell.WriteError(Loc.GetString("cmd-protocorearm-not-found"));
                return;
            }
        }
        else
        {
            var query = _entManager.EntityQueryEnumerator<ProtoCoreComponent>();
            while (query.MoveNext(out var uid, out core))
            {
                coreUid = uid;
                break;
            }

            if (coreUid == null)
            {
                shell.WriteError(Loc.GetString("cmd-protocorearm-not-found"));
                return;
            }
        }

        float? timer = null;
        if (args.Length >= 1)
        {
            if (!float.TryParse(args[0], out var parsedTimer))
            {
                shell.WriteError(Loc.GetString("shell-argument-must-be-number"));
                return;
            }

            timer = parsedTimer;
        }

        if (core == null)
        {
            shell.WriteError(Loc.GetString("cmd-protocorearm-not-found"));
            return;
        }

        var protoCore = _entManager.System<ProtoCoreSystem>();
        protoCore.StartMeltdown((coreUid.Value, core), timer);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
            return CompletionResult.FromHint(Loc.GetString("cmd-protocorearm-1-help"));

        if (args.Length == 2)
            return CompletionResult.FromHintOptions(CompletionHelper.Components<ProtoCoreComponent>(args[1]), Loc.GetString("cmd-protocorearm-2-help"));

        return CompletionResult.Empty;
    }
}
