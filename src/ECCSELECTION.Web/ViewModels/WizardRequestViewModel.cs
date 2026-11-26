using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ECCSELECTION.Web.ViewModels;

public class WizardRequestViewModel
{
    [Required(ErrorMessage = "請選擇專案型態")]
    public string ProjectType { get; set; } = "Web";

    [Required(ErrorMessage = "請選擇專案導入階段")]
    public string ProjectLifecycle { get; set; } = "Greenfield";

    [Required(ErrorMessage = "請選擇架構風格")]
    public string ArchStyle { get; set; } = "MVC";

    public List<string> TechStacks { get; set; } = new() { "dotnet" };

    public List<string> SdlcRoles { get; set; } = new() { "SA", "SD", "PG_BE", "PG_DB", "PG_TEST", "QA" };

    [Required(ErrorMessage = "請選擇推薦模式")]
    public string Mode { get; set; } = "Compact";

    [StringLength(4000, ErrorMessage = "專案用途說明最多 4000 字元")]
    public string? ProjectPurpose { get; set; }

    public IFormFile? RequirementDocument { get; set; }

    public string? UploadedDocumentName { get; set; }

    public List<string> RequirementKeywords { get; set; } = new();

    public HarnessAssemblyOptionsViewModel AssemblyOptions { get; set; } = new();
}
