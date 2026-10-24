using Content.Server.Eclipse.Rewards;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server.Eclipse.Commands;

[AnyCommand]
public sealed class ClaimRewardsCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "claimrewards";
    public string Description => "Получить ожидающие награды Eclipse Station.";
    public string Help => "claimrewards";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (player.AuthType != LoginType.LoggedIn)
        {
            shell.WriteLine("Для получения наград нужен авторизованный SS14 аккаунт.");
            return;
        }

        await _entityManager.System<EclipseRewardManager>().ClaimPendingRewardsAsync(player);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.Empty;
    }
}

[AnyCommand]
public sealed class RewardsCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entityManager = default!;

    public string Command => "rewards";
    public string Description => "Получить ожидающие награды Eclipse Station.";
    public string Help => "rewards";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (player.AuthType != LoginType.LoggedIn)
        {
            shell.WriteLine("Для получения наград нужен авторизованный SS14 аккаунт.");
            return;
        }

        await _entityManager.System<EclipseRewardManager>().ClaimPendingRewardsAsync(player);
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.Empty;
    }
}
