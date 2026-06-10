using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RagAgentConsole.Pages.Operations;

public class EvaluationModel(
    IRetrievalEvaluationService evaluationService,
    ILogger<EvaluationModel> logger) : PageModel
{
    public IReadOnlyList<RetrievalEvaluationCaseEntity> ManagedCases { get; private set; } = [];
    public RetrievalEvaluationReport? Report { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? LastRunAt => Report?.RanAt;
    public IReadOnlyList<string> AvailableModes { get; } = [RetrievalModes.Hybrid, RetrievalModes.Vector, RetrievalModes.Keyword];

    public bool IsEditMode => Input.RetrievalEvaluationCaseId.HasValue;

    [BindProperty]
    public CaseInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(int? edit, CancellationToken cancellationToken)
    {
        ManagedCases = await evaluationService.GetManagedCasesAsync(cancellationToken);

        if (edit is int editId)
        {
            var existing = await evaluationService.GetCaseAsync(editId, cancellationToken);
            if (existing is not null)
            {
                Input = new CaseInput
                {
                    RetrievalEvaluationCaseId = existing.RetrievalEvaluationCaseId,
                    Question = existing.Question,
                    ExpectedCveIds = existing.ExpectedCveIds,
                    ExpectedDocumentTitles = existing.ExpectedDocumentTitles,
                    ExpectedContentKeywords = existing.ExpectedContentKeywords,
                    ExpectedMetadata = existing.ExpectedMetadata,
                    Notes = existing.Notes
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Input.Question))
        {
            StatusMessage = "請先填寫評估問題。";
            return RedirectToPage();
        }

        if (string.IsNullOrWhiteSpace(Input.ExpectedDocumentTitles) &&
            string.IsNullOrWhiteSpace(Input.ExpectedContentKeywords) &&
            string.IsNullOrWhiteSpace(Input.ExpectedCveIds) &&
            string.IsNullOrWhiteSpace(Input.ExpectedMetadata))
        {
            StatusMessage = "請至少設定一種預期命中條件。";
            return RedirectToPage();
        }

        var draft = new RetrievalEvaluationCaseDraft(
            Input.Question,
            Input.ExpectedCveIds,
            Input.ExpectedDocumentTitles,
            Input.Notes,
            Input.ExpectedMetadata,
            Input.ExpectedContentKeywords);

        if (Input.RetrievalEvaluationCaseId is int id)
        {
            await evaluationService.UpdateCaseAsync(id, draft, cancellationToken);
            StatusMessage = "評估案例已更新。";
        }
        else
        {
            await evaluationService.CreateCaseAsync(draft, cancellationToken);
            StatusMessage = "評估案例已新增。";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id, CancellationToken cancellationToken)
    {
        await evaluationService.DeleteCaseAsync(id, cancellationToken);
        StatusMessage = "評估案例已刪除。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRunAsync(CancellationToken cancellationToken)
    {
        try
        {
            ManagedCases = await evaluationService.GetManagedCasesAsync(cancellationToken);
            if (ManagedCases.Count == 0)
            {
                ErrorMessage = "目前沒有評估案例，請先在下方新增至少一個案例。";
                return Page();
            }

            Report = await evaluationService.EvaluateAsync(retrievalModes: null, topK: 5, cancellationToken: cancellationToken);
            return Page();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Retrieval evaluation failed.");
            ErrorMessage = $"評估執行失敗：{exception.Message}";
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

    public class CaseInput
    {
        public int? RetrievalEvaluationCaseId { get; set; }
        public string? Question { get; set; }
        public string? ExpectedCveIds { get; set; }
        public string? ExpectedDocumentTitles { get; set; }
        public string? ExpectedContentKeywords { get; set; }
        public string? ExpectedMetadata { get; set; }
        public string? Notes { get; set; }
    }
}
