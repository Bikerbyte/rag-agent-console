using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecurityAdvisoryBot.Pages.Operations;

public class EvaluationModel(
    IRetrievalEvaluationService evaluationService,
    ILogger<EvaluationModel> logger) : PageModel
{
    public IReadOnlyList<RetrievalEvaluationCase> Cases { get; private set; } = [];
    public RetrievalEvaluationReport? Report { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? LastRunAt => Report?.RanAt;
    public IReadOnlyList<string> AvailableModes { get; } = [RetrievalModes.Hybrid, RetrievalModes.Vector, RetrievalModes.Keyword];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Cases = await evaluationService.LoadCasesAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            Cases = await evaluationService.LoadCasesAsync(cancellationToken);
            if (Cases.Count == 0)
            {
                ErrorMessage = "No evaluation cases found. Ensure Evaluation/golden-set.json exists and contains cases.";
                return Page();
            }

            Report = await evaluationService.EvaluateAsync(retrievalModes: null, topK: 5, cancellationToken: cancellationToken);
            return Page();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Retrieval evaluation failed.");
            ErrorMessage = $"Evaluation failed: {exception.Message}";
            return Page();
        }
    }

    public string FormatPercent(double value) => $"{value * 100:0.0}%";
    public string FormatMrr(double value) => value.ToString("0.000");

    public string ModeStatusClass(double value)
        => value switch
        {
            >= 0.75 => "is-success",
            >= 0.5 => "is-warning",
            _ => "is-missing"
        };
}
