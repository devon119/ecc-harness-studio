# SPEC_20261127_ROBLOX_GAME_LUAU_SUPPORT — 強化 Roblox Game / Luau 開發之 ECC 知識庫與問券推薦支援

- **文件編號**：`SPEC_20261127_ROBLOX_GAME_LUAU_SUPPORT`
- **建立日期**：2026-11-27
- **撰寫角色**：SA Agent
- **目標專案**：`ECCSELECTION`（`ECCSELECTION.slnx`）
- **狀態**：已核准實作（13 筆擴充版）

---

## 1. 背景與目標

使用者反映本專案（`ECCSELECTION` / `ecc-harness-studio`）對於開發 **Roblox Game（Luau / Lua）** 的支援度偏低，實際操作時會遇到「選不到可用的 ECC 內容」之問題。

本規格旨在：
1. 盤點本機 SQLite 資料庫、靜態版 `catalog.json`、問券精靈（Wizard）與推薦引擎，確認「選不到」的真正根因。
2. 設計可落地的解決方案，補齊 Roblox / Luau / ECS 專屬 ECC 元件、修正問券篩選與推薦權重，並確保 **本機完整版（`main`）** 與 **線上簡易版（`gh-pages`）** 雙版本皆能開箱即用產出高品質的 Roblox Harness 套件。

---

## 2. 現況盤點與根因分析（實證數據）

### 2.1 資料庫與靜態知識庫盤點
實際查核本機 SQLite 資料庫 `src/ECCSELECTION.Web/eccselection.db`（由 `appsettings.json` 設定 `Data Source=eccselection.db`）與線上靜態版資料 `docs/data/catalog.json`：

- **總元件數**：兩者皆為 **620 筆**（`IsActive = 1` 共 620 筆：Agent 72、Command 99、Rule 121、Skill 328）。
- **`TechStacks` 分布統計（啟用元件）**：
  - `common`: 408、`python`: 63、`go`: 44、`react`: 41、`java`: 23、`kotlin`: 23、`vue`: 21、`dotnet`: 15、`postgres`: 12、`sqlite`: 6、`mysql`: 6、`typescript`: 5。
  - **`luau`: 0 筆、`lua`: 0 筆、`roblox`: 0 筆、`cpp`: 0 筆、`godot`: 0 筆**。
- **全文關鍵字查核（`Name` / `Path` / `DescriptionEn`）**：
  - 含 `roblox` 或 `luau` 之元件：**0 筆**。
  - 僅有 3 筆 Godot/GDScript 元件（`gdscript-reviewer`、`godot-patterns`、`godot-testing`），且其 `TechStacks` 皆為 `common`。
- **`ArchStyles` 分布統計**：
  - `general`: 586、`clean`: 32、`mvc`: 2，**`ecs`: 0 筆**。

### 2.2 問券精靈（Wizard）、相容性矩陣與推薦引擎追蹤
1. **UI 與相容性矩陣已有入口，但內容空缺**：
   - `src/ECCSELECTION.Web/Services/WizardCompatibility.cs:56-61` 已定義 `Game`（`DefaultArchStyle: "ECS"`、`DefaultTechStacks: ["luau"]`、`AllowedTechStacks: ["luau", "godot", "dotnet", "cpp", "sqlite"]`）。
   - `src/ECCSELECTION.Web/Services/RequirementAnalyzerService.cs:60,73,456-459` 已能辨識 `roblox` / `luau` 並推斷 `Roblox Studio / Luau` 工具鏈（`selene src/`、`stylua --check src/`、`busted tests/`）。
   - `src/ECCSELECTION.Web/Views/Wizard/Index.cshtml:11` 磁貼副標題僅寫 `"遊戲開發 / Unity / Godot"`，未標註 Roblox（對比 `docs/index.html:348` 有標註 `Roblox`）。
