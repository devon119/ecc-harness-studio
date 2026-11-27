# SPEC_20261127_FULL_AUDIT_REMEDIATION — 全專案深度稽核修復規格書（Phase 1–3）

- **文件編號**：`SPEC_20261127_FULL_AUDIT_REMEDIATION`
- **建立日期**：2026-11-27
- **撰寫角色**：SA / 規格 Agent
- **目標專案**：`ECCSELECTION`（`ECCSELECTION.slnx`）
- **狀態**：已核准並完成實作與驗證（2026-11-27）

---

## 1. 背景與目的

2026-11-27 全專案深度稽核（涵蓋 `src/ECCSELECTION.Web`、`tools/ECCSELECTION.Mcp`、`docs/` 與 `gh-pages` 雙版本靜態站、`tests/ECCSELECTION.Tests` 及專案文件）共識別出 **12 項待修復問題**。

本規格書依風險與影響程度分為 **3 個階段（Phase 1–3）**，明確定義每項問題的根因、目標檔案、設計規格、寫入邊界與驗收條件，供後續 Backend、Frontend/MVC、MCP/Tools、Test 與 Docs Agent 分層實作與驗證。

---

## 2. 雙版本架構與稽核總覽

| 階段 | 優先級 | 涵蓋項目 | 負責寫入邊界 |
| :--- | :--- | :--- | :--- |
| **Phase 1** | **High**（資料正確性與雙版本一致） | 1. `CatalogExportService.cs` `ComponentType` 型別讀取修復<br>2. `main:docs/` 與 `gh-pages` v1.6 3D UI 一致性修復 | `tools/ECCSELECTION.Mcp/`、`docs/`（非 `docs/specs/`） |
| **Phase 2** | **Medium**（後端韌性、安全性與 MVC 分層） | 3. `WizardController.cs` 瘦身與消除重複 `RecommendAsync`<br>4. `GitHubSyncService.cs` 錯誤記錄與超時降級<br>5. 統一 LLM Helper（`LlmClientHelper`）與原生 Gemini 支援<br>6. `HarnessBundleService.cs` 路徑清理、Token 網域檢查與方法拆解<br>7. `RecommendationService.cs` `AdditionalCandidates` 排序統一 | `src/ECCSELECTION.Web/Services/`、`Controllers/` |
| **Phase 3** | **Low**（UI 表單、MCP 掃描、測試覆蓋與文件同步） | 8. `Wizard/Result.cshtml` `ExportMarkdown` 表單補齊隱藏欄位<br>9. `UiContractCheckerService.cs` 支援 `[AutoValidateAntiforgeryToken]` 與多行屬性<br>10. `tests/ECCSELECTION.Tests/` 補齊缺失單元測試<br>11–12. 文件同步（`SPEC_20261126_ECCSELECTION_MCP.md`、`VALIDATION_REPORT_ECCSELECTION.md`、`README.md`、`DEVELOPMENT_STATUS.md`） | `Views/`、`tools/ECCSELECTION.Mcp/`、`tests/`、專案文件 |

---

## 3. Phase 1（High — 資料正確性與雙版本一致）

### 3.1 `CatalogExportService.cs`：`ComponentType` 以字串讀取並安全解析

- **問題根因**：
  - `AppDbContext.OnModelCreating`（`src/ECCSELECTION.Web/Data/AppDbContext.cs` 第 26–29 行）設定 `EccComponent.ComponentType` 使用 `.HasConversion<string>()`，SQLite `EccComponents.ComponentType` 欄位實際儲存 `"Agent"`、`"Skill"`、`"Rule"`、`"Command"` 等字串。
  - 然而 `tools/ECCSELECTION.Mcp/Services/CatalogExportService.cs` 第 185 行以 `reader.GetInt32(1)` 讀取該欄位，再呼叫 `MapComponentType(int)`，導致執行 `export_catalog_json` 或 `sync_and_publish_lite(includeCatalogJson: true)` 時於真實資料庫發生 `InvalidCastException` / 格式錯誤。
