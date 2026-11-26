# ECCSELECTION — ECC 知識庫與 Harness 選型系統

本地可運行之 ASP.NET Core MVC (`.NET 10`) + SQLite 知識庫系統，可從 GitHub `affaan-m/ECC` 增量同步 `agents`、`skills`、`rules` 與 `commands`，並透過互動式 SDLC 問券產出可解釋的 Harness 推薦清單與 Markdown 匯出文件。

## 執行方式

```powershell
cd D:\T01_GIT\ECCSELECTION
dotnet build
dotnet test
dotnet run --project src\ECCSELECTION.Web\ECCSELECTION.Web.csproj
```

## 可選設定（建議使用 User Secrets）

- `GitHubSync:PersonalAccessToken`：可選，避免匿名 GitHub API 每小時 60 次限流。
- `LlmEnrichment:Enabled` / `Endpoint` / `ApiKey` / `Model`：可選，未設定時自動使用確定性規則式繁體中文摘要。