2. **推薦演算法實測模擬（`Game + ECS + luau + Greenfield + Compact`, `minScore = 50`）**：
   - `RecommendationService.cs:274-292`（`IsTechStackCompatible`）：因 `luau` 專屬元件為 0 筆，`matchedStacks.Count` 恆為 `0`，只能退回 408 筆 `common` 元件。
   - `RecommendationService.cs:586-601`（`ComputeProjectTypeBonus`）：對 `Game` 型態給予 `component.Name.StartsWith("gan-")` +15 分；加上 `GreenfieldKeywords` +20 分。
   - **實際推薦結果失準**：Lead Agent 前 3 名為 `gan-generator`（95 分）、`gan-planner`（95 分）、`gdscript-reviewer`（95 分，為 Godot 而非 Roblox）；`PG_FE` 角色推薦 `a11y-architect`（WCAG 網頁無障礙）與 `angular-developer`（Angular 前端框架，因 `TechStacks=common` 且含關鍵字）。**完全沒有任何 Roblox Studio / Luau / RemoteEvent / DataStore / Rojo / Selene 相關 ECC 元件可用**。
3. **Catalog 篩選器缺漏**：
   - `src/ECCSELECTION.Web/Views/Catalog/Index.cshtml:6` 與 `docs/index.html:238-253` 的「技術棧」下拉選單皆未列出 `luau`、`godot`、`cpp`，使用者在知識庫頁面直接無法選擇 `luau` 篩選。
4. **Parser 架構辨識缺漏**：
   - `src/ECCSELECTION.Web/Services/EccParserService.cs:315-337`（`InferArchStyles`）僅辨識 `mvc`、`clean`、`mvvm`，未辨識 `ecs`。

### 2.3 雙版本影響評估（`main` 完整版 vs `gh-pages` 線上簡易版）
- **`docs/data/catalog.json`** 與 SQLite 同源，同樣缺乏 Roblox / Luau / ECS 元件（0 筆）。
- **`docs/app.js:364-472`**（`recommendDeterministic`）未實作 `projectType`（`Game`）與 `lifecycle` 加分，且 **`docs/app.js:712`** 與 **`src/ECCSELECTION.Web/Services/HarnessBundleService.cs:154`** 皆假設元件內容可透過 `RawUrl` 自 `affaan-m/ECC` 下載。若新增 Studio 內建精選元件，必須同時支援「本機/靜態站內建檔案讀取」與「GitHub 同步不誤刪內建元件」。

---

## 3. 影響範圍與寫入邊界

- **後端實作邊界**（僅可寫入 `src/ECCSELECTION.Web/Services/`、`Models/`、`Data/`、`Program.cs`、`BuiltInEcc/`）：
  - `Data/BuiltInEccSeeder.cs`（新增）、`Program.cs`（啟動呼叫 Seeder）
  - `BuiltInEcc/roblox/...`（新增 13 份內建 Roblox/Luau Markdown 元件檔）
  - `Services/GitHubSyncService.cs`（保護 `builtin/` 元件不被同步下架）
  - `Services/HarnessBundleService.cs`（支援讀取 `builtin/` 本地檔案打包進 `.zip`）
  - `Services/EccParserService.cs`（`InferArchStyles` 補齊 `ecs`；`InferTechStacks` 補齊 `builtin/roblox/`、`rules/luau/`）
  - `Services/RecommendationService.cs`（優化 `Game` + `luau` 推薦權重與 Suite 配對）
- **前端 / MVC 邊界**（僅可寫入 `src/ECCSELECTION.Web/Controllers/`、`Views/`、`wwwroot/`）：
  - `Views/Catalog/Index.cshtml`（技術棧下拉選單補齊 `luau`、`godot`、`cpp`）
  - `Views/Wizard/Index.cshtml`（`Game` 磁貼副標題補註 `Roblox`）
- **靜態線上版邊界（`docs/`）**：
  - `docs/builtin-ecc/roblox/...`（對應 13 份靜態 Markdown 檔）
  - `docs/index.html`（`#catStack` 補齊 `luau`、`godot`、`cpp`）
  - `docs/app.js`（對齊 `Game`/`luau` 推薦加權、支援 `./builtin-ecc/...` 本地抓取打包）
  - `docs/data/catalog.json`（透過 `export_catalog_json` 重新匯出）
