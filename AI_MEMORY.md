# AI_MEMORY.md — ECCSELECTION 專案長期記憶

> 本文件為跨 Session 的專案真理之源，記錄技術決策、核心契約與關鍵限制。

---

## 1. 專案基本定位
- **專案名稱**：`ECCSELECTION`（ECC Harness Selector Web App）
- **專案路徑**：`D:\T01_GIT\ECCSELECTION`
- **技術棧**：ASP.NET Core MVC（`.NET 10`）+ EF Core + SQLite（`eccselection.db`）+ Bootstrap 5 + xUnit（`ECCSELECTION.Tests`）
- **使用者主力技術棧偏好**：.NET Web MVC、Android（Kotlin/Java）、MS SQL Server / SQLite、MVC 架構。

---

## 2. 核心架構與資料契約（不可破壞）
1. **GitHub 增量同步（`GitHubSyncService`）**：
   - 來源：`affaan-m/ECC`（`git/trees/main?recursive=1` + `raw.githubusercontent.com/affaan-m/ECC/main/...`）。
   - 以 `EccComponents.Path` 為唯一索引、`Sha` 為變更比對基準。
   - **人工編輯保護**：`IsManuallyEdited == true` 的元件在同步時不得覆蓋 `SummaryZh`、`SdlcRoles`、`TechStacks`、`ArchStyles` 與 `UsageGuide`。
   - **軟刪除**：GitHub 移除的檔案將 `IsActive` 設為 `false`，保留歷史紀錄。
   - **非阻塞降級**：未設定 `LlmEnrichment:ApiKey` 時純走 YAML Frontmatter + 確定性規則推斷，不得阻塞同步。
2. **確定性問券配對與相容性契約（`RecommendationService` / `WizardCompatibility`）**：
   - 依 SDLC 角色（`SA`, `SD`, `PG_FE`, `PG_BE`, `PG_DB`, `PG_TEST`, `QA`）、技術棧（含衝突語言排除與 `common` 加權）、架構風格與模式（`Compact` / `Comprehensive`）進行可預期計分，不依賴執行期 LLM。
   - **專案型態連動相容性（`WizardCompatibility`）**：作為前後端單一真理來源，限制 `ProjectType`（`Web` / `Mobile` / `API` / `Fullstack` / `Game`）與 `ProjectLifecycle`（`Greenfield` / `Brownfield`）對應之合法 `ArchStyle`（含 `ECS`）、`TechStacks`（含 `luau`, `godot`, `cpp`）與 `SdlcRoles`，前端即時停用不適用選項，後端 `WizardController` 強制驗證。
   - **以 Agent 為核心的配套套組與 ZIP 下載**：`RecommendationService.BuildAgentSuites` 以 Lead Agent 為核心自動配對 Skill / Rule / Command；`HarnessBundleService` 依使用者勾選的 `SelectedComponentIds` 從 GitHub Raw URL 下載真實檔案，按 `.claude/` 建議路徑打包成 `ecc-harness-bundle.zip`（含 `README_HARNESS.md`，若個別失敗附註 `DOWNLOAD_ERRORS.txt`）。
3. **錯誤處理鐵律**：
   - 嚴禁空的 `catch` 區塊；外部 HTTP（GitHub / LLM）失敗必須記錄並寫入 `SyncLogs.ErrorMessage`。

---

## 3. 本機專案專屬 Harness（`.ecc/`）與 Antigravity 自動載入
- `.ecc/` 內已精選並在地化調整 62 個檔案（`agents/` 13、`skills/` 16、`rules/` 23、`commands/` 9、`README.md` 1）。
- 最高專案規則：`.ecc/rules/project/eccselection-mvc.md`（鎖定 MVC 結構與 `dotnet build` / `dotnet test` 指令）。
- **Antigravity 自動載入規則**：`AGENTS.md` 與 `.agents/rules/eccselection-workflow.md`（`always_on`），強制所有新對話採**「專責協調員監控 ＋ 專業 Agent 唯讀邊界隔離」**模式，完整執行 **7 步流程（嚴禁跳步）**：`SA 規格（SPEC_ECCSELECTION.md）→ 人類確認閘門（強制暫停）→ 分層實作（後端／前端）→ TDD 測試（tests/ECCSELECTION.Tests/）→ 審查與強制品質閘門（scripts/quality-gate.ps1）＋ 實機驗證（http://localhost:5109）→ 文件同步（DEVELOPMENT_STATUS.md）`。
- **強制品質閘門防堵（`scripts/quality-gate.ps1`）**：任何程式碼異動後必跑 `powershell -ExecutionPolicy Bypass -File scripts/quality-gate.ps1`（涵蓋 `dotnet build -warnaserror`、`dotnet format --verify-no-changes`、`dotnet test` 含覆蓋率、`npx ecc-agentshield scan`），並在最終回覆逐項附上 4 項實測結果；執行前若 `ECCSELECTION.Web` 正在背景執行需先停止以避免 `apphost.exe` 鎖檔。
- 本機開發啟動網址：`http://localhost:5109`（定義於 `src/ECCSELECTION.Web/Properties/launchSettings.json`）。

