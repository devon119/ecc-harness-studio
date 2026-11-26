using ECCSELECTION.Web.Models.Enums;

namespace ECCSELECTION.Web.Services.Interfaces;

public record ParsedEccComponent(
    ComponentType ComponentType,
    string Name,
    string Path,
    string DescriptionEn,
    string SummaryZh,
    string SdlcRoles,
    string TechStacks,
    string ArchStyles,
    string UsageGuide);

public interface IEccParserService
{
    ParsedEccComponent Parse(ComponentType componentType, string path, string rawContent);
}