- **測試邊界**：`tests/ECCSELECTION.Tests/`

---

## 4. 解決方案與分層實作規格

### 4.1 Studio 內建精選 Roblox / Luau ECC 元件（共 13 筆）
不更動既有 620 筆上游元件，於專案內建置並冪等播種 13 筆 Roblox / Luau 專屬 ECC 元件（覆蓋全部 SDLC 角色與 4 種元件類型）：

| 類型 | 名稱 | Path | SdlcRoles | TechStacks | ArchStyles | 核心內容 |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| Agent | `roblox-luau-architect` | `builtin/roblox/agents/roblox-luau-architect.md` | `SA,SD,PG_BE` | `luau` | `ecs,clean` | Server-Client 邊界、Rojo 結構、ECS/Knit 模組化與 Remote 契約 |
| Agent | `roblox-luau-reviewer` | `builtin/roblox/agents/roblox-luau-reviewer.md` | `PG_FE,PG_BE,PG_TEST,QA` | `luau` | `ecs,general` | `--!strict` 型別、Connection 洩漏檢查、Server 權限驗證、Selene/StyLua |
| Rule | `luau-strict-coding` | `builtin/roblox/rules/luau-strict-coding.md` | `SA,SD,PG_FE,PG_BE,PG_TEST,QA` | `luau` | `ecs,general` | `--!strict`、`task.*` 取代舊版 `wait/spawn`、Trove/Maid 清理規範 |
| Rule | `roblox-server-security` | `builtin/roblox/rules/roblox-server-security.md` | `SA,SD,PG_BE,PG_DB,QA` | `luau` | `ecs,general` | 永不信任 Client、RemoteEvent Rate Limit & 型別驗證、DataStore 併發鎖 |
| Rule | `roblox-ecs-patterns` | `builtin/roblox/rules/roblox-ecs-patterns.md` | `SA,SD,PG_FE,PG_BE` | `luau` | `ecs` | CollectionService Tags、ReplicatedStorage/ServerScriptService 職責分離 |
| Skill | `roblox-datastore-patterns` | `builtin/roblox/skills/roblox-datastore-patterns/SKILL.md` | `SD,PG_BE,PG_DB` | `luau` | `ecs,clean` | `UpdateAsync` 重試退避、Session Lock、`BindToClose` 安全存檔、Schema 遷移 |
| Skill | `roblox-luau-testing` | `builtin/roblox/skills/roblox-luau-testing/SKILL.md` | `PG_TEST,QA` | `luau` | `ecs,general` | TestEZ / Busted 單元測試、Selene 靜態檢查、StyLua 格式與 Service Mock |
| Command | `roblox-check` | `builtin/roblox/commands/roblox-check.md` | `PG_BE,PG_TEST,QA` | `luau` | `ecs,general` | 一鍵執行 `selene src/`、`stylua --check src/`、`busted tests/` 與安全檢查 |
| Skill | `roblox-ui-patterns` | `builtin/roblox/skills/roblox-ui-patterns/SKILL.md` | `PG_FE,SD` | `luau` | `ecs,general` | React-Lua / Fusion 宣告式 UI、SafeArea、TweenService 動畫與 UI Scale 響應式設計 |
| Skill | `roblox-rojo-wally-workflow` | `builtin/roblox/skills/roblox-rojo-wally-workflow/SKILL.md` | `SA,SD,PG_BE` | `luau` | `ecs,clean` | Rojo `default.project.json` 映射、Wally 套件管理、Aftman 工具鏈與 CI/CD 自動部署 |
| Rule | `roblox-combat-physics` | `builtin/roblox/rules/roblox-combat-physics.md` | `PG_BE,PG_FE,QA` | `luau` | `ecs,general` | Client 預判 + Server Hitbox 驗證、Raycast/SpatialQuery 最佳實踐與防作弊校驗 |
| Skill | `roblox-network-performance` | `builtin/roblox/skills/roblox-network-performance/SKILL.md` | `SD,PG_BE,PG_FE,QA` | `luau` | `ecs,general` | StreamingEnabled 設計、UnreliableRemoteEvent 頻寬壓縮、ScriptProfiler 與記憶體洩漏排查 |
| Rule | `roblox-monetization-patterns` | `builtin/roblox/rules/roblox-monetization-patterns.md` | `PG_BE,PG_DB,QA` | `luau` | `ecs,general` | MarketplaceService `ProcessReceipt` 冪等防重複發貨、GamePass 快取與交易稽核 |

