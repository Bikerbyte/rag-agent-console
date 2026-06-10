using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

/// <summary>
/// Resolves knowledge domains by module name or by retrieval result.
/// The first registered domain acts as the default for unknown modules,
/// which keeps the built-in demo behavior (unknown module → CveAdvisory).
/// </summary>
public sealed class RagDomainRegistry : IRagDomainRegistry
{
    private readonly IReadOnlyList<IRagDomain> domains;
    private readonly Dictionary<string, IRagDomain> domainsByModule;

    public RagDomainRegistry(IEnumerable<IRagDomain> registeredDomains)
    {
        domains = registeredDomains.ToList();
        if (domains.Count == 0)
        {
            throw new InvalidOperationException("At least one RAG domain must be registered.");
        }

        domainsByModule = new Dictionary<string, IRagDomain>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in domains)
        {
            foreach (var moduleName in domain.ModuleNames)
            {
                domainsByModule.TryAdd(moduleName, domain);
            }
        }
    }

    public IRagDomain DefaultDomain => domains[0];

    public IRagDomain Resolve(string? moduleName)
    {
        var normalized = moduleName?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) &&
            domainsByModule.TryGetValue(normalized, out var domain))
        {
            return domain;
        }

        return DefaultDomain;
    }

    public IRagDomain ResolveForResult(RetrievalResult result)
        => domains.FirstOrDefault(domain => domain.Owns(result)) ?? DefaultDomain;

    public string NormalizeModuleName(string? moduleName)
    {
        var normalized = moduleName?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized) &&
            domainsByModule.TryGetValue(normalized, out var domain))
        {
            // Return the canonical casing declared by the domain.
            return domain.ModuleNames.First(name =>
                string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase));
        }

        return DefaultDomain.DefaultModuleName;
    }

    public IReadOnlyList<IRagDomain> ListDomains() => domains;
}
