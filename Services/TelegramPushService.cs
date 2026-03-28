using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Services;

public class TelegramPushService(ApplicationDbContext dbContext, ITelegramBotClient telegramBotClient, ILogger<TelegramPushService> logger) : ITelegramPushService
{
    public async Task<bool> SendPushAsync(string chatId, string messageTitle, string messageBody, string pushType, CancellationToken cancellationToken = default)
    {
        var combinedMessage = string.IsNullOrWhiteSpace(messageBody)
            ? messageTitle
            : $"{messageTitle}\n\n{messageBody}";

        var result = await telegramBotClient.SendTextMessageAsync(chatId, combinedMessage, cancellationToken);

        dbContext.PushLogs.Add(new PushLog
        {
            TargetGroupId = chatId,
            MessageTitle = messageTitle,
            PushType = pushType,
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.IsSuccess ? null : result.ErrorMessage,
            CreatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Recorded Telegram push log for chat {ChatId}. Success={IsSuccess}", chatId, result.IsSuccess);
        return result.IsSuccess;
    }
}