#### 資料契約細節
- `Sha` 格式：`studio-builtin-v1-<slug>`（固定值，長度 <= 64）。
- `RawUrl`：`https://raw.githubusercontent.com/devon119/ecc-harness-studio/main/docs/builtin-ecc/<subpath>`（瀏覽器點開 Raw 可直接檢視）。
- `HtmlUrl`：`https://github.com/devon119/ecc-harness-studio/blob/main/docs/builtin-ecc/<subpath>`。
- **離線打包優先**：`HarnessBundleService` 偵測 `component.Path.StartsWith("builtin/", StringComparison.OrdinalIgnoreCase)` 時，直接從 `ContentRootPath/BuiltInEcc/<subpath>` 讀取檔案（不需對外 HTTP 請求）；`docs/app.js` 則優先從 `./builtin-ecc/<subpath>` 抓取。

### 4.2 後端邏輯調整
1. **`Data/BuiltInEccSeeder.cs` + `Program.cs`**：
   - 於 `db.Database.EnsureCreated()` 後執行 `BuiltInEccSeeder.SeedAsync(db)`。
   - 依 `Path` 判斷：僅在資料庫尚無該 `Path` 時插入；若已存在且 `IsManuallyEdited == true`，絕對不得覆寫。
2. **`Services/GitHubSyncService.cs`**：
   - 於下架比對迴圈（原 line 156）加入條件：`!existing.Path.StartsWith("builtin/", StringComparison.OrdinalIgnoreCase)`，確保同步上游 `affaan-m/ECC` 時不會停用 Studio 內建元件。
3. **`Services/EccParserService.cs`**：
   - `InferArchStyles` 新增 `@"\b(ecs|entity[-_\s]component)\b"` -> `"ecs"`。
   - `InferTechStacks` 新增 `normalizedPath.StartsWith("builtin/roblox/", ...)` 或 `rules/luau/` -> `"luau"`。
4. **`Services/RecommendationService.cs`**：
   - `ComputeProjectTypeBonus`：當 `projectType == "Game"` 且 `selectedStacks` 含 `luau` 時：
     - 元件若命中 `luau` / `roblox` / `ecs` 或名稱含 `roblox`/`luau`，給予 `+20` 分（取代原本僅 +15）。
     - `gan-` 前綴僅在非 `luau` 專屬情境或無 `roblox`/`luau` 元件競爭時才維持原本加分（或降為 `+5`），避免 `gan-generator`、`gan-planner` 擠掉 `roblox-luau-architect` 與 `roblox-luau-reviewer`。
   - `EvaluateSuitePairings`：傳入 `selectedProjectType` 與 `selectedArch`，當候選元件命中 `selectedStacks`（如 `luau`）或 `selectedArch`（如 `ecs`）時額外加分，確保 `roblox-luau-architect` 的 Suite 優先配對 `roblox-luau-testing`、`roblox-datastore-patterns`、`luau-strict-coding`、`roblox-server-security`、`roblox-check`。

