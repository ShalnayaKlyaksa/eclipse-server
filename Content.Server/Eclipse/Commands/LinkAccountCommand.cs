using Content.Server.Chat.Managers;
using Content.Server.Eclipse.Integration;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server.Eclipse.Commands;

[AnyCommand]
public sealed class LinkAccountCommand : IConsoleCommand
{
    [Dependency] private readonly EclipseSiteClient _siteClient = default!;
    [Dependency] private readonly IChatManager _chat = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public string Command => "link";
    public string Description => "Привязать SS14 аккаунт к аккаунту сайта Eclipse Station.";
    public string Help => "link ECL-123456";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (!_cfg.GetCVar(CCVars.EclipseSiteEnableAccountLinking))
        {
            WritePlayer(shell, "Привязка аккаунта временно отключена.");
            return;
        }

        if (player.AuthType != LoginType.LoggedIn)
        {
            WritePlayer(shell, "Для привязки нужен авторизованный SS14 аккаунт.");
            return;
        }

        if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            WritePlayer(shell, "Использование: link ECL-123456. Код можно получить в личном кабинете на сайте Eclipse Station.");
            return;
        }

        var response = await _siteClient.ConfirmAccountLinkAsync(args[0].Trim(), player.UserId.ToString(), player.Name);
        if (response.Success)
        {
            WritePlayer(shell, "Аккаунт успешно привязан к сайту Eclipse Station.");
            return;
        }

        WritePlayer(shell, TranslateLinkError(response.Message));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHint("ECL-123456")
            : CompletionResult.Empty;
    }

    private void WritePlayer(IConsoleShell shell, string message)
    {
        shell.WriteLine(message);

        if (shell.Player is { } player)
            _chat.DispatchServerMessage(player, message, suppressLog: true);
    }

    private static string TranslateLinkError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Сайт Eclipse Station временно недоступен. Попробуйте позже.";

        var lower = message.ToLowerInvariant();

        if (lower.Contains("expired") || lower.Contains("ист"))
            return "Код привязки истёк. Создайте новый код в личном кабинете.";

        if (lower.Contains("not found") || lower.Contains("invalid") || lower.Contains("не найден") || lower.Contains("невер"))
            return "Код привязки не найден. Проверьте код или создайте новый в личном кабинете.";

        if (lower.Contains("used") || lower.Contains("использ"))
            return "Код привязки уже использован. Создайте новый код в личном кабинете.";

        if ((lower.Contains("ss14") || lower.Contains("user")) &&
            (lower.Contains("already linked") || lower.Contains("already bound") || lower.Contains("уже привязан")))
        {
            return "Этот SS14 аккаунт уже привязан к аккаунту сайта.";
        }

        if ((lower.Contains("site") || lower.Contains("website") || lower.Contains("account")) &&
            (lower.Contains("already linked") || lower.Contains("already bound") || lower.Contains("уже привязан")))
        {
            return "Аккаунт сайта уже привязан к SS14 аккаунту.";
        }

        return message;
    }
}
