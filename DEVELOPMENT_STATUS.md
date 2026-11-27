# DEVELOPMENT_STATUS.md — ECCSELECTION 開發進度與驗收狀態

> 最後更新：2026-11-27

---

## 2026-11-27 Roblox Game / Luau 支援擴充（13 筆內建精選元件）

- **規格依據**：`docs/specs/SPEC_20261127_ROBLOX_GAME_LUAU_SUPPORT.md`（已核准並完成實作與驗證）
- **1. 內建精選元件與冪等播種**：
  - 新增 13 份 Studio 內建 Roblox / Luau ECC 元件（涵蓋 Agent 2、Command 1、Rule 5、Skill 5），同步置於 `src/ECCSELECTION.Web/BuiltInEcc/roblox/`（本機完整版）與 `docs/builtin-ecc/roblox/`（線上靜態版）。
  - 新增 `src/ECCSELECTION.Web/Data/BuiltInEccSeeder.cs`（並於 `Program.cs` 啟動時執行），支援依 `Path` 冪等播種，且嚴格保護 `IsManuallyEdited == true` 之人工編輯欄位不被覆蓋。
- **2. 後端服務與安全性強化**：
  - `GitHubSyncService.cs`：保護 `builtin/` 前綴元件於同步上游 `affaan-m/ECC` 時不被自動下架。
  - `HarnessBundleService.cs`：支援優先從本機 `BuiltInEcc/` 離線讀取 `builtin/` 元件打包 `.zip`，並實作 Path Traversal 防護。
  - `EccParserService.cs`：擴充 `ecs`（`InferArchStyles`）與 `luau`（`InferTechStacks`）自動推斷規則。
  - `RecommendationService.cs`：強化 `Game` + `luau` 推薦權重與 `EvaluateSuitePairings` Suite 配對優化，確保優先推薦 Roblox / Luau 專屬元件。
- **3. MVC 前端與靜態線上版同步更新**：
  - 本機 MVC：`Views/Catalog/Index.cshtml` 技術棧篩選補齊 `luau` / `godot` / `cpp`；`Views/Wizard/Index.cshtml` `Game` 磁貼補註 `Roblox`。
  - 線上靜態版：`docs/index.html`、`docs/app.js`（支援 `Game`/`luau` 推薦加權與 `./builtin-ecc/` 本地抓取打包）、`docs/data/catalog.json` 重新匯出（總計 **633 筆**啟用元件，含 **13 筆 `luau`**）。
- **4. 測試、契約檢查與品質閘門實測結果**：
  - `scripts/quality-gate.ps1` 4 項檢查全數通過：
    1. `dotnet build ECCSELECTION.slnx -warnaserror`：0 警告、0 錯誤
    2. `dotnet format ECCSELECTION.slnx --verify-no-changes --no-restore`：通過
    3. `dotnet test ECCSELECTION.slnx --no-build --verbosity normal --collect:"XPlat Code Coverage"`：**162 / 162 通過**
    4. `npx --yes ecc-agentshield scan --path . --format text`：Grade A（100/100），0 findings
  - `check_ui_contracts`：0 違規（0 errors、0 warnings）。

---

## 目前進度：全專案深度稽核修復（Phase 1–3）已完成並通過品質閘門與實機驗證

