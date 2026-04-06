using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 送出單筆 Telegram 推播，並把結果寫進 <see cref="PushLog"/>。
/// </summary>
public class TelegramPushService(ApplicationDbContext dbContext, ITelegramBotClient telegramBotClient, ILogger<TelegramPushService> logger) : ITelegramPushService
{
    public async Task<bool> SendPushAsync(string chatId, string messageTitle, string messageBody, string pushType, CancellationToken cancellationToken = default)
    {
        // 送出後立刻記錄 DB log，之後比較容易追每一次 outbound push 的結果。
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
