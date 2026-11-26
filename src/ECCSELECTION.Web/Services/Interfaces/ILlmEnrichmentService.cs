namespace ECCSELECTION.Web.Services.Interfaces;

public interface ILlmEnrichmentService
{
    Task<string?> EnrichSummaryZhAsync(ParsedEccComponent parsed, string rawContent, CancellationToken cancellationToken = default);
}