- **規格要求**：
  1. 於 `CatalogExportService.ReadCatalogAsync` 改用 `string rawType = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);`（或先 `GetValue(1)` / `GetFieldValue<string>` 並相容字串讀取）。
  2. 新增或改寫映射 helper（例如 `NormalizeComponentType(string rawType)`）：
     - 使用 `Enum.TryParse<ComponentTypeName>`（或明確白名單 `Enum.TryParse` / `switch` 比對 `"Agent"`, `"Skill"`, `"Rule"`, `"Command"`，忽略大小寫）回傳標準 PascalCase 字串。
     - 若遇舊版整數字串（如 `"0"`–`"3"`）或未知值，安全降級為對應名稱或 `"Unknown"`，不得拋出未預期例外。
  3. 移除僅接受 `int` 的舊 `MapComponentType(int componentType)`，確保 `BuildCountsByType` 與既有測試相容。
- **驗收條件**：
  - 針對 `ComponentType` 儲存為 `"Agent"` / `"Skill"` / `"Rule"` / `"Command"`（`TEXT`）的 SQLite 資料庫呼叫 `ExportAsync` 時，`TotalExported` 與 `CountsByType` 完全正確。

### 3.2 `main:docs/` 與 `gh-pages` 雙版本靜態 UI 一致性修復

- **現況查核結果**：
  - `gh-pages` 分支目前已部署 **v1.6 3D 空間視覺化靜態版**（`index.html` 引用 `css/icon3d.css`、`app.js` 內建 `renderIcon3D()` 且**零 `<table>` 標籤**、`data/catalog.json`、`downloads/eccselection-full.zip`）。
  - 但 `main` 分支上的 `docs/index.html` 與 `docs/app.js` 仍停留於舊版非 3D UI：`docs/index.html` 未引用 `css/icon3d.css`，且 `docs/app.js` 第 412 行與第 436 行仍殘留 `<table class="table ...">` 語法。
  - `PagesSyncService.cs`（`tools/ECCSELECTION.Mcp/Services/PagesSyncService.cs`）目前僅同步 `downloads/eccselection-full.zip`、`css/icon3d.css`（取自 `main:src/ECCSELECTION.Web/wwwroot/css/icon3d.css`）及選填的 `data/catalog.json` 至 `gh-pages`，並未同步 `docs/index.html` 與 `docs/app.js`。
- **規格要求**：
  1. **更新 `main:docs/` 至 v1.6 3D UI**：
     - 同步 `gh-pages` 現有之 v1.6 3D `index.html` 與 `app.js` 至 `main` 分支的 `docs/index.html` 與 `docs/app.js`，並將 `src/ECCSELECTION.Web/wwwroot/css/icon3d.css` 同步一份至 `docs/css/icon3d.css`（及必要時共用或內嵌 `icon3d.js` / `renderIcon3D()`），確保 `main:docs/` 為**零 `<table>`** 之 v1.6 3D UI，與線上 `gh-pages` 完全一致。
  2. **`PagesSyncService.cs` 同步策略確認**：
     - 維持 `PagesSyncService.SyncWorktreeAssetsAsync` 的核心邊界（打包 `main` 為 `downloads/eccselection-full.zip`、同步 `css/icon3d.css` 及選填 `data/catalog.json` 至 `gh-pages` worktree），不得在未經確認下覆蓋 `gh-pages` 的部署結構；若未來需從 `main:docs/` 同步靜態頁面，應透過明確參數開關控制，本階段以「`main:docs/` 對齊 `gh-pages` v1.6 3D UI」為主。
- **驗收條件**：
  - `docs/index.html` 正確引用 `css/icon3d.css`（及 3D Icon 渲染邏輯）。
  - `docs/index.html` 與 `docs/app.js` 靜態掃描**零 `<table>` 標籤**，與 `gh-pages` 功能與視覺版面一致。

---

## 4. Phase 2（Medium — 後端韌性、安全性與 MVC 分層）

### 4.1 `WizardController.cs` 瘦身與消除重複 `RecommendAsync` 呼叫

