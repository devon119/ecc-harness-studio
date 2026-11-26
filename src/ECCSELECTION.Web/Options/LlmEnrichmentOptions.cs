namespace ECCSELECTION.Web.Options;

public class LlmEnrichmentOptions
{
    public const string SectionName = "LlmEnrichment";

    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}
