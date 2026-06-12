using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Xunit;

namespace RagAgentConsole.Tests;

public class RagDomainTests
{
    private static readonly SecurityAdvisoryDomain SecurityDomain = new();
    private static readonly GenericKnowledgeDomain GenericDomain = new();
    private static readonly RagDomainRegistry Registry = new([SecurityDomain, GenericDomain]);

    // ── Registry resolution ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_CveAdvisoryModule_ReturnsSecurityDomain()
        => Assert.Same(SecurityDomain, Registry.Resolve(KnowledgeModuleNames.CveAdvisory));

    [Theory]
    [InlineData(KnowledgeModuleNames.WorkflowQa)]
    [InlineData(KnowledgeModuleNames.InternalDocs)]
    public void Resolve_DocumentModules_ReturnGenericDomain(string moduleName)
        => Assert.Same(GenericDomain, Registry.Resolve(moduleName));

    [Fact]
    public void Resolve_UnknownModule_ReturnsDefaultDomain()
        => Assert.Same(SecurityDomain, Registry.Resolve("SomethingElse"));

    [Fact]
    public void NormalizeModuleName_UnknownModule_FallsBackToDefaultModule()
        => Assert.Equal(KnowledgeModuleNames.CveAdvisory, Registry.NormalizeModuleName("SomethingElse"));

    [Fact]
    public void NormalizeModuleName_IgnoresCasing_ReturnsCanonicalName()
        => Assert.Equal(KnowledgeModuleNames.InternalDocs, Registry.NormalizeModuleName("internaldocs"));

    [Fact]
    public void ResolveForResult_AdvisoryResult_ReturnsSecurityDomain()
        => Assert.Same(SecurityDomain, Registry.ResolveForResult(AdvisoryResult()));

    [Fact]
    public void ResolveForResult_DocumentResult_ReturnsGenericDomain()
    {
        // Even documents filed under the CveAdvisory module carry no advisory
        // metadata, so they are formatted by the generic domain.
        var result = DocumentResult(moduleName: KnowledgeModuleNames.CveAdvisory);

        Assert.Same(GenericDomain, Registry.ResolveForResult(result));
    }

    // ── Context formatting ───────────────────────────────────────────────────

    [Fact]
    public void SecurityDomain_BuildContextBlock_IncludesCveMetadata()
    {
        var block = SecurityDomain.BuildContextBlock(AdvisoryResult());

        Assert.Contains("CVE: CVE-2024-3400", block);
        Assert.Contains("Severity: Critical", block);
        Assert.Contains("Known exploited: True", block);
        Assert.Contains("Context chunk: advisory chunk", block);
    }

    [Fact]
    public void GenericDomain_BuildContextBlock_OmitsCveMetadata()
    {
        var block = GenericDomain.BuildContextBlock(DocumentResult());

        Assert.DoesNotContain("CVE", block);
        Assert.DoesNotContain("Severity", block);
        Assert.DoesNotContain("Known exploited", block);
        Assert.Contains("Title: Onboarding policy", block);
        Assert.Contains("Context chunk: document chunk", block);
    }

    [Fact]
    public void SecurityDomain_BuildContextBlock_DocumentResult_FallsBackToGenericFormat()
    {
        var block = SecurityDomain.BuildContextBlock(DocumentResult(moduleName: KnowledgeModuleNames.CveAdvisory));

        Assert.DoesNotContain("Severity", block);
        Assert.Contains("Title: Onboarding policy", block);
    }

    // ── Trace metadata ───────────────────────────────────────────────────────

    [Fact]
    public void SecurityDomain_BuildTraceMetadata_ExposesCveFields()
    {
        var metadata = SecurityDomain.BuildTraceMetadata(AdvisoryResult());

        Assert.Equal("CVE-2024-3400", metadata[SecurityAdvisoryTraceKeys.CveId]);
        Assert.Equal("true", metadata[SecurityAdvisoryTraceKeys.KnownExploited]);
    }

