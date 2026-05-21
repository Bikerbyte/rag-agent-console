namespace SecurityAdvisoryBot.Services;

public interface ISecurityAdvisoryAnswerService
{
    Task<string> BuildAnswerAsync(string question, CancellationToken cancellationToken = default);
}
