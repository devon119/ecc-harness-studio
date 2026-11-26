using ECCSELECTION.Web.Models;
using ECCSELECTION.Web.Models.Enums;

namespace ECCSELECTION.Web.ViewModels;

public class EccCatalogFilterViewModel
{
    public string? Keyword { get; set; }
    public ComponentType? ComponentType { get; set; }
    public string? SdlcRole { get; set; }
    public string? TechStack { get; set; }
    public bool IncludeInactive { get; set; }

    public List<EccComponent> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int ActiveCount { get; set; }
}