---

## 4. v1.3 – v1.4 關鍵決策與線上發佈狀態（2026-11-26）
- **協作規範偏好**：重大架構或規則變動前，須先與使用者討論對齊設計方向後再實作。
- **需求解析與指令自動產出（`RequirementAnalyzerService`）**：支援用途說明與需求文件上傳（`.md`/`.txt`/`.json`/`.yaml`/`.csv`/`.xml`），採「LLM 優先＋確定性非阻塞降級」自動推斷技術棧、關鍵字與工程 CLI 指令（`TargetEnvironment`、`BuildCommand`、`FormatCommand`、`TestCommand`、`SecurityScanCommand`）；UI 僅顯示直覺欄位，CLI 指令收納於進階可收合區。
- **收斂推薦與 Gemini 雙軌精選（`GeminiCurationService`）**：
  - `Compact` 模式嚴格收斂為最多 3 套 Agent、每套最多 **2 Skill / 2 Rule / 1 Command**；未入選達標項目列於「其他候選元件」供手動加勾，且每組必須顯示「適用角色」。
  - 支援 **Gemini API 自動精選**（金鑰本機存於 `.gitignore` 排除之 `appsettings.Development.local.json`，線上靜態版則存於瀏覽器 `localStorage`）及 **Chrome 網頁版 Gemini 接力模式**（複製 Prompt 開啟 `gemini.google.com` 並貼回 JSON 套用）。
- **防堵卡點組建與運作說明書（`HarnessBundleService`）**：
  - 單一目錄輸出（`All` 與 `ClaudeCode` 輸出至 `.claude/`，純 `Antigravity` 輸出至 `.ecc/`），不重複鏡像。
  - `Skill` 打包時透過 GitHub Trees API 一併下載 `skills/<name>/` 底下的附屬輔助檔（`references/`、`scripts/`、`examples/`、`resources/`）。
  - `.claude/settings.json` 透過 `permissions.deny` 阻擋敏感路徑（`ProtectedPaths`）與 `rm -rf`；`HARNESS_PLAYBOOK.md` 必須完整寫入 5 項防堵卡點狀態（規格先行卡點、品質閘門腳本、證據輸出契約、Git Pre-Commit 卡點、IDE 自動載入規則 & Hooks）。
- **線上簡易版與完整版發佈**：
  - 品牌名稱：**ECC Harness Studio**。
  - 線上靜態版倉庫：`https://github.com/devon119/ecc-harness-studio`（GitHub Pages 網址：`https://devon119.github.io/ecc-harness-studio/`），內含 `docs/data/catalog.json`（620 筆元件）及完整版一鍵安裝包（`docs/install-eccselection.ps1`、`.sh`、`docs/downloads/eccselection-full.zip`）。

---

## 5. v1.5 多代理協作治理與建議包內建規範（2026-11-26）
- **專責協調員 ＋ 7 種角色唯讀寫入邊界**：
  - 主 Agent 僅擔任專責協調員（拆解、派發、監控、驗收，不越權直接修改業務程式碼、測試或規格）。
  - 各專責子代理寫入邊界嚴格隔離：SA（僅 `SPEC_ECCSELECTION.md` 或增量規格檔）、後端（僅 `Services/`、`Models/`、`Data/`、`Program.cs`）、前端（僅 `Controllers/`、`Views/`、`wwwroot/`）、測試（僅 `tests/ECCSELECTION.Tests/`）、審查與驗證（全專案唯讀）、文件同步（僅 `DEVELOPMENT_STATUS.md`）。
- **SA 後人類確認閘門與嚴禁跳步**：SA / 規格產出後**必須暫停呈報摘要並取得人類明確確認**才可進入實作；每項需求須完整走完 7 步流程，禁止 AI 自行略過任何步驟。
- **建議包內建協作治理原則（`HarnessBundleService` v1.5）**：不新增額外開關，直接於匯出之 `AGENTS.md`、`CLAUDE.md`、`.agents/rules/workflow-guardrails.md` 與 `README_HARNESS.md`（永遠產出）寫入上述協調員邊界、人類確認閘門、7 步流程與新手操作指引。
- **規格維護成本最佳化建議**：`SPEC_ECCSELECTION.md` 作為總覽與核心契約索引；後續 `v1.6+` 獨立新功能可改採增量規格檔（如 `docs/specs/v1.6-xxx.md`）並在總覽補上索引連結，以節省 SA 讀寫 token 成本。