- **問題根因**：
  - `WizardController.SaveGeminiKey`（第 46–78 行）直接在 Controller 內執行檔案 I/O（組合 `appsettings.Development.local.json` 路徑、序列化 JSON 並呼叫 `System.IO.File.WriteAllTextAsync`），違反 MVC 分層規範。
  - `WizardController.ApplySuggestedAssemblyDefaults`（第 223–260 行）包含大量組建預設值推斷與覆寫邏輯，屬於商業規則。
  - `WizardController.ApplyGeminiCuration`（第 147–170 行）連續呼叫了兩次 `_recommendationService.RecommendAsync(request, cancellationToken)`（第 158 行與第 161 行），造成重複資料庫查詢與計算浪費。
- **規格要求**：
  1. 將 `SaveGeminiKey` 的設定檔寫入邏輯移至 `Services/`（例如 `IGeminiCurationService` / `GeminiCurationService` 或專用 `LocalLlmConfigService`），由 Service 負責驗證參數、組裝設定物件並寫入 `appsettings.Development.local.json`。
  2. 將 `ApplySuggestedAssemblyDefaults` 移至 `Services/`（例如 `RequirementAnalyzerService` 或 `RecommendationService` 公開方法），Controller 僅傳入 `request` 與 `analysis` 呼叫。
  3. 重構 `ApplyGeminiCuration` 的精選流程：只呼叫**一次** `_recommendationService.RecommendAsync(request, cancellationToken)` 取得 `baseline`，再由 `_geminiCurationService` 依 `geminiResponseJson` 套用精選結果與產生 `ChromeGeminiPrompt`，消除重複運算。
- **驗收條件**：
  - `WizardController` 不直接引用 `System.IO.File` 寫入設定檔，且不包含組建命令推斷商業規則。
  - `ApplyGeminiCuration` 單次請求僅執行一次 `RecommendAsync`。

### 4.2 `GitHubSyncService.cs` 錯誤記錄韌性與單檔超時降級

- **問題根因**：
  - 當同步過程發生例外進入 `catch` 區塊時，若 `cancellationToken` 已取消或 `DbContext` 追蹤器仍保留部分失敗實體，直接使用原 `cancellationToken` 呼叫 `SaveChangesAsync` 會導致 `Failed` 狀態的 `SyncLog` 無法寫入資料庫。
  - `TryFetchRawContentAsync` 未區分並捕捉單檔請求超時（`TaskCanceledException` / `OperationCanceledException` 在非外部取消時），可能導致單一檔案超時即中斷整批同步，而非降級為 `PartialSuccess`。
- **規格要求**：
  1. 於 `GitHubSyncService` 主同步流程的 `catch` 區塊中：
     - 先執行 `_dbContext.ChangeTracker.Clear()` 清除髒實體狀態，再建立並寫入 `Status = SyncStatus.Failed` 之 `SyncLog`（記錄完整 `ErrorMessage`）。
     - 寫入 `Failed` `SyncLog` 時使用 `CancellationToken.None`，確保即使上游請求已超時或取消，仍可確實留存失敗稽核紀錄。
  2. 於 `TryFetchRawContentAsync` 中：
     - 若外部 `cancellationToken.IsCancellationRequested` 為 `true`，正常向上拋出取消；
     - 若為 `HttpClient` 單次請求超時引發之 `TaskCanceledException`（或 `HttpRequestException`），記錄 Warning/Error 日誌並將該檔案錯誤納入 `SyncLog.ErrorMessage` 與失敗計數，讓整體同步狀態得以正確降級為 `PartialSuccess`（部分成功）而非整批崩潰。
- **驗收條件**：
  - 模擬同步中斷或單檔逾時時，資料庫仍可成功寫入 `SyncLog`（`Failed` 或 `PartialSuccess`）且 `ErrorMessage` 非空。

### 4.3 統一 LLM Helper（`LlmClientHelper`）與原生 Gemini API 支援