    [Fact]
    public void GenericDomain_BuildTraceMetadata_HasNoCveFields()
    {
        var metadata = GenericDomain.BuildTraceMetadata(DocumentResult());

        Assert.False(metadata.ContainsKey(SecurityAdvisoryTraceKeys.CveId));
    }

    // ── Plan normalization ───────────────────────────────────────────────────

    [Fact]
    public void SecurityDomain_NormalizePlan_UppercasesCveId()
    {
        var plan = Plan(entities: new Dictionary<string, string?>
        {
            [SecurityAdvisoryPlanKeys.CveId] = "cve-2024-3400"
        });

        var normalized = SecurityDomain.NormalizePlan(plan, "question");

        Assert.Equal("CVE-2024-3400", normalized.GetEntity(SecurityAdvisoryPlanKeys.CveId));
    }

    [Fact]
    public void SecurityDomain_NormalizePlan_ExtractsCveIdFromQuestion()
    {
        var normalized = SecurityDomain.NormalizePlan(Plan(), "CVE-2023-4966 影響範圍是什麼");

        Assert.Equal("CVE-2023-4966", normalized.GetEntity(SecurityAdvisoryPlanKeys.CveId));
    }

    [Fact]
    public void SecurityDomain_NormalizePlan_DropsUnknownRiskFilter()
    {
        var plan = Plan(filters: new Dictionary<string, string?>
        {
            [SecurityAdvisoryPlanKeys.RiskFilter] = "none"
        });

        var normalized = SecurityDomain.NormalizePlan(plan, "question");

        Assert.Null(normalized.GetFilter(SecurityAdvisoryPlanKeys.RiskFilter));
    }

    [Fact]
    public void SecurityDomain_NormalizePlan_KeepsKnownRiskFilter()
    {
        var plan = Plan(filters: new Dictionary<string, string?>
        {
            [SecurityAdvisoryPlanKeys.RiskFilter] = "Known_Exploited"
        });

        var normalized = SecurityDomain.NormalizePlan(plan, "question");

        Assert.Equal("known_exploited", normalized.GetFilter(SecurityAdvisoryPlanKeys.RiskFilter));
    }

    [Fact]
    public void SecurityDomain_NormalizePlan_EmptyRetrievalQuery_PrefersCveId()
    {
        var normalized = SecurityDomain.NormalizePlan(
            Plan(retrievalQuery: string.Empty),
            "幫我查 CVE-2024-3400");

        Assert.Equal("CVE-2024-3400", normalized.RetrievalQuery);
    }

    // ── Document filter application ──────────────────────────────────────────

    [Fact]
    public void GenericDomain_AcceptsDocument_NoFilters_AcceptsEverything()
    {
        var request = Request();
        Assert.True(GenericDomain.AcceptsDocument(request, PolicyDocument(), "any chunk"));
    }

    [Fact]
    public void GenericDomain_AcceptsDocument_FilterValueInTags_Accepts()
    {
        var request = Request(filters: new Dictionary<string, string?> { ["policyCategory"] = "leave" });
        Assert.True(GenericDomain.AcceptsDocument(request, PolicyDocument(tags: "leave;hr"), "chunk"));
    }

    [Fact]
    public void GenericDomain_AcceptsDocument_FilterValueInChunk_Accepts()
    {
        var request = Request(filters: new Dictionary<string, string?> { ["region"] = "Taiwan" });
        Assert.True(GenericDomain.AcceptsDocument(request, PolicyDocument(), "annual leave rules for Taiwan employees"));
    }

    [Fact]
    public void GenericDomain_AcceptsDocument_FilterValueAbsent_Rejects()
    {
        var request = Request(filters: new Dictionary<string, string?> { ["policyCategory"] = "expense" });
        Assert.False(GenericDomain.AcceptsDocument(request, PolicyDocument(tags: "leave;hr"), "annual leave policy"));
    }

