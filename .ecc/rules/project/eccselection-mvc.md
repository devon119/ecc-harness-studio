---
paths:
  - "src/**/*.cs"
  - "src/**/*.cshtml"
  - "tests/**/*.cs"
---
# ECCSELECTION 專案專屬 MVC 與 SQLite 開發規範

本檔案為 `D:\T01_GIT\ECCSELECTION` 的最高專案規則，優先於 `.ecc/` 內其他通用語言範本。

## 1. 專案結構與技術棧
- **方案檔**：`ECCSELECTION.slnx`
- **Web 專案**：`src/ECCSELECTION.Web`（ASP.NET Core MVC, `.NET 10`, EF Core + SQLite）
- **測試專案**：`tests/ECCSELECTION.Tests`（xUnit + SQLite `:memory:`）
- **嚴禁**未經同意改為 SPA 前後端分離（React/Vue）或替換為其他 ORM。

## 2. 核心資料與同步契約
- **`EccComponents.Path` 唯一索引**：所有同步邏輯必須以 `Path` 為唯一鍵、`Sha` 為變更比對依據。
- **人工編輯保護**：`IsManuallyEdited == true` 的元件在 GitHub 增量同步時，不得覆蓋其 `SummaryZh`、`SdlcRoles`、`TechStacks`、`ArchStyles` 與 `UsageGuide`。
- **非阻塞降級**：未設定 `LlmEnrichment:ApiKey` 時，必須安全降級為 YAML Frontmatter + 確定性規則分類，不得阻塞同步流程。
- **軟刪除**：GitHub 上已移除的檔案將 `IsActive` 設為 `false`，保留歷史紀錄。

## 3. 錯誤處理與品質鐵律
- **禁止靜默吞錯**：嚴禁空的 `catch` 區塊；外部 HTTP（GitHub / LLM）失敗必須記錄日誌並寫入 `SyncLogs.ErrorMessage`。
- **確定性配對**：問券推薦（`RecommendationService`）必須採可預期之規則計分與衝突技術棧排除，不依賴執行期 LLM。

## 4. 建置與驗證指令（取代通用範本中的 npm / pytest）
- 編譯：`dotnet build`
- 測試：`dotnet test --verbosity normal`
- 啟動本機 Web：`dotnet run --project src/ECCSELECTION.Web`
