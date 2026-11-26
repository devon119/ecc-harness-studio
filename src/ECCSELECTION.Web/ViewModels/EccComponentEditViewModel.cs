using System.ComponentModel.DataAnnotations;
using ECCSELECTION.Web.Models.Enums;

namespace ECCSELECTION.Web.ViewModels;

public class EccComponentEditViewModel
{
    public int Id { get; set; }
    public ComponentType ComponentType { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string RawUrl { get; set; } = string.Empty;
    public string HtmlUrl { get; set; } = string.Empty;
    public string DescriptionEn { get; set; } = string.Empty;

    [Display(Name = "繁體中文摘要")]
    [MaxLength(2000)]
    public string SummaryZh { get; set; } = string.Empty;

    [Display(Name = "SDLC 角色（逗號分隔）")]
    [MaxLength(500)]
    public string SdlcRoles { get; set; } = string.Empty;

    [Display(Name = "技術棧標籤（逗號分隔）")]
    [MaxLength(500)]
    public string TechStacks { get; set; } = string.Empty;

    [Display(Name = "架構風格標籤（逗號分隔）")]
    [MaxLength(500)]
    public string ArchStyles { get; set; } = string.Empty;

    [Display(Name = "使用方式與觸發時機")]
    [MaxLength(2000)]
    public string UsageGuide { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public bool IsManuallyEdited { get; set; }
    public DateTime LastSyncedAt { get; set; }
}
