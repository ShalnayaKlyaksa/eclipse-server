using Content.Server.Maps;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Server.GameTicking.Commands;

internal static class ForcedGameMapCommandHelper
{
    internal enum Result
    {
        Success,
        Cleared,
        MapNotFound,
    }

    internal static string NormalizeMapArgument(string arg)
    {
        if (arg.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return arg;
    }

    internal static Result TrySetForcedMap(
        IConfigurationManager configurationManager,
        IGameMapManager gameMapManager,
        string name)
    {
        if (!string.IsNullOrEmpty(name) && !gameMapManager.CheckMapExists(name))
            return Result.MapNotFound;

        configurationManager.SetCVar(CCVars.GameMap, name);
        return string.IsNullOrEmpty(name) ? Result.Cleared : Result.Success;
    }
}
