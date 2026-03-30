using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

public class TelegramPollingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ITelegramBotClient telegramBotClient,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramPollingBackgroundService> logger) : BackgroundService
{
    private long? _offset;
    private bool _webhookResetCompleted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var telegramBotOptions = options.Value;

            if (!telegramBotOptions.Enabled || string.IsNullOrWhiteSpace(telegramBotOptions.BotToken))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            try
            {
                if (!_webhookResetCompleted)
                {
                    await telegramBotClient.DeleteWebhookAsync(stoppingToken);
                    _webhookResetCompleted = true;
                    logger.LogInformation("Telegram webhook reset for long polling mode.");
                }

                var updates = await telegramBotClient.GetUpdatesAsync(_offset, stoppingToken);

                foreach (var update in updates)
                {
                    _offset = update.UpdateId + 1;
                    await ProcessUpdateAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram polling loop failed. Retrying after delay.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, telegramBotOptions.PollingDelaySeconds)), stoppingToken);
            }
        }
    }

    private async Task ProcessUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken)
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
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var commandReplyService = scope.ServiceProvider.GetRequiredService<ICommandReplyService>();

        await UpsertChatSubscriptionAsync(dbContext, chat, cancellationToken);

        try
        {
            var chatId = chat.Id.ToString();
            var replyText = await commandReplyService.BuildReplyAsync(text, chatId, cancellationToken);
            var sendResult = await telegramBotClient.SendTextMessageAsync(chatId, replyText, cancellationToken);

            dbContext.PushLogs.Add(new PushLog
            {
                PushType = "TelegramReply",
                TargetGroupId = chatId,
                MessageTitle = BuildMessageTitle(text),
                IsSuccess = sendResult.IsSuccess,
                ErrorMessage = sendResult.IsSuccess ? null : sendResult.ErrorMessage,
                CreatedTime = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Telegram reply flow failed for chat {ChatId}.", chat.Id);

            var fallbackResult = await TrySendFallbackReplyAsync(telegramBotClient, chat.Id.ToString(), cancellationToken);

            dbContext.PushLogs.Add(new PushLog
            {
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

    private static async Task UpsertChatSubscriptionAsync(ApplicationDbContext dbContext, TelegramChat chat, CancellationToken cancellationToken)
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

    private static async Task<string?> TrySendFallbackReplyAsync(ITelegramBotClient telegramBotClient, string chatId, CancellationToken cancellationToken)
    {
        var fallbackMessage = "剛剛整理資料時發生錯誤，我們已經記錄下來請稍後再試一次";
        var fallbackResult = await telegramBotClient.SendTextMessageAsync(chatId, fallbackMessage, cancellationToken);
        return fallbackResult.IsSuccess ? "Fallback reply sent after command failure." : fallbackResult.ErrorMessage;
    }
}
