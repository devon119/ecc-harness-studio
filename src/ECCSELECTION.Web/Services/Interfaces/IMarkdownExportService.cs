using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Web.Services.Interfaces;

public interface IMarkdownExportService
{
    string ExportToMarkdown(WizardResultViewModel result);
}