- **問題根因**：
  - `LlmEnrichmentService`、`RequirementAnalyzerService` 與 `GeminiCurationService` 各自重複實作 `StripCodeFence` 與 HTTP 請求組裝邏輯。
  - `LlmEnrichmentService` 與 `RequirementAnalyzerService` 尚未支援原生 Google Gemini API 格式（偵測 `generativelanguage.googleapis.com`、使用 `x-goog-api-key` 標頭與 `:generateContent` 端點），僅支援 OpenAI 相容格式。
  - 當 `LlmEnrichment:Endpoint` 設定值格式無效時，建構 `Uri` 可能拋出未處理的 `UriFormatException`。
- **規格要求**：
  1. 於 `src/ECCSELECTION.Web/Services/` 建立共用的 `LlmClientHelper`（靜態類別或可注入 Helper 服務）：
     - 集中實作 `StripCodeFence(string raw)`（移除 ````json` / ```` 等 Markdown Code Fence）。
     - 集中處理 OpenAI 相容端點（`/chat/completions` + `Bearer`）與原生 Gemini 端點（偵測 `generativelanguage.googleapis.com`、組裝 `:generateContent` URL 並使用 `x-goog-api-key` 標頭）的請求組裝與回應文字萃取。
     - 安全解析 `Endpoint` 字串：若發生 `UriFormatException` 或非 `http/https` Scheme，記錄日誌並回傳失敗結果（不拋出未處理例外），讓呼叫端非阻塞降級為規則式邏輯。
  2. 重構 `LlmEnrichmentService`、`RequirementAnalyzerService` 與 `GeminiCurationService` 一律改用 `LlmClientHelper`，移除各自重複的 `StripCodeFence` 與 HTTP 組裝程式碼。
- **驗收條件**：
  - 3 個 LLM 服務皆可透過原生 Gemini `Endpoint` 與 `ApiKey` 正常運作，且無重複 `StripCodeFence` 實作。
  - 設定無效 `Endpoint`（如 `"not-a-valid-uri"`）時，系統記錄日誌並安全降級為規則式分析／分類，不拋出 `UriFormatException`。

### 4.4 `HarnessBundleService.cs` 路徑清理、Token 網域檢查與長方法拆解

- **問題根因**：
  - `BuildSafeLocalPath` 未對 `component.Name` 進行路徑分隔字元清理（`/` 與 `\`），若元件名稱含路徑字元可能產生非預期子目錄或路徑穿越風險。
  - 下載遠端元件原始檔時，未驗證 `RawUrl` 之 Host 是否為 `raw.githubusercontent.com` 即附帶 `PersonalAccessToken`，若資料被竄改為第三方網域將有 Token 外洩風險。
  - `HarnessBundleService.BuildBundleZipAsync` 與 `RecommendationService.BuildAgentSuites` 方法過長，職責耦合度高。
- **規格要求**：
  1. `HarnessBundleService.BuildSafeLocalPath`：
     - 對 `component.Name` 明確清理 `/`、`\` 及非法檔名字元（替換為 `-` 或安全字元），並確保最終壓縮包內路徑為相對安全路徑（不含 `..`）。
  2. `RawUrl` Host 白名單驗證：
     - 僅當 `Uri.TryCreate(component.RawUrl, UriKind.Absolute, out var uri)` 成功且 `uri.Scheme == Uri.UriSchemeHttps` 且 `string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)` 時，才可附加 GitHub `PersonalAccessToken` Authorization Header；否則不得附帶 Token（或拒絕非預期網域請求並記錄日誌）。
  3. 長方法拆解（重構不改變外部契約）：
     - 將 `HarnessBundleService.BuildBundleZipAsync` 拆分為職責單一的小型私有 Helper（例如：組裝清單與規則檔寫入、遠端元件抓取與快取、Guardrails 範本寫入）。
     - 將 `RecommendationService.BuildAgentSuites` 拆分為小型私有 Helper（例如：Lead Agent 篩選、套件成員配對與上限收斂、套件摘要組裝）。
- **驗收條件**：
  - 含 `../` 或 `/`、`\` 的 `component.Name` 無法在 ZIP 內產生路徑穿越。
  - 非 `raw.githubusercontent.com` 的 `RawUrl` 絕對不會收到 `PersonalAccessToken`。

### 4.5 `RecommendationService.cs` 統一 `AdditionalCandidates` 排序規則

- **問題根因**：
  - `Groups` 依據 `GetTypePriority`（`Agent` → `Skill` → `Rule` → `Command`）排序，但 `AdditionalCandidates` 的排序未統一套用 `GetTypePriority`，導致額外候選清單的元件類型順序與主要套組不一致。
- **規格要求**：
  - 於 `RecommendationService` 將 `AdditionalCandidates` 改為優先依 `GetTypePriority(c.ComponentType)` 升冪排序，再依相關度分數降冪與名稱升冪排序，與 `Groups` 保持一致。
- **驗收條件**：
  - `AdditionalCandidates` 輸出順序嚴格遵循 `Agent` → `Skill` → `Rule` → `Command`，同類型內依分數由高到低排列。

---

## 5. Phase 3（Low — UI 表單、MCP 掃描、測試覆蓋與文件同步）

### 5.1 `Wizard/Result.cshtml` `ExportMarkdown` 表單補齊隱藏欄位

- **問題根因**：
  - `src/ECCSELECTION.Web/Views/Wizard/Result.cshtml` 第 30–45 行的 `ExportMarkdown` POST 表單僅傳送 `ProjectType`、`ProjectLifecycle`、`ArchStyle`、`Mode`、`TechStacks` 與 `SdlcRoles`，遺漏了 `ProjectPurpose`、`RequirementKeywords` 與 `AssemblyOptions`（`ProjectName`、`TargetEnvironment`、`BuildCommand`、`FormatCommand`、`TestCommand`、`SecurityScanCommand` 等），導致匯出的 `.md` 摘要遺失專案用途、需求關鍵字與客製化組建指令。
- **規格要求**：
  - 於 `Result.cshtml` 的 `ExportMarkdown` `<form>` 內補齊以下隱藏欄位：
    - `<input type="hidden" name="ProjectPurpose" value="@Model.Request.ProjectPurpose" />`
    - `<input type="hidden" name="UploadedDocumentName" value="@Model.Request.UploadedDocumentName" />`（若適用）
    - `@foreach (var kw in Model.Request.RequirementKeywords)` → `<input type="hidden" name="RequirementKeywords" value="@kw" />`
    - `AssemblyOptions.*` 各欄位對應之 `<input type="hidden" name="AssemblyOptions.<Prop>" value="@Model.Request.AssemblyOptions.<Prop>" />`。
- **驗收條件**：
  - 於問券輸入用途、關鍵字與自訂 `AssemblyOptions` 後點擊「匯出推薦摘要 (.md)」，下載的 Markdown 檔案完整包含上述欄位內容。

### 5.2 `UiContractCheckerService.cs` 支援 `[AutoValidateAntiforgeryToken]` 與多行屬性

- **問題根因**：
  - `tools/ECCSELECTION.Mcp/Services/UiContractCheckerService.cs` 的 `ValidateAntiforgeryRegex` 僅比對 `[ValidateAntiForgeryToken]`，未支援類別或方法層級的 `[AutoValidateAntiforgeryToken]`。
  - `CheckControllerAntiforgery` 逐行掃描 `trimmed.StartsWith('[')` 並假設單行完整屬性，遇跨多行屬性（如 `[HttpPost(\n "...")]`）時會提前清空 `pendingAttributes` 而漏檢或誤報。
- **規格要求**：
  1. 更新 `ValidateAntiforgeryRegex`（或新增 `AutoValidateAntiforgeryRegex`）以同時辨識 `[ValidateAntiForgeryToken]` 與 `[AutoValidateAntiforgeryToken]`（忽略大小寫）。
  2. 改進 `CheckControllerAntiforgery` 的屬性收集狀態機：追蹤方括號 `[` 與 `]` 層數（跳過字串常值內的括號），將跨多行的完整屬性合併為單一邏輯屬性項目後再進行類別／方法層級判定。
- **驗收條件**：
  - 類別標註 `[AutoValidateAntiforgeryToken]` 之 Controller 下的 `[HttpPost]` Action 不再誤報 `SEC001`。
  - 跨多行的 `[HttpPost(...)]` 與 `[ValidateAntiForgeryToken]` 可被正確辨識。

### 5.3 `tests/ECCSELECTION.Tests/` 補齊缺失單元測試

- **規格要求**（由測試 Agent 於 `tests/ECCSELECTION.Tests/` 實作）：
  1. **MCP 工具與服務測試（`tests/ECCSELECTION.Tests/Mcp/`）**：
     - `CatalogExportServiceTests`：補齊 `ComponentType` 以字串（`"Agent"`, `"Skill"`, `"Rule"`, `"Command"`）儲存於 SQLite 的真實情境測試，以及 `outputPath` 路徑穿越（`../`、工作區外絕對路徑）拒絕測試。
     - `UiContractCheckerServiceTests`：補齊 `[AutoValidateAntiforgeryToken]` 通過情境與多行屬性（`[HttpPost(...)]`）檢測測試。
     - `PagesSyncServiceTests` & `QualityGateServiceTests`：補齊分支不存在、`dryRun=false` 模擬失敗路徑、無變更略過提交、以及工作區無效或子程序非零結束碼等邊界測試。
     - 新增 `ProcessRunnerTests`（命令執行、逾時與錯誤輸出捕捉）與 `EccSelectionToolsTests`（MCP Tool Wrapper 參數驗證與轉發）。
  2. **Web Controller 與 Service 測試（`tests/ECCSELECTION.Tests/`）**：
     - 新增 `Controllers/` 測試（`HomeControllerTests`、`CatalogControllerTests`、`SyncControllerTests`、`WizardControllerTests`），驗證 HTTP 回應型別、ModelState 驗證與 ViewModel 映射。
     - 新增 `EccCatalogServiceTests`（篩選、分頁/查詢、人工編輯保護）與 `LlmEnrichmentServiceTests`（無 ApiKey 降級、原生 Gemini 格式、無效 `Endpoint` `UriFormatException` 降級、Code Fence 剝除）。
- **驗收條件**：
  - 所有既有與新增 xUnit 測試全數通過（0 失敗、0 略過）。

### 5.4 文件同步

- **規格要求**（由文件同步 Agent 於所有實作與品質閘門通過後執行）：
  1. `docs/specs/SPEC_20261126_ECCSELECTION_MCP.md`：補充 `CatalogExportService` 字串 `ComponentType` 契約、`UiContractCheckerService` 對 `[AutoValidateAntiforgeryToken]` 與多行屬性支援說明。
  2. `README.md`：確認雙版本說明、MCP 工具清單與安裝指令與現況一致。
  3. `VALIDATION_REPORT_ECCSELECTION.md`：記錄本次 Phase 1–3 修復後之實機驗證、品質閘門與測試覆蓋率證據。
  4. `DEVELOPMENT_STATUS.md`：更新最新進度與已修復項目清單。

---

## 6. 品質閘門與驗證要求（全階段完成後必跑）

1. **自動化品質閘門**：執行 `run_quality_gate`（或 `scripts/quality-gate.ps1`），一次通過以下 4 項：
   - `dotnet build ECCSELECTION.slnx -warnaserror`（0 警告、0 錯誤）
   - `dotnet format ECCSELECTION.slnx --verify-no-changes --no-restore`
   - `dotnet test ECCSELECTION.slnx --no-build --verbosity normal --collect:"XPlat Code Coverage"`
   - `npx --yes ecc-agentshield scan --path . --format text`
2. **靜態 UI 契約檢查**：執行 `check_ui_contracts(targetScope="all")`，確認 `ErrorCount == 0`。
3. **實機 UI 驗證**：於 `http://localhost:5109` 驗證 `/Wizard`、`ExportMarkdown` 下載內容及 `docs/index.html` 3D UI 渲染與零 `<table>` 契約。
