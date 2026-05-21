namespace SecurityAdvisoryBot.Models;

public class TelegramSendResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}
