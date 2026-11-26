using System.Text;
using ECCSELECTION.Web.Services.Interfaces;
using ECCSELECTION.Web.ViewModels;

namespace ECCSELECTION.Web.Services;

public class MarkdownExportService : IMarkdownExportService
{
    public string ExportToMarkdown(WizardResultViewModel result)
    {
        var sb = new StringBuilder();
        var req = result.Request;

        sb.AppendLine("# ECC Harness 推薦元件清單");
        sb.AppendLine();
        sb.AppendLine($"- **產生時間 (UTC)**：{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- **專案型態**：{req.ProjectType}");
        sb.AppendLine($"- **導入階段**：{req.ProjectLifecycle}");
        sb.AppendLine($"- **架構風格**：{req.ArchStyle}");
        sb.AppendLine($"- **技術棧**：{(req.TechStacks.Count > 0 ? string.Join(", ", req.TechStacks) : "無指定")}");
        sb.AppendLine($"- **目標 SDLC 角色**：{(req.SdlcRoles.Count > 0 ? string.Join(", ", req.SdlcRoles) : "無指定")}");
        sb.AppendLine($"- **推薦模式**：{req.Mode}");
        sb.AppendLine($"- **總推薦項目數**：{result.TotalRecommendedCount}");
        sb.AppendLine();

        if (result.Groups.Count == 0 || result.TotalRecommendedCount == 0)
        {
            sb.AppendLine("> 目前條件下尚無符合的元件，請確認知識庫已完成 GitHub 同步或放寬篩選條件。");
            return sb.ToString();
        }

        foreach (var group in result.Groups)
        {
            sb.AppendLine($"## {group.RoleDisplayName}");
            sb.AppendLine();

            if (group.Items.Count == 0)
            {
                sb.AppendLine("_此角色目前無符合門檻之推薦元件。_");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("| 角色 | 類型 | 名稱 | 分數 | 推薦理由（為何被選中） | 使用方式與觸發時機 | GitHub Raw 下載與本地建議路徑 |");
            sb.AppendLine("| :--- | :--- | :--- | :---: | :--- | :--- | :--- |");

            foreach (var item in group.Items)
            {
                var reasons = EscapeCell(string.Join("；", item.MatchReasons));
                var usage = EscapeCell(string.IsNullOrWhiteSpace(item.UsageGuide) ? item.SummaryZh : item.UsageGuide);
                var linkAndPath = $"[Raw 下載]({item.RawUrl})<br>`{EscapeCell(item.LocalSuggestedPath)}`";

                sb.AppendLine(
                    $"| {EscapeCell(item.RoleDisplayName)} | {item.ComponentType} | `{EscapeCell(item.Name)}` | {item.Score} | {reasons} | {usage} | {linkAndPath} |");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string EscapeCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("|", "\\|")
            .Trim();
    }
}
