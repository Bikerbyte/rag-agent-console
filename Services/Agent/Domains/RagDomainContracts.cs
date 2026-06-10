using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

/// <summary>
/// Pluggable knowledge domain. The generic RAG pipeline (planner, hybrid
/// retrieval, answer generation, trace) stays domain-neutral and delegates
/// domain-specific behavior — plan normalization, context formatting, and
/// trace metadata — to the active domain.
/// </summary>
public interface IRagDomain
{
    string Name { get; }

    /// <summary>Module the planner falls back to for this domain.</summary>
    string DefaultModuleName { get; }

    /// <summary>Knowledge modules routed to this domain.</summary>
    IReadOnlyList<string> ModuleNames { get; }

    /// <summary>True when this domain should format/annotate the result.</summary>
    bool Owns(RetrievalResult result);

    /// <summary>
    /// Deterministic, rule-guarded normalization and enrichment of a planner
    /// output (regex extraction, enum validation, casing). Must not throw for
    /// ordinary planner shortcomings.
    /// </summary>
    RetrievalPlan NormalizePlan(RetrievalPlan plan, string question);

    /// <summary>Context block sent to the LLM for one retrieval result.</summary>
    string BuildContextBlock(RetrievalResult result);

    /// <summary>Plain-text summary block used when AI generation is unavailable.</summary>
    string BuildPlainSummaryBlock(RetrievalResult result);

    /// <summary>Domain metadata attached to a retrieval trace match.</summary>
    IReadOnlyDictionary<string, string?> BuildTraceMetadata(RetrievalResult result);
}

public interface IRagDomainRegistry
{
    /// <summary>Domain used when no module or an unknown module is requested.</summary>
    IRagDomain DefaultDomain { get; }

    /// <summary>Resolve the domain that handles the given knowledge module.</summary>
    IRagDomain Resolve(string? moduleName);

    /// <summary>Resolve the domain that owns a concrete retrieval result.</summary>
    IRagDomain ResolveForResult(RetrievalResult result);

    /// <summary>Map a planner-provided module name onto a known module.</summary>
    string NormalizeModuleName(string? moduleName);

    IReadOnlyList<IRagDomain> ListDomains();
}
