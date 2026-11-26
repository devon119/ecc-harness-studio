# ECCSELECTION 專案專屬 Harness 設定 (`.ecc/`)

本目錄包含從 `affaan-m/ECC` 精選並針對 `ECCSELECTION`（ASP.NET Core MVC .NET 10 + SQLite）調整後的 Harness 元件：

- `rules/project/eccselection-mvc.md`：**最高專案規則**（鎖定方案結構、同步契約與 `dotnet` 指令）。
- `agents/`：精選 13 個角色 Agent，其中 `csharp-reviewer`、`database-reviewer`、`tdd-guide`、`e2e-runner`、`build-error-resolver` 已加上本專案專屬檢查清單與指令。
- `skills/`：精選 16 個核心 Skill（含 `dotnet-patterns`、`csharp-testing`、`database-migrations`、`api-connector-builder` 等）。
- `rules/`：`common/`、`csharp/`、`web/` 三組基礎規則。
- `commands/`：精選 9 個流程指令（`plan-prd`, `plan`, `multi-plan`, `multi-backend`, `build-fix`, `test-coverage`, `code-review`, `security-scan`, `quality-gate`）。
