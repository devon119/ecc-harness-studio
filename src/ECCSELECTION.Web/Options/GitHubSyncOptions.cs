namespace ECCSELECTION.Web.Options;

public class GitHubSyncOptions
{
    public const string SectionName = "GitHubSync";

    public string Owner { get; set; } = "affaan-m";
    public string Repo { get; set; } = "ECC";
    public string Branch { get; set; } = "main";
    public string PersonalAccessToken { get; set; } = string.Empty;
}