| 階段 | 狀態 | 說明與證據 |
| :--- | :--- | :--- |
| **SA / SD 規格與規範** | ✅ 已核准並完成實作 | - Phase 1–3 稽核修復規格：`docs/specs/SPEC_20261127_FULL_AUDIT_REMEDIATION.md`（已核准並完成實作與驗證）<br>- MCP Server 規格：`docs/specs/SPEC_20261126_ECCSELECTION_MCP.md`（已實作並通過驗證）<br>- 核心基線：`SPEC_ECCSELECTION.md` v1.6 |
| **Phase 1–3 實作與修復** | ✅ 已完成 | **Phase 1（High）**：<br>- `CatalogExportService.cs` 修復為以字串讀取 `EccComponents.ComponentType`（相容 `HasConversion<string>()` 與舊版整數格式）<br>- `main:docs/`（`index.html`、`app.js`、`css/icon3d.css`）對齊 `gh-pages` v1.6 3D 空間視覺化 UI（零 `<table>`）<br>**Phase 2（Medium）**：<br>- `WizardController.cs` 瘦身並消除重複 `RecommendAsync` 呼叫；`LocalLlmConfigService` 接管設定檔寫入<br>- `GitHubSyncService.cs` 強化失敗日誌（`ChangeTracker.Clear()` + `CancellationToken.None`）與單檔超時降級 `PartialSuccess`<br>- 抽出 `LlmClientHelper` 統一 Code Fence 剝除與原生 Gemini API 支援；`HarnessBundleService` 與 `RecommendationService` 重構與排序統一<br>**Phase 3（Low）**：<br>- `Wizard/Result.cshtml` `ExportMarkdown` 表單補齊 28 個隱藏欄位（含用途、關鍵字與自訂 `AssemblyOptions`）<br>- `UiContractCheckerService.cs` 支援 `[AutoValidateAntiforgeryToken]` 與跨多行屬性掃描<br>- `tools/ECCSELECTION.Mcp/` 5 項工作流程 MCP Tools（`eccselection`）持續維運 |
| **TDD 與單元測試** | ✅ 通過 | - `tests/ECCSELECTION.Tests/` 補齊 MCP、Controllers、Services 單元測試<br>- 全專案測試總計：**152 / 152 通過**（0 失敗、0 略過） |
| **品質閘門（Quality Gate）** | ✅ 通過 | `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1`（2026-11-27）4 項檢查全數通過：<br>1. `dotnet build ECCSELECTION.slnx -warnaserror`：0 警告、0 錯誤<br>2. `dotnet format ECCSELECTION.slnx --verify-no-changes --no-restore`：通過<br>3. `dotnet test ECCSELECTION.slnx --no-build --verbosity normal --collect:"XPlat Code Coverage"`：**152 / 152 通過**<br>4. `npx --yes ecc-agentshield scan --path . --format text`：Grade A（100/100），0 findings |
| **靜態契約與真實 GitHub 同步** | ✅ 通過 | - `check_ui_contracts(targetScope="all")`（2026-11-27）：掃描 **54** 個檔案，**0 errors、0 warnings**<br>- 已同步 620 筆元件入 `eccselection.db`（Agent 72、Command 99、Rule 121、Skill 328） |
| **實機端對端與 UI 驗證** | ✅ 通過 | - **Web UI 實機驗證（`http://localhost:5109`，2026-11-27）**：`/`、`/Catalog`、`/Sync`、`/Wizard`、`/Wizard/Recommend` 均回傳 HTTP 200、`<table>` 數為 0、0 Console Error、3D SVG 圖示正常渲染；`ExportMarkdown` 表單 28 個隱藏欄位驗證通過<br>- **MCP Stdio JSON-RPC 實測**：`eccselection` 工具鏈運作正常 |
| **專案 Harness 在地化與自動載入** | ✅ 已完成 | `.ecc/` 內含 62 個檔案；`AGENTS.md` 與 `.agents/rules/eccselection-workflow.md` 供 Antigravity 自動載入 |
| **驗收審計與放行** | ✅ 已核准並驗證 | 驗收報告：`VALIDATION_REPORT_ECCSELECTION.md`（已更新 2026-11-27 Phase 1–3 實測證據） |

---

## 已知非阻塞提醒（後續可優化項目）
1. **v1.6 UI 視覺微調（非功能阻斷）**：`/Wizard/Recommend` 超長名稱卡片右側 `.i3d-score-gauge` 邊界微調；`/Catalog` 篩選列按鈕文字不折行。
2. **首次全量同步耗時**：從 GitHub 逐檔抓取 620 筆檔案約需 2 分鐘，`/Sync` 頁面可考慮補充等候提示或進度條。
3. **中斷紀錄**：若同步中途連線逾時，會在 `SyncLogs` 留下一筆 `Status = Running`、`CompletedAt = NULL` 的紀錄（不影響後續再次執行同步）。
4. **尚未實機覆蓋項目**：實機二次增量同步。