### 4.3 前端與靜態版調整
1. **`Views/Wizard/Index.cshtml`**：`Game` 磁貼副標題改為 `"遊戲開發 / Roblox / Unity / Godot"`。
2. **`Views/Catalog/Index.cshtml` & `docs/index.html`**：技術棧下拉選單補齊 `luau`、`godot`、`cpp`。
3. **`docs/app.js`**：
   - `recommendDeterministic(state)` 補齊 `Game`/`luau`/`roblox` 與 `Greenfield`/`Brownfield` 加權，與後端 `RecommendationService` 行為一致。
   - `downloadHarnessZip()` 偵測 `c.Path.startsWith("builtin/")` 時改為 `fetch("./builtin-ecc/" + c.Path.slice("builtin/".length))`。

---

## 5. 安全性設計

- `HarnessBundleService` 讀取 `BuiltInEcc/` 本地檔案時，必須驗證相對路徑不含 `..` 且透過 `Path.GetFullPath` 確認最終路徑位於 `BuiltInEcc` 根目錄之內，防止路徑遍歷（Path Traversal）。
- 所有既有 Controller POST/PUT/DELETE 維持 `[ValidateAntiForgeryToken]`。
- 嚴禁空 `catch` 區塊或硬編碼密鑰；檔案讀取失敗或 HTTP 失敗須記錄日誌並寫入對應錯誤清單。

---

## 6. 單元測試計畫（`tests/ECCSELECTION.Tests/`）

1. **`BuiltInEccSeederTests`**：
   - 驗證空資料庫執行兩次 `SeedAsync` 皆只產生 13 筆 `builtin/roblox/...` 元件（冪等性），且不覆寫 `IsManuallyEdited == true` 之人工紀錄。
2. **`GitHubSyncServiceTests`**：
   - 驗證執行同步時，上游未包含的 `builtin/roblox/...` 元件不會被設為 `IsActive = false`。
3. **`RecommendationServiceTests`**：
   - 驗證選取 `Game + ECS + luau + Greenfield + Compact` 時，Lead Agent 首選包含 `roblox-luau-architect` 與 `roblox-luau-reviewer`（而非僅 `gan-*` / `gdscript-reviewer`），且每個主要 SDLC 角色皆有對應的 Roblox/Luau 元件入選。
4. **`HarnessBundleServiceTests`**：
   - 驗證選取 `builtin/roblox/...` 元件打包 `.zip` 時，無需對外網請求即可成功寫入 `.claude/...`（或 `.ecc/...`）且無 `DOWNLOAD_ERRORS.txt`。
5. **`EccParserServiceTests`**：
   - 驗證含 `ECS` / `Entity Component` 關鍵字之元件能正確推斷 `ArchStyles` 包含 `ecs`。

---

## 7. 驗收條件（Acceptance Criteria）

1. 本機完整版（`http://localhost:5109`）於 `/Catalog` 選擇技術棧 `luau` 或搜尋 `roblox`，可查得至少 13 筆啟用元件（涵蓋 Agent、Skill、Rule、Command）。
2. 於 `/Wizard` 選擇 `Game` + `ECS` + `luau`（或於需求欄輸入「開發 Roblox 遊戲」並自動分析），推薦結果 Lead Agent 優先呈現 `roblox-luau-architect` 與 `roblox-luau-reviewer`，且可成功下載含完整 `.md` 與 `selene`/`stylua`/`busted` 品質閘門之 `.zip`。
3. `docs/data/catalog.json`、`docs/index.html`、`docs/app.js`、`docs/builtin-ecc/` 同步完成，靜態版亦可篩選 `luau` 並推薦與打包 Roblox Harness。
4. 執行 `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1` 一次通過全部 4 項檢查（`dotnet build -warnaserror`、`dotnet format --verify-no-changes`、`dotnet test`、`ecc-agentshield scan`）。

---

## 8. 人類確認閘門（Human-in-the-Loop Checklist）

- [x] 確認新增 13 筆 Studio 內建精選 Roblox / Luau ECC 元件清單與資料契約。
- [x] 確認後端、前端、靜態版與測試之寫入邊界與驗收條件。
- [x] 核准進入實作與測試階段。