    [Fact]
    public void GenericDomain_AcceptsDocument_AdvisoryFilters_AreIgnored()
    {
        var request = Request(filters: new Dictionary<string, string?>
        {
            [SecurityAdvisoryPlanKeys.RiskFilter] = "none",
            [SecurityAdvisoryPlanKeys.CveYear] = "2026"
        });

        Assert.True(GenericDomain.AcceptsDocument(request, PolicyDocument(), "annual leave policy"));
    }

    [Fact]
    public void SecurityDomain_AcceptsDocument_IgnoresAdvisoryFilters()
    {
        var request = Request(filters: new Dictionary<string, string?>
        {
            [SecurityAdvisoryPlanKeys.RiskFilter] = "critical",
            [SecurityAdvisoryPlanKeys.CveYear] = "2024"
        });

        Assert.True(SecurityDomain.AcceptsDocument(request, PolicyDocument(), "chunk"));
    }

    // ── Filter parsing ───────────────────────────────────────────────────────

    [Fact]
    public void SecurityAdvisoryFilter_ParsesRiskAndYearFromRequest()
    {
        var request = new RetrievalRequest(
            "q",
            [],
            new Dictionary<string, string?> { [SecurityAdvisoryPlanKeys.CveId] = "CVE-2024-3400" },
            new Dictionary<string, string?>
            {
                [SecurityAdvisoryPlanKeys.RiskFilter] = "critical",
                [SecurityAdvisoryPlanKeys.CveYear] = "2024"
            },
            QueryEmbedding: [],
            MaxResults: 5);

        var filter = SecurityAdvisoryFilter.From(request);

        Assert.Equal("CVE-2024-3400", filter.CveId);
        Assert.False(filter.KevOnly);
        Assert.True(filter.HighRiskOnly);
        Assert.Equal(2024, filter.CveYear);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RetrievalPlan Plan(
        string retrievalQuery = "query",
        IReadOnlyDictionary<string, string?>? entities = null,
        IReadOnlyDictionary<string, string?>? filters = null)
        => new(
            "question",
            retrievalQuery,
            "knowledge_lookup",
            [],
            [],
            entities ?? RetrievalPlan.EmptyValues,
            filters ?? RetrievalPlan.EmptyValues);

    private static RetrievalRequest Request(IReadOnlyDictionary<string, string?>? filters = null)
        => new(
            "q",
            [],
            RetrievalPlan.EmptyValues,
            filters ?? RetrievalPlan.EmptyValues,
            QueryEmbedding: [],
            MaxResults: 5,
            ModuleName: KnowledgeModuleNames.InternalDocs);

    private static KnowledgeDocument PolicyDocument(string? tags = null)
        => new()
        {
            ModuleName = KnowledgeModuleNames.InternalDocs,
            Title = "Annual leave policy",
            SourceType = "Upload",
            Tags = tags,
            ExtractedText = "text",
            ContentHash = "hash"
        };

    private static RetrievalResult AdvisoryResult()
        => new(
            Advisory: new SecurityAdvisory
            {
                SourceName = "CISA KEV",
                ExternalId = "CVE-2024-3400",
                CveId = "CVE-2024-3400",
                Title = "PAN-OS command injection",
                Description = "test",
                Vendor = "Palo Alto",
                Product = "PAN-OS",
                Severity = "Critical",
                CvssScore = 10.0m,
                IsKnownExploited = true,
                SourceUrl = "https://example.com",
                ContentHash = "hash"
            },
            Document: null,
            ChunkText: "advisory chunk",
            Score: 1.0,
            VectorScore: 0.5,
            TextScore: 0.5);

    private static RetrievalResult DocumentResult(string moduleName = KnowledgeModuleNames.InternalDocs)
        => new(
            Advisory: null,
            Document: new KnowledgeDocument
            {
                ModuleName = moduleName,
                Title = "Onboarding policy",
                SourceType = "Upload",
                ExtractedText = "text",
                ContentHash = "hash"
            },
            ChunkText: "document chunk",
            Score: 1.0,
            VectorScore: 0.5,
            TextScore: 0.5);
}
