using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 實際處理 Telegram update 的 service。
/// 先集中在這裡，後面若要改成 queue 化，搬移會比較容易。
/// </summary>
public class TelegramUpdateProcessingService(
    ApplicationDbContext dbContext,
    ICommandReplyService commandReplyService,
    ITelegramBotClient telegramBotClient,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<TelegramUpdateProcessingService> logger) : ITelegramUpdateProcessingService
{
    public async Task ProcessUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken = default)
    {
        if (update.CallbackQuery is { Message.Chat: not null } callbackQuery)
        {
            var callbackText = callbackQuery.Data?.Trim();
            if (!string.IsNullOrWhiteSpace(callbackText))
            {
                await ProcessIncomingTextAsync(callbackQuery.Message.Chat, callbackText, cancellationToken);
            }

            await telegramBotClient.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken);
            return;
        }

        var text = update.Message?.Text?.Trim();
        var chat = update.Message?.Chat;

        if (string.IsNullOrWhiteSpace(text) || chat is null)
        {
            return;
        }

        await ProcessIncomingTextAsync(chat, text, cancellationToken);
    }

    private async Task ProcessIncomingTextAsync(TelegramChat chat, string text, CancellationToken cancellationToken)
    {
        // 只要 chat 曾經對 bot 講過話，就先建立一筆訂閱資料，後面管理頁才接得上。
        await UpsertChatSubscriptionAsync(chat, cancellationToken);

        try
        {
            var chatId = chat.Id.ToString();
            var replyText = await commandReplyService.BuildReplyAsync(text, chatId, cancellationToken);
            var sendResult = await telegramBotClient.SendTextMessageAsync(chatId, replyText, cancellationToken);

            dbContext.PushLogs.Add(new PushLog
            {
                InstanceName = runtimeOptions.Value.InstanceName,
                PushType = "TelegramReply",
                TargetGroupId = chatId,
                MessageTitle = BuildMessageTitle(text),
                IsSuccess = sendResult.IsSuccess,
                ErrorMessage = sendResult.IsSuccess ? null : sendResult.ErrorMessage,
                CreatedTime = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            if (!sendResult.IsSuccess)
            {
                throw new InvalidOperationException(sendResult.ErrorMessage ?? "Telegram sendTextMessage failed.");
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Telegram reply flow failed for chat {ChatId}.", chat.Id);

            var fallbackResult = await TrySendFallbackReplyAsync(chat.Id.ToString(), cancellationToken);

            dbContext.PushLogs.Add(new PushLog
            {
                InstanceName = runtimeOptions.Value.InstanceName,
                PushType = "TelegramReply",
                TargetGroupId = chat.Id.ToString(),
                MessageTitle = BuildMessageTitle(text),
                IsSuccess = false,
                ErrorMessage = fallbackResult ?? exception.Message,
                CreatedTime = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task UpsertChatSubscriptionAsync(TelegramChat chat, CancellationToken cancellationToken)
    {
        var chatId = chat.Id.ToString();
        var displayName = BuildChatDisplayName(chat);
        var subscription = await dbContext.TelegramChatSubscriptions
            .FirstOrDefaultAsync(item => item.ChatId == chatId, cancellationToken);

        if (subscription is null)
        {
            dbContext.TelegramChatSubscriptions.Add(new TelegramChatSubscription
            {
                ChatId = chatId,
                ChatTitle = displayName,
                EnableSchedulePush = true,
                EnableNewsPush = true,
                FollowedTeamCode = null,
                CreatedTime = DateTimeOffset.UtcNow,
                LastUpdatedTime = DateTimeOffset.UtcNow
            });
        }
        else if (!string.Equals(subscription.ChatTitle, displayName, StringComparison.Ordinal))
        {
            subscription.ChatTitle = displayName;
            subscription.LastUpdatedTime = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildChatDisplayName(TelegramChat chat)
    {
        if (!string.IsNullOrWhiteSpace(chat.Title))
        {
            return chat.Title;
        }

        var combinedName = string.Join(' ', new[] { chat.FirstName, chat.LastName }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(combinedName))
        {
            return combinedName;
        }

        return chat.Username ?? chat.Id.ToString();
    }

    private static string BuildMessageTitle(string commandText)
    {
        var compactText = string.Join(' ', commandText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var title = $"Command: {compactText}";
        return title.Length <= 200 ? title : title[..200];
    }

    private async Task<string?> TrySendFallbackReplyAsync(string chatId, CancellationToken cancellationToken)
    {
        var fallbackMessage = "剛剛整理資料時發生錯誤，我們已經記錄下來請稍後再試一次";
        var fallbackResult = await telegramBotClient.SendTextMessageAsync(chatId, fallbackMessage, cancellationToken);
        return fallbackResult.IsSuccess ? "Fallback reply sent after command failure." : fallbackResult.ErrorMessage;
    }
}
